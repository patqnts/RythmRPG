// Light shafts for GodRays.cs: soft additive ribbons with streaks, shimmer, dust motes and light pools, optionally
// posterised with ordered dithering on the render-texture pixel grid. No textures.
// uv0 = (across 0..1, along 0..1 from the opening). uv1 = (width, length, random, type: 0 beam / 1 pool).
// color.r = beam brightness.
Shader "RythmRPG/God Rays"
{
    Properties
    {
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 1
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 1
        [HDR] _TopColor ("Top Colour", Color) = (1, 0.95, 0.75, 1)
        [HDR] _BottomColor ("Bottom Colour", Color) = (1, 0.82, 0.52, 1)
        _Shape ("Edge Softness, Top Fade, Bottom Fade, Intensity", Vector) = (0.55, 0.2, 0.4, 0.55)
        _Streaks ("Streaks, Scale, Speed, Soft Distance", Vector) = (0.4, 1.5, 0.25, 0.6)
        _Shimmer ("Shimmer, Speed, Pool Intensity", Vector) = (0.3, 0.5, 0.45, 0)
        _Motes ("Mote Density, Spacing, Fall Speed, Brightness", Vector) = (0.3, 0.35, 0.12, 1.3)
        _Pixel ("Bands, Pixels Per Unit, Mote Pixels, Dither", Vector) = (5, 32, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "GodRays"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            // multi_compile (not shader_feature): the material is made at runtime, so the variant must exist in builds.
            #pragma multi_compile_local _ _SOFT_INTERSECTION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #if defined(_SOFT_INTERSECTION)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #endif

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _BottomColor;
                float4 _Shape;
                float4 _Streaks;
                float4 _Shimmer;
                float4 _Motes;
                float4 _Pixel;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 data : TEXCOORD1;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 data : TEXCOORD1;
                half brightness : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.data = input.data;
                output.brightness = input.color.r;
                return output;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(233.34, 851.73));
                p += dot(p, p + 23.45);
                return frac(p.x * p.y);
            }

            float2 Hash22(float2 p)
            {
                float n = Hash21(p);
                return float2(n, Hash21(p + n * 19.19));
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), u.x),
                            lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), u.x), u.y);
            }

            // 4x4 Bayer threshold (0..1) on the render-texture pixel grid.
            float Bayer4(float2 pixel)
            {
                uint2 q = uint2(pixel) % 4u;
                static const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
                return (m[q.y * 4u + q.x] + 0.5) / 16.0;
            }

            #if defined(_SOFT_INTERSECTION)
            float EyeDepth(float rawDepth)
            {
                if (unity_OrthoParams.w > 0.5)
                {
                    #if UNITY_REVERSED_Z
                    rawDepth = 1.0 - rawDepth;
                    #endif
                    return lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
                }
                return LinearEyeDepth(rawDepth, _ZBufferParams);
            }
            #endif

            half4 Frag(Varyings input) : SV_Target
            {
                float u = input.uv.x;
                float v = input.uv.y;
                float width = input.data.x;
                float beamLength = input.data.y;
                float random = input.data.z;
                bool isPool = input.data.w > 0.5;
                float time = _Time.y;

                // Slow brightening / dimming, different per beam.
                float shimmer = 1.0 + _Shimmer.x * (0.6 * sin(time * _Shimmer.y * 6.2832 + random * 40.0)
                                                  + 0.4 * sin(time * _Shimmer.y * 3.71 + random * 91.0));

                float light;
                half3 color;
                if (isPool)
                {
                    float2 p = input.uv * 2.0 - 1.0;
                    float d = length(p);
                    light = (1.0 - smoothstep(0.25, 1.0, d)) * _Shimmer.z * shimmer;
                    color = _BottomColor.rgb;
                }
                else
                {
                    // Across: soft edges. Along: fade in at the opening, out toward the ground.
                    float across = abs(u * 2.0 - 1.0);
                    float edge = saturate((1.0 - across) / max(_Shape.x, 0.001));
                    edge = edge * edge * (3.0 - 2.0 * edge);
                    float along = saturate(v / max(_Shape.y, 0.001)) * saturate((1.0 - v) / max(_Shape.z, 0.001));
                    along = along * along * (3.0 - 2.0 * along);

                    // Streaks: noise stretched along the beam, sliding down it.
                    float2 streakUV = float2(u * width * _Streaks.y * 3.0, v * beamLength * _Streaks.y * 0.35 - time * _Streaks.z);
                    float streak = ValueNoise(streakUV + random * 13.0);
                    float streakTerm = lerp(1.0, 0.35 + streak * 1.3, _Streaks.x);

                    light = edge * along * streakTerm * shimmer;
                    color = lerp(_TopColor.rgb, _BottomColor.rgb, v);

                    #if defined(_SOFT_INTERSECTION)
                    float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    float sceneDepth = EyeDepth(SampleSceneDepth(screenUV));
                    float beamDepth = EyeDepth(input.positionCS.z);
                    light *= saturate((sceneDepth - beamDepth) / max(_Streaks.w, 0.001));
                    #endif

                    // Dust motes: square specks one render-texture pixel wide, drifting down the beam.
                    if (_Motes.x > 0.0)
                    {
                        float spacing = max(_Motes.y, 0.05);
                        float2 beamPos = float2((u - 0.5) * width, v * beamLength);
                        beamPos.x += sin(time * 0.7 + beamPos.y * 1.3 + random * 6.0) * 0.08;
                        beamPos.y -= time * _Motes.z;
                        float2 cell = floor(beamPos / spacing);
                        float2 local = frac(beamPos / spacing);
                        float pick = Hash21(cell + random * 37.0);
                        if (pick < _Motes.x)
                        {
                            float2 centre = 0.15 + 0.7 * Hash22(cell + 5.3);
                            float2 offsetPixels = abs(local - centre) * spacing * _Pixel.y;
                            float halfSize = max(_Pixel.z, 1.0) * 0.5;
                            if (max(offsetPixels.x, offsetPixels.y) < halfSize)
                            {
                                float twinkle = 0.55 + 0.45 * sin(time * 2.7 + pick * 60.0);
                                light += _Motes.w * twinkle * sqrt(edge) * saturate(along * 2.0);
                                color = lerp(color, _TopColor.rgb, 0.5);
                            }
                        }
                    }
                }

                light *= _Shape.w * input.brightness;

                // Pixel-art bands with ordered dithering between them.
                if (_Pixel.x > 0.5)
                {
                    float threshold = _Pixel.w > 0.5 ? Bayer4(input.positionCS.xy) : 0.5;
                    light = floor(light * _Pixel.x + threshold) / _Pixel.x;
                }
                light = max(light, 0.0);
                return half4(color * light, light);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
