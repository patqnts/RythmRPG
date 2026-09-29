// Pixel water for the 480x270 pixel camera and the oblique projection. Deliberately simple:
//   1. See-through: the scene under the water, shifted by whole pixels (distortion) and tinted from Shallow to Deep
//      by how deep the ground is below the surface.
//   2. A crisp pixel outline where the water meets the shore or anything standing in it (legs, rocks, posts).
//   3. Waves: gentle up / down motion plus thin pixel outlines around the moving wave crests.
//   4. Interaction: every character standing in the water gets rings of 1-pixel lines spreading out from its
//      contact outline (the same waterline outline as above, so the rings follow the shape of the legs / body),
//      stretched behind it while it moves. Ring events from WaterSimulation add the rest, in the same 1-pixel style:
//      a trail of rings behind a moving character, a splash (double ring + pixel droplets) when something falls in or
//      Water.Splash is called, and small rings where rain drops land. They also bend the distortion.
//   5. Tinted by the sun's colour (and its shadows).
// No dithering, no noise grain: every effect is a flat colour or a 1-pixel line.
// Needs the URP asset's Depth Texture and Opaque Texture.
Shader "RythmRPG/Pixel Water"
{
    Properties
    {
        [Header(Colour)]
        _ShallowColor ("Shallow (alpha = cover)", Color) = (0.36, 0.74, 0.84, 0.3)
        _DeepColor ("Deep (alpha = cover)", Color) = (0.1, 0.33, 0.56, 0.9)
        _DeepDistance ("Depth For Deep Colour", Float) = 1.2
        _ColorSteps ("Colour Steps (0 = smooth)", Range(0, 8)) = 0

        [Header(Distortion)]
        _DistortPixels ("Distortion (pixels)", Range(0, 4)) = 1
        _DistortScale ("Distortion Size", Float) = 1.6
        _DistortSpeed ("Distortion Speed", Float) = 0.6

        [Header(Outline)]
        _OutlineColor ("Outline", Color) = (0.93, 0.98, 1, 1)
        _OutlineWidth ("Outline Pixels", Range(0, 2)) = 1
        _ContactHeight ("Contact Height", Range(0.01, 1)) = 0.2

        [Header(Waves)]
        _WaveHeight ("Wave Height", Range(0, 0.3)) = 0.03
        _WaveLength ("Wave Length", Float) = 2.4
        _WaveSpeed ("Wave Speed", Float) = 0.6
        _WaveDirection ("Wave Direction (degrees)", Range(0, 360)) = 30
        _WindInfluence ("Weather Wind Influence", Range(0, 1)) = 0.5
        _WaveLineColor ("Wave Lines", Color) = (0.86, 0.96, 1, 0.55)
        _WaveLines ("Wave Line Amount", Range(0, 1)) = 0.45
        _WaveBreakup ("Wave Line Breakup", Range(0, 1)) = 0.6

        [Header(Interaction)]
        _RippleColor ("Ripple Lines", Color) = (0.95, 1, 1, 0.85)
        _RippleSpacing ("Ripple Spacing", Range(0.05, 1)) = 0.16
        _RippleSpeed ("Ripple Speed", Range(0, 2)) = 0.3
        _RippleRange ("Ripple Reach", Range(0.1, 3)) = 0.7
        _RippleFootprint ("Contact Search Size (x character radius)", Range(0.5, 3)) = 1.5
        _RippleStrength ("Splash Distortion", Range(0, 4)) = 1
        [Toggle] _TrailMerge ("Trail: Outline Only (merge rings)", Float) = 1

        [Header(Light)]
        _SunInfluence ("Sun Colour Influence", Range(0, 1)) = 0.6
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.35

        [Header(Other)]
        _PixelsPerUnit ("Pixels Per Unit", Float) = 32
        [Enum(Off, 0, On, 1)] _ZWrite ("Hide Submerged Parts (Depth Write)", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "PixelWater"
            Tags { "LightMode" = "UniversalForward" }
            Blend Off
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                float _DeepDistance;
                float _ColorSteps;
                float _DistortPixels;
                float _DistortScale;
                float _DistortSpeed;
                half4 _OutlineColor;
                float _OutlineWidth;
                float _ContactHeight;
                float _WaveHeight;
                float _WaveLength;
                float _WaveSpeed;
                float _WaveDirection;
                float _WindInfluence;
                half4 _WaveLineColor;
                float _WaveLines;
                float _WaveBreakup;
                half4 _RippleColor;
                float _RippleSpacing;
                float _RippleSpeed;
                float _RippleRange;
                float _RippleFootprint;
                float _RippleStrength;
                float _TrailMerge;
                half _SunInfluence;
                half _ShadowStrength;
                float _PixelsPerUnit;
                float _ZWrite;
            CBUFFER_END

            // Globals from WaterSimulation and the WeatherController.
            TEXTURE2D(_WaterSimTex);
            SAMPLER(sampler_WaterSimTex);
            float4 _WaterSimParams;   // origin x, origin z, 1 / size, on
            float4 _WeatherWind;      // wind x, wind z, speed, time
            // Characters in water (WaterSimulation): xyz = feet on the surface, w = radius; velocity xz, speed.
            float4 _WaterMovers[16];
            float4 _WaterMoverVelocities[16];
            float _WaterMoverCount;
            // Ring events (WaterSimulation): xy = world XZ, z = start time, w = type (0 ring, 1 splash, 2 rain drop,
            // 3 trail);
            // shape: x = start radius, y = speed, z = lifetime, w = strength.
            float4 _WaterRings[128];
            float4 _WaterRingShapes[128];
            float _WaterRingCount;
            float _WaterTime;

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fogFactor : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float WindAmount()
            {
                return saturate(_WeatherWind.z / 8.0) * _WindInfluence;
            }

            // Two crossing swells, -1..1.
            float Swell(float2 xz, float time)
            {
                float a = radians(_WaveDirection);
                float2 d1 = float2(cos(a), sin(a));
                float2 d2 = float2(cos(a + 0.9), sin(a + 0.9));
                float k = 6.2831853 / max(_WaveLength, 0.05);
                float w = k * _WaveSpeed * (1.0 + WindAmount());
                return sin(dot(d1, xz) * k - time * w) * 0.65 + sin(dot(d2, xz) * k * 1.7 - time * w * 1.15 + 1.3) * 0.35;
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS.y += Swell(positionWS.xz, _Time.y) * _WaveHeight * (1.0 + WindAmount() * 2.0);
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x),
                            lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y);
            }

            float EyeDepth(float raw)
            {
                if (unity_OrthoParams.w > 0.5)
                {
                    #if UNITY_REVERSED_Z
                        raw = 1.0 - raw;
                    #endif
                    return lerp(_ProjectionParams.y, _ProjectionParams.z, raw);
                }
                return LinearEyeDepth(raw, _ZBufferParams);
            }

            // View ray direction (exact for the sheared / oblique orthographic camera).
            float3 RayDirection(float3 positionWS)
            {
                if (unity_OrthoParams.w > 0.5)
                {
                    float3 v = cross(UNITY_MATRIX_P[0].xyz, UNITY_MATRIX_P[1].xyz);
                    if (dot(v, v) < 1e-10) return -UNITY_MATRIX_V[2].xyz;
                    v = normalize(v);
                    if (v.z > 0.0) v = -v;
                    return normalize(mul((float3x3)UNITY_MATRIX_I_V, v));
                }
                return normalize(positionWS - _WorldSpaceCameraPos);
            }

            // Wave-crest value with breakup, so the crest outlines become short drifting dashes.
            float CrestValue(float2 xz, float time)
            {
                float swell = Swell(xz, time);
                float breakup = Noise(xz * 1.3 + time * float2(0.11, 0.07)) - 0.5;
                return swell + breakup * _WaveBreakup * 1.6;
            }

            float Ripple(float2 xz)
            {
                if (_WaterSimParams.w < 0.5) return 0;
                float2 uv = (xz - _WaterSimParams.xy) * _WaterSimParams.z;
                if (any(uv <= 0.0) || any(uv >= 1.0)) return 0;
                return SAMPLE_TEXTURE2D_LOD(_WaterSimTex, sampler_WaterSimTex, uv, 0).r * _RippleStrength;
            }

            struct DepthContext
            {
                int2 pixel;
                int2 maxPixel;
                float surfaceEye;
                float2 surfaceEyeStep;   // change of the water surface's eye depth per screen pixel (x, y)
                float toVertical;        // eye depth difference -> vertical world units
            };

            // How far below the water surface the scene is at pixel + o (vertical world units, < 0 = above it).
            float DepthAt(DepthContext c, int2 o)
            {
                uint2 p = uint2(clamp(c.pixel + o, int2(0, 0), c.maxPixel));
                float sceneEye = EyeDepth(LoadSceneDepth(p));
                float surfaceEye = c.surfaceEye + dot(float2(o), c.surfaceEyeStep);
                return (sceneEye - surfaceEye) * c.toVertical;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 P = input.positionWS;
                float time = _Time.y;

                // One screen pixel over, on the water surface (the surface is a plane, so these are exact).
                float3 stepX = ddx(P);
                float3 stepY = ddy(P);

                DepthContext dc;
                dc.pixel = int2(input.positionCS.xy);
                dc.maxPixel = int2(_ScaledScreenParams.xy) - 1;
                dc.surfaceEye = EyeDepth(input.positionCS.z);
                dc.surfaceEyeStep = float2(ddx(dc.surfaceEye), ddy(dc.surfaceEye));
                float3 D = RayDirection(P);
                float3 forward = -UNITY_MATRIX_V[2].xyz;
                dc.toVertical = max(-D.y, 0.05) / max(dot(D, forward), 0.05);

                // ---------- ripples from the simulation
                float ripple = Ripple(P.xz);
                float rippleX = Ripple(P.xz + stepX.xz);
                float rippleY = Ripple(P.xz + stepY.xz);

                // ---------- see-through with whole-pixel distortion
                float2 flow = P.xz * _DistortScale + time * _DistortSpeed * float2(0.35, 0.22);
                float2 nudge = float2(Noise(flow) - 0.5, Noise(flow + 17.3) - 0.5) * 2.0
                             + float2(rippleX - ripple, rippleY - ripple) * 5.0;
                int2 offset = int2(round(clamp(nudge, -1.0, 1.0) * _DistortPixels));
                float depthHere = DepthAt(dc, int2(0, 0));
                float depth = DepthAt(dc, offset);
                int2 source = dc.pixel + offset;
                if (depth <= 0.0)
                {
                    // That pixel is something above the water (a leg, the shore): don't pull it under.
                    source = dc.pixel;
                    depth = depthHere;
                }
                source = clamp(source, int2(0, 0), dc.maxPixel);
                // Sampled by UV (at the pixel's centre) so it also works if the Opaque Texture is downsampled.
                half3 scene = SampleSceneColor((float2(source) + 0.5) / _ScaledScreenParams.xy);

                float t = saturate(max(depth, 0.0) / max(_DeepDistance, 0.01));
                if (_ColorSteps >= 1.0) t = saturate(floor(t * _ColorSteps + 0.5) / _ColorSteps);
                half4 water = lerp(_ShallowColor, _DeepColor, t);
                half3 color = lerp(scene, water.rgb, water.a);

                // ---------- wave crest outlines (1 pixel, on the inside of each crest)
                if (_WaveLines > 0.0 && _WaveLineColor.a > 0.0)
                {
                    float threshold = lerp(1.0, 0.35, _WaveLines);
                    float c = CrestValue(P.xz, time);
                    if (c >= threshold)
                    {
                        float c1 = CrestValue(P.xz + stepX.xz, time);
                        float c2 = CrestValue(P.xz - stepX.xz, time);
                        float c3 = CrestValue(P.xz + stepY.xz, time);
                        float c4 = CrestValue(P.xz - stepY.xz, time);
                        if (min(min(c1, c2), min(c3, c4)) < threshold)
                            color = lerp(color, _WaveLineColor.rgb, _WaveLineColor.a);
                    }
                }

                float eps = 0.5 / max(_PixelsPerUnit, 1.0);
                float lo = -_ContactHeight;
                // Screen pixel offset <-> world XZ offset on the water: world = ax * o.x + ay * o.y.
                float2 ax = stepX.xz, ay = stepY.xz;
                float det = ax.x * ay.y - ay.x * ax.y;
                bool canMap = abs(det) > 1e-10;
                #define TO_PIXELS(w) (float2(ay.y * (w).x - ay.x * (w).y, -ax.y * (w).x + ax.x * (w).y) / det)

                // ---------- ring events: trails, splashes, rain
                if (_WaterRingCount > 0.5 && _RippleColor.a > 0.0 && depthHere > 0.0 && canMap)
                {
                    float ringAlpha = 0.0;
                    float trailAlpha = 0.0;   // trail rings
                    float trailHidden = 0.0;  // inside another trail ring (merged: only the outer outline shows)
                    bool mergeTrail = _TrailMerge > 0.5;
                    int ringCount = min((int)_WaterRingCount, 128);
                    [loop] for (int e = 0; e < ringCount; e++)
                    {
                        float4 ev = _WaterRings[e];
                        float4 shape = _WaterRingShapes[e];
                        float age = _WaterTime - ev.z;
                        if (age < 0.0 || age > shape.z) continue;
                        float2 dv = P.xz - ev.xy;
                        float dist = length(dv);
                        float radius = shape.x + shape.y * age;
                        if (dist > radius + 0.6) continue;
                        float life = 1.0 - age / shape.z;
                        float strength = saturate(shape.w) * life;

                        // One screen pixel along the ring's radius, in world units.
                        float2 radial = dist > 1e-4 ? dv / dist : float2(1, 0);
                        float width = 1.0 / max(length(TO_PIXELS(radial)), 1e-4);
                        bool onRing = abs(dist - radius) < width * 0.5;
                        if (ev.w > 2.5 && mergeTrail)
                        {
                            // Trail ring: keep its line, but hide every other trail line that falls inside it.
                            if (onRing) trailAlpha = max(trailAlpha, strength);
                            // (inset by 1.5 pixels, so where two rings meet the outline stays unbroken)
                            else if (dist < radius - width * 1.5) trailHidden = max(trailHidden, saturate(life * 10.0));
                            continue;
                        }
                        if (onRing) ringAlpha = max(ringAlpha, strength);

                        if (ev.w > 0.5 && ev.w < 1.5)
                        {
                            // Splash: a second, inner ring and pixel droplets thrown out and falling back.
                            float inner = radius * 0.55;
                            if (age > 0.1 && abs(dist - inner) < width * 0.5) ringAlpha = max(ringAlpha, strength * 0.8);
                            float flight = age * 1.8 - age * age * 7.0;   // height of the droplets, up then down
                            if (flight > 0.0)
                            {
                                float seed = frac(sin(dot(ev.xy, float2(12.9898, 78.233))) * 43758.5453);
                                float reachOut = shape.x + age * shape.y * 1.4;
                                float lift = flight * (1.0 + shape.w * 0.5) / max(-RayDirection(P).y, 0.05);
                                [unroll] for (int k = 0; k < 8; k++)
                                {
                                    float angle = (k + seed) * 0.785398;
                                    float spread = 0.75 + 0.5 * frac(seed * 7.0 + k * 0.37);
                                    float2 drop = ev.xy + float2(cos(angle), sin(angle)) * reachOut * spread
                                                + RayDirection(P).xz * lift * spread;
                                    float2 o = TO_PIXELS(P.xz - drop);
                                    if (max(abs(o.x), abs(o.y)) < 0.5) ringAlpha = 1.0;
                                }
                            }
                        }
                        else if (ev.w > 1.5 && ev.w < 2.5 && age < 0.07)
                        {
                            // Rain: the drop itself, one pixel, the moment it lands.
                            float2 o = TO_PIXELS(dv);
                            if (max(abs(o.x), abs(o.y)) < 0.5) ringAlpha = max(ringAlpha, 1.0);
                        }
                    }
                    ringAlpha = max(ringAlpha, trailAlpha * (1.0 - trailHidden));
                    if (ringAlpha > 0.0) color = lerp(color, _RippleColor.rgb, _RippleColor.a * ringAlpha);
                }

                // ---------- contact ripples: rings of 1-pixel lines spreading out from a character's waterline
                if (_WaterMoverCount > 0.5 && _RippleColor.a > 0.0 && depthHere > 0.0)
                {
                    // The nearest character in the water.
                    int nearest = -1;
                    float nearestGap = 1e9;
                    int count = min((int)_WaterMoverCount, 16);
                    [loop] for (int i = 0; i < count; i++)
                    {
                        float4 m = _WaterMovers[i];
                        float gap = length(P.xz - m.xz) - m.w * _RippleFootprint;
                        if (gap < nearestGap) { nearestGap = gap; nearest = i; }
                    }

                    if (nearest >= 0 && nearestGap < _RippleRange * 1.6 && canMap)
                    {
                        float4 mover = _WaterMovers[nearest];
                        float4 velocity = _WaterMoverVelocities[nearest];
                        float footprint = mover.w * _RippleFootprint;
                        float2 toMover = mover.xz - P.xz;
                        float rStep = 1.0 / max(_PixelsPerUnit, 1.0);

                        // Distance (world, on the water) to the nearest waterline pixel of this character: rays in
                        // 32 directions, marched one art pixel at a time only where they cross its footprint.
                        float contact = 1e9;
                        [loop] for (int k = 0; k < 32; k++)
                        {
                            float angle = k * (6.2831853 / 32.0);
                            float2 u = float2(cos(angle), sin(angle));
                            float b = dot(u, toMover);
                            float disc = b * b - (dot(toMover, toMover) - footprint * footprint);
                            if (disc <= 0.0) continue;
                            float root = sqrt(disc);
                            float t0 = max(b - root, rStep);
                            float t1 = min(b + root, contact);
                            [loop] for (int n = 0; n < 48; n++)
                            {
                                float tt = t0 + n * rStep;
                                if (tt > t1) break;
                                float2 w = u * tt;
                                int2 o = int2(round(float2(ay.y * w.x - ay.x * w.y, -ax.y * w.x + ax.x * w.y) / det));
                                if (o.x == 0 && o.y == 0) continue;
                                float nd = DepthAt(dc, o);
                                if (nd < eps && nd > lo)
                                {
                                    // Refine between the last miss and this hit, so the rings come out smooth.
                                    float a0 = tt - rStep, a1 = tt;
                                    [unroll] for (int r = 0; r < 3; r++)
                                    {
                                        float mid = (a0 + a1) * 0.5;
                                        float2 wm = u * mid;
                                        int2 om = int2(round(float2(ay.y * wm.x - ay.x * wm.y, -ax.y * wm.x + ax.x * wm.y) / det));
                                        float md = DepthAt(dc, om);
                                        if ((om.x != 0 || om.y != 0) && md < eps && md > lo) a1 = mid;
                                        else a0 = mid;
                                    }
                                    contact = a1;
                                    break;
                                }
                            }
                        }

                        if (contact < 1e8)
                        {
                            // Moving: rings bunch up in front and trail out behind.
                            float2 away = -toMover;
                            float awayLength = length(away);
                            float2 heading = velocity.z > 0.01 ? velocity.xy / velocity.z : float2(0, 0);
                            float stretch = awayLength > 1e-4 ? dot(away / awayLength, heading) * saturate(velocity.z * 0.5) * 0.5 : 0.0;
                            float d = contact * (1.0 + stretch);
                            float reach = _RippleRange * (1.0 - stretch);
                            if (d > rStep * 1.5 && d < reach)
                            {
                                // Line width = one screen pixel along the ring's radius.
                                float2 radial = awayLength > 1e-4 ? away / awayLength : float2(1, 0);
                                float2 radialPixels = float2(ay.y * radial.x - ay.x * radial.y, -ax.y * radial.x + ax.x * radial.y) / det;
                                float width = 1.0 / max(length(radialPixels), 1e-4);
                                float spacing = max(_RippleSpacing, width * 3.0);
                                float band = frac(d / spacing - time * _RippleSpeed / spacing);
                                if (band * spacing < width)
                                    color = lerp(color, _RippleColor.rgb, _RippleColor.a * saturate(1.0 - d / reach));
                            }
                        }
                    }
                }

                // ---------- outline where the water meets the shore / anything standing in it:
                // this pixel is water, and a neighbour is a surface right at the waterline.
                if (_OutlineWidth >= 0.5 && depthHere > 0.0)
                {
                    bool onLine = depthHere < eps;
                    int width = (int)round(_OutlineWidth);
                    [unroll] for (int w = 1; w <= 2; w++)
                    {
                        if (w > width) break;
                        float n0 = DepthAt(dc, int2(w, 0));
                        float n1 = DepthAt(dc, int2(-w, 0));
                        float n2 = DepthAt(dc, int2(0, w));
                        float n3 = DepthAt(dc, int2(0, -w));
                        onLine = onLine || (n0 < eps && n0 > lo) || (n1 < eps && n1 > lo)
                                        || (n2 < eps && n2 > lo) || (n3 < eps && n3 > lo);
                    }
                    if (onLine) color = lerp(color, _OutlineColor.rgb, _OutlineColor.a);
                }

                // ---------- sun colour and shadow
                Light sun = GetMainLight(TransformWorldToShadowCoord(P));
                half3 sunColor = sun.color / max(max(sun.color.r, max(sun.color.g, sun.color.b)), 1.0h);
                half3 light = lerp(half3(1, 1, 1), sunColor, _SunInfluence);
                light *= lerp(1.0h, (half)sun.shadowAttenuation, _ShadowStrength);
                color *= light;

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
