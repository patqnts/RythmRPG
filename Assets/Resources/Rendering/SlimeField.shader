// Pixel slime: one goopy silhouette made of a sprite (the slime's own body) plus analytic shapes (tendrils, blobs,
// rings, rounded boxes), all melted together with a smooth union. Drawn on one camera-facing quad inside the pixel
// camera, so every fragment is exactly one render-texture pixel: hard thresholds give crisp pixel-art edges.
//
//   Fill:    accent ramp (shadow / base / highlight) from a fake dome normal, posterised into bands with optional
//            Bayer dithering, a 1 px inner rim light and a specular glint.
//   Outline: iridescent, cycling ember yellow -> ember hot green -> ash pink -> accent highlight along the silhouette
//            (hue from the edge normal, position and time), posterised to a few steps. Optional dark outer line.
//
// Fed by SlimeField.cs (primitives in the quad's plane, sprite -> plane mapping). Not SRP-batcher compatible (arrays).
Shader "RythmRPG/Combat/Slime Field"
{
    Properties
    {
        [Header(Fill (accent ramp))]
        _ShadowColor ("Shadow", Color) = (0.039, 0.62, 0.106, 1)
        _BaseColor ("Base", Color) = (0.129, 0.929, 0.526, 1)
        _HighlightColor ("Highlight", Color) = (0.42, 1, 0.51, 1)
        _GlintColor ("Glint", Color) = (1, 0.99, 0.75, 1)
        _FillAlpha ("Fill Alpha", Range(0, 1)) = 0.92
        _DomeDepth ("Dome Depth (units)", Float) = 0.18
        _ShadowCut ("Shadow Band", Range(-1, 1)) = 0.25
        _HighlightCut ("Highlight Band", Range(-1, 1)) = 0.72
        _GlintCut ("Glint Band", Range(0, 1)) = 0.93
        _Dither ("Band Dither", Range(0, 1)) = 0.5
        _RimLight ("Inner Rim Light", Range(0, 1)) = 0.8
        _LightDir ("Light Direction (xy screen, z out)", Vector) = (-0.55, 0.7, 0.55, 0)

        [Header(Iridescent outline)]
        _IriA ("Ember (yellow)", Color) = (0.97, 1, 0.125, 1)
        _IriB ("Ember Hot (green)", Color) = (0.061, 1, 0.125, 1)
        _IriC ("Ash (pink)", Color) = (1, 0.76, 0.96, 1)
        _IriD ("Accent", Color) = (0.42, 1, 0.51, 1)
        _OutlinePixels ("Outline Pixels", Range(0, 4)) = 1
        _IriAngleBands ("Hue Turns Around The Edge", Range(0, 4)) = 1
        _IriPositionScale ("Hue Change Per Unit", Range(0, 8)) = 1.2
        _IriSpeed ("Hue Speed", Range(-4, 4)) = 0.35
        _IriSteps ("Hue Steps (0 = smooth)", Range(0, 16)) = 8
        _OuterColor ("Dark Outer Line", Color) = (0.035, 0.03, 0.07, 0)
        _OuterPixels ("Dark Outer Pixels", Range(0, 3)) = 1

        [Header(Goo)]
        _WobbleAmount ("Wobble (units)", Range(0, 0.1)) = 0.012
        _WobbleScale ("Wobble Scale", Range(0.5, 20)) = 5
        _WobbleFps ("Wobble FPS (0 = smooth)", Range(0, 60)) = 12

        [Header(Sprite silhouette)]
        _SpriteTex ("Body Sprite (set by script)", 2D) = "white" {}
        _SpriteCutoff ("Sprite Alpha Cutoff", Range(0.01, 1)) = 0.5
        _SpriteReach ("Sprite Search Radius (px)", Range(1, 10)) = 6

        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 8
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "SlimeField"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            ZTest [_ZTest]
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define SLIME_MAX_PRIMS 48
            #define SLIME_BIG 1e5

            TEXTURE2D(_SpriteTex);

            float4 _ShadowColor, _BaseColor, _HighlightColor, _GlintColor;
            float _FillAlpha, _DomeDepth, _ShadowCut, _HighlightCut, _GlintCut, _Dither, _RimLight;
            float4 _LightDir;
            float4 _IriA, _IriB, _IriC, _IriD;
            float _OutlinePixels, _IriAngleBands, _IriPositionScale, _IriSpeed, _IriSteps;
            float4 _OuterColor;
            float _OuterPixels;
            float _WobbleAmount, _WobbleScale, _WobbleFps;
            float _SpriteCutoff, _SpriteReach;

            // Per frame (SlimeField.cs). Plane = the quad's local XY in world units.
            float4 _SlimePrimA[SLIME_MAX_PRIMS]; // capsule: a.xy b.xy | ring: c.xy (unused) | box: c.xy halfSize angle
            float4 _SlimePrimB[SLIME_MAX_PRIMS]; // capsule: ra rb | ring: radius halfThickness | box: corner - ; z = kind, w = blend
            float4 _SlimePrimC[SLIME_MAX_PRIMS]; // tint rgb, tint amount
            float _SlimePrimCount;
            float _SlimeTime;
            float _SpriteEnabled;
            float4 _SpriteMap0;  // uv at plane origin (xy), d(uv)/d(plane x) (zw)
            float4 _SpriteMap1;  // d(uv)/d(plane y) (xy)
            float4 _SpriteRectUV; // min uv (xy), max uv (zw): texels outside are empty (atlas neighbours)
            float4 _SpriteBox;   // plane-space bounds of the sprite (min xy, max xy)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 plane : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.plane = input.uv;
                return output;
            }

            // ---------- noise ----------

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            static const float kBayer4[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            float Bayer4(float2 pixel)
            {
                int2 q = int2(fmod(floor(pixel), 4.0));
                return (kBayer4[q.y * 4 + q.x] + 0.5) / 16.0;
            }

            // ---------- distance shapes ----------

            float SdTaperedCapsule(float2 p, float2 a, float2 b, float ra, float rb)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-8));
                return length(pa - ba * h) - lerp(ra, rb, h);
            }

            float SdRing(float2 p, float2 c, float radius, float halfThickness)
            {
                return abs(length(p - c) - radius) - halfThickness;
            }

            float SdRoundBox(float2 p, float2 c, float halfSize, float angle, float corner)
            {
                float s, co;
                sincos(-angle, s, co);
                float2 q = p - c;
                q = float2(co * q.x - s * q.y, s * q.x + co * q.y);
                corner = min(corner, halfSize);
                float2 d = abs(q) - (halfSize - corner);
                return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - corner;
            }

            // Polynomial smooth minimum; w = how much of b's colour the blended surface takes.
            float SmoothMin(float a, float b, float k, out float w)
            {
                k = max(k, 1e-5);
                float h = saturate(0.5 + 0.5 * (a - b) / k);
                w = h;
                return lerp(a, b, h) - k * h * (1.0 - h);
            }

            // ---------- sprite silhouette ----------

            float SpriteAlpha(float2 plane)
            {
                float2 uv = _SpriteMap0.xy + plane.x * _SpriteMap0.zw + plane.y * _SpriteMap1.xy;
                if (any(uv < _SpriteRectUV.xy) || any(uv > _SpriteRectUV.zw)) return 0.0;
                return SAMPLE_TEXTURE2D_LOD(_SpriteTex, sampler_PointClamp, uv, 0).a;
            }

            // Approximate distance to the sprite's silhouette: rings of taps, one pixel apart, until one lands inside.
            float SpriteDistance(float2 p, float2 unitsPerPixel)
            {
                if (_SpriteEnabled < 0.5) return SLIME_BIG;
                float reach = _SpriteReach;
                float2 margin = unitsPerPixel * (reach + 1.0);
                if (any(p < _SpriteBox.xy - margin) || any(p > _SpriteBox.zw + margin)) return SLIME_BIG;
                float pixel = 0.5 * (unitsPerPixel.x + unitsPerPixel.y);
                if (SpriteAlpha(p) >= _SpriteCutoff) return -0.5 * pixel;

                [loop]
                for (int r = 1; r <= 10; r++)
                {
                    if (r > (int)reach) break;
                    float found = 0.0;
                    [unroll]
                    for (int i = 0; i < 12; i++)
                    {
                        float angle = i * (6.2831853 / 12.0);
                        float2 dir = float2(cos(angle), sin(angle));
                        float2 offset = dir * unitsPerPixel * r;
                        if (SpriteAlpha(p + offset) >= _SpriteCutoff) found = 1.0;
                    }
                    if (found > 0.5) return (r - 0.5) * pixel;
                }
                return SLIME_BIG;
            }

            // ---------- the whole field ----------

            float Field(float2 p, float2 unitsPerPixel, float wobbleTime, out float4 tint)
            {
                float d = SpriteDistance(p, unitsPerPixel);
                tint = float4(0, 0, 0, 0);
                int count = (int)_SlimePrimCount;
                [loop]
                for (int i = 0; i < SLIME_MAX_PRIMS; i++)
                {
                    if (i >= count) break;
                    float4 A = _SlimePrimA[i];
                    float4 B = _SlimePrimB[i];
                    float di;
                    if (B.z < 0.5) di = SdTaperedCapsule(p, A.xy, A.zw, B.x, B.y);
                    else if (B.z < 1.5) di = SdRing(p, A.xy, B.x, B.y);
                    else di = SdRoundBox(p, A.xy, A.z, A.w, B.x);
                    float w;
                    d = SmoothMin(d, di, B.w, w);
                    tint = lerp(tint, _SlimePrimC[i], w);
                }
                if (_WobbleAmount > 0.0)
                    d += (ValueNoise(p * _WobbleScale + float2(wobbleTime * 0.9, wobbleTime * 0.6)) - 0.5) * 2.0 * _WobbleAmount;
                return d;
            }

            float3 IridescentColor(float hue)
            {
                if (_IriSteps >= 1.0) hue = (floor(hue * _IriSteps) + 0.5) / _IriSteps;
                float x = frac(hue) * 4.0;
                float3 c;
                if (x < 1.0) c = lerp(_IriA.rgb, _IriB.rgb, x);
                else if (x < 2.0) c = lerp(_IriB.rgb, _IriC.rgb, x - 1.0);
                else if (x < 3.0) c = lerp(_IriC.rgb, _IriD.rgb, x - 2.0);
                else c = lerp(_IriD.rgb, _IriA.rgb, x - 3.0);
                return c;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.plane;
                // The plane is linear in screen space, so the derivatives are exact: world units per render pixel.
                float2 unitsPerPixel = max(float2(length(float2(ddx(p.x), ddy(p.x))), length(float2(ddx(p.y), ddy(p.y)))), 1e-5);
                float pixel = 0.5 * (unitsPerPixel.x + unitsPerPixel.y);

                float wobbleTime = _WobbleFps > 0.0 ? floor(_SlimeTime * _WobbleFps) / _WobbleFps : _SlimeTime;
                float4 tint;
                float d = Field(p, unitsPerPixel, wobbleTime, tint);

                float outline = max(_OutlinePixels, 0.0) * pixel;
                float outer = outline + (_OuterColor.a > 0.0 ? max(_OuterPixels, 0.0) * pixel : 0.0);
                if (d >= outer) discard;

                // Edge normal (screen-plane gradient of the field).
                float4 unused;
                float dx = Field(p + float2(unitsPerPixel.x, 0), unitsPerPixel, wobbleTime, unused)
                         - Field(p - float2(unitsPerPixel.x, 0), unitsPerPixel, wobbleTime, unused);
                float dy = Field(p + float2(0, unitsPerPixel.y), unitsPerPixel, wobbleTime, unused)
                         - Field(p - float2(0, unitsPerPixel.y), unitsPerPixel, wobbleTime, unused);
                float2 n2 = float2(dx, dy);
                n2 = dot(n2, n2) > 1e-12 ? normalize(n2) : float2(0, 1);

                if (d >= 0.0)
                {
                    if (d >= outline) return half4(_OuterColor.rgb, 1.0);
                    float angle = atan2(n2.y, n2.x) / 6.2831853;
                    float hue = angle * _IriAngleBands + dot(p, float2(0.7071, 0.7071)) * _IriPositionScale + _SlimeTime * _IriSpeed;
                    return half4(IridescentColor(hue), 1.0);
                }

                // Inside: dome normal, flat in the middle and steep at the rim.
                float inside = saturate(-d / max(_DomeDepth, 1e-4));
                float slope = 1.0 - inside;
                float3 normal = normalize(float3(n2 * slope, sqrt(saturate(1.0 - slope * slope)) + 1e-3));
                float3 light = normalize(_LightDir.xyz);
                float lambert = dot(normal, light);

                float2 pixelPos = input.positionCS.xy;
                float dither = (Bayer4(pixelPos) - 0.5) * 0.25 * _Dither;
                float shade = lambert + dither;

                float3 color = shade < _ShadowCut ? _ShadowColor.rgb : (shade < _HighlightCut ? _BaseColor.rgb : _HighlightColor.rgb);

                // 1 px rim light on the edges facing the light.
                if (-d < pixel && dot(n2, normalize(light.xy)) > 0.35)
                    color = lerp(color, _HighlightColor.rgb, _RimLight);

                // Glint: a few pixels near the lit shoulder.
                if (lambert > _GlintCut && inside > 0.15 && inside < 0.75) color = _GlintColor.rgb;

                color = lerp(color, tint.rgb, saturate(tint.a));
                return half4(color, _FillAlpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
