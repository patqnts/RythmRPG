// GPU grass for GrassField.cs. One procedural draw per (chunk, sprite variant); every blade is read from a
// StructuredBuffer by SV_InstanceID, so there are no GameObjects, no per-blade CPU work and no matrices.
// Blades are upright quads facing the camera's yaw (1:1 under the ObliqueProjection), bent by wind and by the
// GrassInteractionMap (trampling, shockwaves), optionally in whole-pixel steps for crisp pixel art.
// Cutting, burning and regrowth come from a per-blade state buffer (_GrassStates, see GrassField.Burning.cs):
// the shader animates the fire, the curling and the regrowth from the times stored there, in whole sprite rows.
// With _GRASS_PIECES (a second draw of the chunks where something was just cut) it draws the severed tops
// instead (_GrassCuts): each severed top is broken into up to 4 x 2 fragments by its size, and every fragment
// is thrown off, flutters, tumbles and dissolves pixel by pixel in mid-air. Each blade is drawn
// GRASS_PIECE_FRAGMENTS times in that draw; the fragments a small piece does not need collapse to nothing.
Shader "Hidden/RythmRPG/PixelGrass"
{
    Properties
    {
        _BaseMap ("Sprite Texture", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    struct GrassBlade
    {
        float4 positionScale; // xyz = root (world), w = scale
        float4 data;          // x = flip (+1 / -1), y = shade, z = random 0..1, w = unused
    };

    StructuredBuffer<GrassBlade> _GrassBlades;
    float _GrassInstanceOffset; // first blade of this draw in _GrassBlades

    TEXTURE2D(_BaseMap);
    SAMPLER(sampler_BaseMap);

    TEXTURE2D(_GrassInteractionTex);
    SAMPLER(sampler_GrassInteractionTex);
    float4 _GrassInteractionParams; // xy = map corner XZ, z = 1 / size, w = 1 when active

    // Per draw (MaterialPropertyBlock): the variant's sprite.
    float4 _SpriteUV;     // xy = uv min, zw = uv size
    float4 _SpriteSize;   // xy = size in world units, zw = pivot (0..1)

    // Per field (material).
    float _Cutoff;
    float4 _GrassRight;       // xyz = world direction blades are wide along (camera right, flattened)
    float4 _Wind;             // xy = direction XZ (normalised), z = strength (units), w = speed
    float4 _WindShape;        // x = wave frequency, y = gust strength, z = gust scale, w = bend height (units)
    float4 _Push;             // x = push bend (units), y = flatten (0..1), z = trample darken, w = pixels per unit
    float4 _PixelOptions;     // x = snap bend to pixels (0/1)
    float4 _ColorA;
    float4 _ColorB;
    float4 _Patch;            // x = patch scale, y = shade variation, z = normal up blend, w = light bands (0 = smooth)

    // Cut / burn state, one per blade (same order as _GrassBlades):
    // x = height left (0..1), y = ignite time (< 0 = none), z = regrow start time (< 0 = none), w = char (0..1).
    StructuredBuffer<float4> _GrassStates;
    float4 _SpriteExtra;      // per draw: x, y = lowest / highest row the grass covers (quad v), z = sprite height, w = width (texels)
    float4 _GrassTime;        // x = the game time the states are written in
    float4 _Burn;             // x = burn duration, y = ash height, z = ember time
    float4 _Fire;             // x = flame rows above the edge, y = glowing rows below it, z = flicker fps, w = ember density
    float4 _Regrow;           // x = regrow duration
    float4 _FlameColor;
    float4 _FlameTipColor;
    float4 _CharColor;
    float4 _AshColor;
    float4 _Curl;             // x = how far burning grass curls over (0 = stays straight)

    // Severed tops, one per blade: x = cut time (< 0 = none), y / z = bottom / top of the piece (0..1 of the tuft),
    // w = flight direction (radians, world XZ).
    StructuredBuffer<float4> _GrassCuts;
    float4 _Pieces;           // x = lifetime, y = sideways speed, z = jump speed, w = gravity
    float4 _Pieces2;          // x = spin (radians / s), y = ground height offset, z = air drag, w = when fading starts (0..1)
    float4 _Pieces3;          // x = fragment size (world units)
    #define GRASS_PIECE_FRAGMENTS 8 // 4 rows x 2 columns

    float GrassHash(float2 p)
    {
        p = frac(p * float2(123.34, 456.21));
        p += dot(p, p + 45.32);
        return frac(p.x * p.y);
    }

    float GrassNoise(float2 p)
    {
        float2 i = floor(p);
        float2 f = frac(p);
        float2 u = f * f * (3.0 - 2.0 * f);
        float a = GrassHash(i);
        float b = GrassHash(i + float2(1, 0));
        float c = GrassHash(i + float2(0, 1));
        float d = GrassHash(i + float2(1, 1));
        return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
    }

    float SnapToPixels(float value)
    {
        return _PixelOptions.x > 0.5 ? round(value * _Push.w) / _Push.w : value;
    }

    struct GrassState
    {
        // x = top of what is left (quad v; 2 = whole), y = 0, or 1 + a per-blade seed while in flames,
        // z = char (0..1), w = ember glow (0..1), or -1 - the lowest row kept (quad v) for a cut piece.
        float4 burn;
        float curl; // 0..1: how far the tuft has curled up from the heat
    };

    GrassState GrassEvaluate(uint index, float seed)
    {
        float4 s = _GrassStates[index];
        float clock = _GrassTime.x;
        float height = s.x;
        float charAmount = s.w;
        float flame = 0.0;
        float ember = 0.0;
        float curl = s.w; // ash stays curled until it grows back
        if (s.y >= 0.0 && clock >= s.y)
        {
            float age = clock - s.y;
            float p = saturate(age / max(_Burn.x, 0.001));
            height = lerp(s.x, min(s.x, _Burn.y), p);
            charAmount = max(charAmount, saturate(p * 3.0));
            curl = max(curl, saturate(p * 1.4));
            if (p < 1.0) flame = 1.0 + seed;
            else ember = saturate(1.0 - (age - _Burn.x) / max(_Burn.z, 0.001));
        }
        if (s.z >= 0.0)
        {
            float r = saturate((clock - s.z) / max(_Regrow.x, 0.001));
            height = lerp(height, 1.0, r);
            charAmount *= 1.0 - r;
            curl *= 1.0 - r;
        }
        GrassState o;
        o.burn = float4(height >= 0.999 ? 2.0 : lerp(_SpriteExtra.x, _SpriteExtra.y, height), flame, charAmount, ember);
        o.curl = curl;
        return o;
    }

    // Bends a point of the tuft (x across, y up from the root) along an arc that tightens toward the tip, the way
    // a blade of grass curls and shrivels in a fire. side = +1 / -1: which way it curls.
    float2 GrassCurl(float2 local, float height, float amount, float side)
    {
        float theta = amount * 2.4;
        float h = local.y;
        float stepLength = h * 0.25;
        float2 spine = float2(0, 0);
        [unroll] for (int i = 0; i < 4; i++)
        {
            float s = (i + 0.5) * stepLength / height;
            float a = theta * s * s;
            spine += float2(sin(a), cos(a)) * stepLength;
        }
        float tipAngle = theta * (h / height) * (h / height);
        float across = local.x * (1.0 - 0.3 * saturate(amount));
        return float2(side * spine.x + across * cos(tipAngle), spine.y - side * across * sin(tipAngle));
    }

    // Sprite texel (column, row) under a uv.
    float2 GrassTexel(float2 uv)
    {
        float2 corner = (uv - _SpriteUV.xy) / max(_SpriteUV.zw, 1e-6);
        return floor(corner * _SpriteExtra.wz);
    }

    // Removes the cut or burnt-away part of a tuft in whole sprite rows, so the edge stays crisp. While the tuft
    // burns, a few flickering rows of flame survive above the edge: returns 1 for those pixels.
    float GrassBurnClip(float2 texel, float4 burn)
    {
        // A cut piece fading away: pixels dissolve in a random order (w = -1 - dissolve, x = 2 + seed).
        if (burn.w < 0.0) clip(GrassHash(texel * 0.61 + frac(burn.x * 7.31) * 53.0) - (-1.0 - burn.w));
        float edgeRow = floor(burn.x * _SpriteExtra.z + 0.5);
        float above = texel.y - edgeRow;
        if (above < 0.0) return 0.0;
        float tongue = 0.0;
        if (burn.y > 0.5 && above < _Fire.x)
        {
            float seed = burn.y - 1.0;
            float frame = floor(_GrassTime.x * _Fire.z + seed * 7.0);
            float n = GrassHash(float2(texel.x * 0.73 + seed * 19.0 + above * 3.1, frame * 0.61 + seed));
            tongue = n > (above + 1.0) / (_Fire.x + 1.0) ? 1.0 : 0.0;
        }
        clip(tongue - 0.5);
        return tongue;
    }

    struct GrassVertex
    {
        float3 positionWS;
        float2 uv;
        float shade;
        float3 rootWS;
        float4 burn;
    };

    GrassVertex BuildGrassVertex(float2 corner, uint instanceID)
    {
        uint index = (uint)_GrassInstanceOffset + instanceID;
        GrassBlade blade = _GrassBlades[index];
        float3 root = blade.positionScale.xyz;
        float scale = blade.positionScale.w;

        // Quad corner in the sprite's own frame (x across, y up), pivot at the root.
        float2 local = (corner - _SpriteSize.zw) * _SpriteSize.xy * scale;
        local.x *= blade.data.x;
        float3 right = _GrassRight.xyz;
        float heightAbove = max(local.y, 0.0);

        // Burning grass curls over (whole pixels, so the sprite stays crisp).
        GrassState state = GrassEvaluate(index, blade.data.z);
        float curl = state.curl * _Curl.x;
        if (curl > 0.001 && local.y > 0.0)
        {
            float visibleHeight = max((_SpriteExtra.y - _SpriteSize.w) * _SpriteSize.y * scale, 0.0001);
            float2 curled = GrassCurl(local, visibleHeight, curl, blade.data.z > 0.5 ? 1.0 : -1.0);
            local += float2(SnapToPixels(curled.x - local.x), SnapToPixels(curled.y - local.y));
        }

        // 0 at the root, 1 at the bend height: the tip moves, the root stays planted.
        float bendHeight = max(_WindShape.w * scale, 0.0001);
        float t = saturate(heightAbove / bendHeight);
        float weight = t * t;

        // Interaction map (trampling).
        float2 push = 0;
        float flatten = 0;
        if (_GrassInteractionParams.w > 0.5)
        {
            float2 mapUV = (root.xz - _GrassInteractionParams.xy) * _GrassInteractionParams.z;
            if (all(mapUV >= 0.0) && all(mapUV <= 1.0))
            {
                float4 m = SAMPLE_TEXTURE2D_LOD(_GrassInteractionTex, sampler_GrassInteractionTex, mapUV, 0);
                push = m.xy;
                flatten = saturate(m.z);
            }
        }

        // Wind: a lean downwind that breathes with travelling waves and slow gusts.
        float2 windDir = _Wind.xy;
        float phase = dot(root.xz, windDir) * _WindShape.x - _Time.y * _Wind.w + blade.data.z * 1.7;
        float wave = sin(phase) * 0.6 + sin(phase * 2.31 + 1.3) * 0.4;
        float gust = GrassNoise(root.xz * _WindShape.z - windDir * (_Time.y * _Wind.w * 0.35));
        float windAmount = _Wind.z * (0.35 + 0.65 * lerp(1.0, gust, _WindShape.y)) * (0.55 + 0.45 * wave);
        float2 bend = windDir * windAmount * (1.0 - flatten) + push * _Push.x;

        float2 offsetXZ = bend * weight * scale;
        float squash = 1.0 - flatten * _Push.y;
        float yOffset = heightAbove * (squash - 1.0) - dot(offsetXZ, offsetXZ) * 0.35 / max(bendHeight, 0.0001);

        offsetXZ = float2(SnapToPixels(offsetXZ.x), SnapToPixels(offsetXZ.y));
        yOffset = SnapToPixels(yOffset);

        GrassVertex v;
        v.rootWS = root;
        v.positionWS = root + right * local.x + float3(0, local.y, 0) + float3(offsetXZ.x, yOffset, offsetXZ.y);
        v.uv = _SpriteUV.xy + corner * _SpriteUV.zw;
        v.shade = blade.data.y * lerp(1.0, _Push.z, flatten);
        v.burn = state.burn;
        return v;
    }

    // Where fragment edge k (0..count) of a piece lies, between lo and hi: inner edges wander a little per tuft,
    // snapped to whole sprite pixels so fragments stay crisp.
    float GrassFragmentEdge(float lo, float hi, float k, float count, float seed, float pixels)
    {
        float t = k / count;
        if (k > 0.5 && k < count - 0.5) t += (GrassHash(float2(seed * 91.7, k * 3.3)) - 0.5) * 0.35 / count;
        return round(lerp(lo, hi, t) * pixels) / pixels;
    }

    // One fragment of a severed top: thrown away from the cut, fluttering and tumbling, dissolving in mid-air.
    GrassVertex BuildPieceVertex(float2 corner, uint instanceID)
    {
        uint bladeInstance = instanceID / GRASS_PIECE_FRAGMENTS;
        uint fragment = instanceID - bladeInstance * GRASS_PIECE_FRAGMENTS;
        uint index = (uint)_GrassInstanceOffset + bladeInstance;
        GrassBlade blade = _GrassBlades[index];
        float4 cut = _GrassCuts[index];
        float3 root = blade.positionScale.xyz;
        float scale = blade.positionScale.w;
        float seed = blade.data.z;
        float age = _GrassTime.x - cut.x;

        GrassVertex v;
        v.rootWS = root;
        v.uv = _SpriteUV.xy + corner * _SpriteUV.zw;
        v.shade = blade.data.y;
        v.burn = float4(2.0 + seed, 0.0, 0.0, -1.0);
        v.positionWS = root; // unused fragment: every corner on one point, nothing is drawn
        if (cut.x < 0.0 || age < 0.0 || age > _Pieces.x) return v;

        // How many fragments this piece breaks into: rows by its height, two columns when it is wide.
        float bottomV = lerp(_SpriteExtra.x, _SpriteExtra.y, cut.y);
        float topV = lerp(_SpriteExtra.x, _SpriteExtra.y, cut.z);
        float fragmentSize = max(_Pieces3.x, 0.01);
        float rows = clamp(ceil((topV - bottomV) * _SpriteSize.y * scale / fragmentSize - 0.05), 1.0, 4.0);
        float columns = _SpriteSize.x * scale > fragmentSize * 1.5 ? 2.0 : 1.0;
        float row = floor(fragment / 2.0);
        float column = fragment - row * 2.0;
        if (row >= rows || column >= columns) return v;

        float2 rectMin = float2(GrassFragmentEdge(0.0, 1.0, column, columns, seed + 0.37, _SpriteExtra.w),
                                GrassFragmentEdge(bottomV, topV, row, rows, seed, _SpriteExtra.z));
        float2 rectMax = float2(GrassFragmentEdge(0.0, 1.0, column + 1.0, columns, seed + 0.37, _SpriteExtra.w),
                                GrassFragmentEdge(bottomV, topV, row + 1.0, rows, seed, _SpriteExtra.z));
        if (rectMax.y - rectMin.y <= 0.0 || rectMax.x - rectMin.x <= 0.0) return v;
        float2 cornerF = lerp(rectMin, rectMax, corner);
        float2 center = 0.5 * (rectMin + rectMax);
        v.uv = _SpriteUV.xy + cornerF * _SpriteUV.zw;

        float flip = blade.data.x;
        float2 local = (cornerF - center) * _SpriteSize.xy * scale;
        local.x *= flip;
        float2 start = (center - _SpriteSize.zw) * _SpriteSize.xy * scale;
        start.x *= flip;

        // Every fragment gets its own throw; higher fragments fly higher, outer columns fly further out.
        float fragmentSeed = frac(seed * 7.13 + fragment * 0.618 + 0.11);
        float r1 = GrassHash(float2(fragmentSeed * 43.1, 1.7));
        float r2 = GrassHash(float2(fragmentSeed * 17.9, 5.3));
        float r3 = GrassHash(float2(fragmentSeed * 29.3, 9.1));
        float3 right = _GrassRight.xyz;
        float spread = (r3 - 0.5) * 1.4 + (columns > 1.5 ? (column - 0.5) * 0.9 : 0.0);
        float heading = cut.w + spread;
        float2 dir = float2(cos(heading), sin(heading));
        float speed = _Pieces.y * (0.45 + 0.9 * r1);
        float jump = _Pieces.z * (0.5 + 0.7 * r2) * (0.8 + 0.25 * row);

        // Air drag makes pieces float and flutter instead of flying like stones.
        float drag = max(_Pieces2.z, 0.01);
        float travelTime = (1.0 - exp(-drag * age)) / drag;
        float gravity = _Pieces.w;
        float fall = gravity * (age - travelTime) / drag;
        float height = start.y + jump * travelTime - fall;
        float flutter = sin(age * (7.0 + 5.0 * r2) + fragmentSeed * 31.0) * 0.06 * saturate(age * 3.0);
        float2 travel = dir * (speed * travelTime) + float2(right.x, right.z) * flutter;
        height = max(height, _Pieces2.y);

        float spinSign = r3 > 0.5 ? 1.0 : -1.0;
        float angle = _Pieces2.x * (0.5 + r1) * spinSign * travelTime + sin(age * 9.0 + r1 * 20.0) * 0.25;

        // Dissolve pixel by pixel from the fade start to the end of the lifetime (and shrink a touch).
        float life = saturate(age / _Pieces.x);
        float dissolve = smoothstep(_Pieces2.w, 1.0, life);
        v.burn.w = -1.0 - dissolve;
        float shrink = 1.0 - 0.3 * dissolve;

        float sn, cs;
        sincos(angle, sn, cs);
        float2 rotated = float2(local.x * cs - local.y * sn, local.x * sn + local.y * cs) * shrink;
        float3 offset = float3(travel.x, height, travel.y) + right * start.x;
        offset = float3(SnapToPixels(offset.x), SnapToPixels(offset.y), SnapToPixels(offset.z));
        v.positionWS = root + offset + right * rotated.x + float3(0, rotated.y, 0);
        return v;
    }

    GrassVertex BuildVertex(float2 corner, uint instanceID)
    {
        #if defined(_GRASS_PIECES)
        return BuildPieceVertex(corner, instanceID);
        #else
        return BuildGrassVertex(corner, instanceID);
        #endif
    }

    half3 GrassNormal()
    {
        // Blend between "lit like the ground" and "lit like an upright card facing the camera".
        float3 facing = normalize(cross(_GrassRight.xyz, float3(0, 1, 0)));
        return (half3)normalize(lerp(-facing, float3(0, 1, 0), _Patch.z));
    }

    half4 SampleGrass(float2 uv, float3 rootWS, float shade)
    {
        half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
        clip(tex.a - _Cutoff);
        float patch = GrassNoise(rootWS.xz * _Patch.x);
        half3 tint = (half3)lerp(_ColorA.rgb, _ColorB.rgb, patch);
        return half4(tex.rgb * tint * (half)shade, tex.a);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ _GRASS_PIECES

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 rootWS : TEXCOORD2;
                float2 shadeFog : TEXCOORD3;
                nointerpolation float4 burn : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                GrassVertex g = BuildVertex(input.uv, input.instanceID);
                Varyings output;
                output.positionWS = g.positionWS;
                output.positionCS = TransformWorldToHClip(g.positionWS);
                output.uv = g.uv;
                output.rootWS = g.rootWS;
                output.shadeFog = float2(g.shade, ComputeFogFactor(output.positionCS.z));
                output.burn = g.burn;
                return output;
            }

            half Band(half value)
            {
                return _Patch.w > 0.5 ? floor(value * _Patch.w + 0.5) / _Patch.w : value;
            }

            // Charring, flames and embers. Darkens the albedo where it burns; returns the glow (added unlit).
            half3 GrassBurnColor(float2 texel, float4 burn, float3 rootWS, float tongue, inout half3 albedo)
            {
                float speck = GrassHash(texel * 0.37 + rootWS.xz * 5.3);
                half3 burnt = speck > 0.6 ? (half3)_AshColor.rgb : (half3)_CharColor.rgb;
                albedo = lerp(albedo, burnt, (half)burn.z);

                half3 emission = half3(0, 0, 0);
                if (burn.y > 0.5)
                {
                    float seed = burn.y - 1.0;
                    float frame = floor(_GrassTime.x * _Fire.z + seed * 7.0);
                    float edgeRow = floor(burn.x * _SpriteExtra.z + 0.5);
                    float below = edgeRow - texel.y; // 1 = the row just under the burning edge
                    float heat = tongue > 0.5 ? 1.0 : saturate(1.0 - (below - 1.0) / max(_Fire.y, 1.0));
                    float flicker = GrassHash(float2(texel.x + seed * 13.0, frame * 0.37 + texel.y));
                    heat *= 0.65 + 0.35 * flicker;
                    heat = floor(heat * 3.0 + 0.5) / 3.0; // three flame shades, pixel-art style
                    if (heat > 0.0)
                    {
                        float tip = tongue > 0.5 ? flicker : heat * 0.5;
                        emission = (half3)(lerp(_FlameColor.rgb, _FlameTipColor.rgb, tip) * heat);
                        albedo *= (half)(1.0 - heat);
                    }
                }
                else if (burn.w > 0.0)
                {
                    float frame = floor(_GrassTime.x * 3.0);
                    float n = GrassHash(texel * 0.53 + rootWS.xz * 7.1 + frame * 0.17);
                    if (n > 1.0 - _Fire.w * burn.w) emission = (half3)(_FlameColor.rgb * (burn.w * 0.8));
                }
                return emission;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 texel = GrassTexel(input.uv);
                float tongue = GrassBurnClip(texel, input.burn);
                half4 albedo = SampleGrass(input.uv, input.rootWS, input.shadeFog.x);
                half3 burnAlbedo = albedo.rgb;
                half3 emission = GrassBurnColor(texel, input.burn, input.rootWS, tongue, burnAlbedo);
                albedo.rgb = burnAlbedo;
                half3 normalWS = GrassNormal();

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                half3 color = albedo.rgb * SampleSH(normalWS);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half mainTerm = saturate(dot(normalWS, mainLight.direction) * 0.5h + 0.5h)
                                * mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                color += albedo.rgb * mainLight.color * Band(mainTerm);

                #if defined(_ADDITIONAL_LIGHTS)
                half4 shadowMask = half4(1, 1, 1, 1);
                uint pixelLightCount = GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint dirIndex = 0; dirIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); dirIndex++)
                {
                    Light dirLight = GetAdditionalLight(dirIndex, input.positionWS, shadowMask);
                    half dirTerm = saturate(dot(normalWS, dirLight.direction) * 0.5h + 0.5h)
                                   * dirLight.shadowAttenuation * dirLight.distanceAttenuation;
                    color += albedo.rgb * dirLight.color * Band(dirTerm);
                }
                #endif
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);
                    half term = saturate(dot(normalWS, light.direction) * 0.5h + 0.5h)
                                * light.shadowAttenuation * light.distanceAttenuation;
                    color += albedo.rgb * light.color * Band(term);
                LIGHT_LOOP_END
                #endif

                color += emission;
                color = MixFog(color, input.shadeFog.y);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ _GRASS_PIECES
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation float4 burn : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                GrassVertex g = BuildVertex(input.uv, input.instanceID);
                float3 normalWS = (float3)GrassNormal();
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - g.positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                Varyings output;
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(g.positionWS, normalWS, lightDirectionWS)));
                output.uv = g.uv;
                output.burn = g.burn;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                GrassBurnClip(GrassTexel(input.uv), input.burn);
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ _GRASS_PIECES

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation float4 burn : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                GrassVertex g = BuildVertex(input.uv, input.instanceID);
                Varyings output;
                output.positionCS = TransformWorldToHClip(g.positionWS);
                output.uv = g.uv;
                output.burn = g.burn;
                return output;
            }

            half Frag(Varyings input) : SV_Target
            {
                GrassBurnClip(GrassTexel(input.uv), input.burn);
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - _Cutoff);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ _GRASS_PIECES
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #if defined(_GBUFFER_NORMALS_OCT)
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation float4 burn : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                GrassVertex g = BuildVertex(input.uv, input.instanceID);
                Varyings output;
                output.positionCS = TransformWorldToHClip(g.positionWS);
                output.uv = g.uv;
                output.burn = g.burn;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                GrassBurnClip(GrassTexel(input.uv), input.burn);
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - _Cutoff);
                float3 normalWS = (float3)GrassNormal();
                #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                return half4(PackFloat2To888(remappedOctNormalWS), 0.0);
                #else
                return half4(NormalizeNormalPerPixel(normalWS), 0.0);
                #endif
            }
            ENDHLSL
        }
    }
    Fallback Off
}
