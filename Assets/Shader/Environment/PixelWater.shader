// Pixel-art water in the Eastward style, made for the 480x270 pixel camera and the oblique projection.
//   - See-through: the scene below is refracted in whole pixels (read from the camera's opaque texture) and tinted
//     by depth in flat stepped colour bands (no dithering).
//   - A crisp 1-pixel line where the water touches the shore and anything standing in it, plus stepped foam bands
//     that pulse toward the shore.
//   - Gentle vertex waves with crest highlights (stronger in windy weather), and a wobbling light pattern.
//   - Interactive: ripples, wakes, splashes and rain rings from the shared ripple simulation (PixelWater /
//     WaterSimulation) bend the refraction and the pattern and draw bright rings.
//   - Receives the sun's shadows; sparkling sun glints.
// Everything is snapped to the art-pixel grid in world space, so the patterns never swim when the camera moves.
// Needs the URP asset's Depth Texture and Opaque Texture (both on in this project).
Shader "RythmRPG/Pixel Water"
{
    Properties
    {
        [Header(Colour)]
        _ShallowColor ("Shallow", Color) = (0.36, 0.74, 0.82, 0.45)
        _DeepColor ("Deep", Color) = (0.1, 0.32, 0.55, 0.92)
        _DepthDistance ("Depth For Deep Colour", Float) = 1.5
        _ColorBands ("Depth Colour Bands", Range(1, 8)) = 4
        _Clarity ("See-Through", Range(0, 1)) = 0.75
        _LightInfluence ("Sun Colour Influence", Range(0, 1)) = 0.5
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.45

        [Header(Refraction)]
        _RefractionPixels ("Refraction (pixels)", Range(0, 6)) = 2
        _RefractionScale ("Refraction Scale", Float) = 1.4
        _RefractionSpeed ("Refraction Speed", Float) = 0.5

        [Header(Waves)]
        _WaveAmplitude ("Wave Height", Range(0, 0.5)) = 0.035
        _WaveLength ("Wave Length", Float) = 2.5
        _WaveSpeed ("Wave Speed", Float) = 0.8
        _WaveDirection ("Wave Direction (degrees)", Range(0, 360)) = 30
        _WindInfluence ("Weather Wind Influence", Range(0, 1)) = 0.5
        _CrestColor ("Crest Colour", Color) = (0.9, 0.97, 1, 0.55)
        _CrestThreshold ("Crest Threshold", Range(0, 1)) = 0.8

        [Header(Light Pattern)]
        _PatternColor ("Pattern Colour", Color) = (0.72, 0.92, 1, 0.45)
        _PatternScale ("Pattern Scale", Float) = 1.1
        _PatternSpeed ("Pattern Speed", Float) = 0.25
        _PatternWidth ("Pattern Line Width", Range(0, 0.3)) = 0.07

        [Header(Shore)]
        _LineColor ("Shore Line", Color) = (0.92, 0.98, 1, 1)
        _LineWidth ("Shore Line Pixels", Range(0, 3)) = 1
        _FoamColor ("Foam", Color) = (0.86, 0.96, 1, 0.9)
        _FoamDistance ("Foam Width (depth)", Float) = 0.35
        _FoamBands ("Foam Bands", Range(0, 4)) = 2
        _FoamSpeed ("Foam Pulse Speed", Float) = 0.7
        _FoamBreakup ("Foam Breakup", Range(0, 1)) = 0.5

        [Header(Interaction)]
        _RippleStrength ("Ripple Strength", Range(0, 4)) = 1
        _RippleColor ("Ripple Highlight", Color) = (0.92, 0.98, 1, 0.8)
        _RippleThreshold ("Ripple Highlight Threshold", Range(0.001, 0.3)) = 0.03

        [Header(Sparkle)]
        _GlintColor ("Sun Glints", Color) = (1, 1, 0.95, 1)
        _GlintAmount ("Glint Amount", Range(0, 1)) = 0.35

        [Header(Pixel)]
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
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                float _DepthDistance;
                float _ColorBands;
                half _Clarity;
                half _LightInfluence;
                half _ShadowStrength;
                float _RefractionPixels;
                float _RefractionScale;
                float _RefractionSpeed;
                float _WaveAmplitude;
                float _WaveLength;
                float _WaveSpeed;
                float _WaveDirection;
                float _WindInfluence;
                half4 _CrestColor;
                float _CrestThreshold;
                half4 _PatternColor;
                float _PatternScale;
                float _PatternSpeed;
                float _PatternWidth;
                half4 _LineColor;
                float _LineWidth;
                half4 _FoamColor;
                float _FoamDistance;
                float _FoamBands;
                float _FoamSpeed;
                float _FoamBreakup;
                float _RippleStrength;
                half4 _RippleColor;
                float _RippleThreshold;
                half4 _GlintColor;
                float _GlintAmount;
                float _PixelsPerUnit;
                float _ZWrite;
            CBUFFER_END

            // Globals from WaterSimulation and the WeatherController.
            TEXTURE2D(_WaterSimTex);
            SAMPLER(sampler_WaterSimTex);
            float4 _WaterSimParams;   // origin x, origin z, 1 / size, on
            float4 _WaterSimTexel;    // x = 1 / resolution (uv), y = world size of a texel
            float4 _WeatherWind;      // wind x, wind z, speed, time

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 waveFog : TEXCOORD1; // x = wave 0..1, y = fog factor
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float WaveValue(float2 xz, float time)
            {
                float a = radians(_WaveDirection);
                float2 d1 = float2(cos(a), sin(a));
                float2 d2 = float2(cos(a + 0.87), sin(a + 0.87));
                float k = 6.2831853 / max(_WaveLength, 0.05);
                float w = k * _WaveSpeed;
                return sin(dot(d1, xz) * k - time * w) * 0.65 + sin(dot(d2, xz) * k * 1.73 - time * w * 1.2 + 1.3) * 0.35;
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float wave = WaveValue(positionWS.xz, _Time.y);
                float wind = saturate(_WeatherWind.z / 8.0) * _WindInfluence;
                positionWS.y += wave * _WaveAmplitude * (1.0 + wind * 2.0);
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.waveFog = float2(wave * 0.5 + 0.5, ComputeFogFactor(o.positionCS.z));
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float2 Hash22(float2 p)
            {
                return float2(Hash21(p), Hash21(p + 19.19));
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x),
                            lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y);
            }

            // Distance to the nearest border between cells (thin wobbly light lines).
            float CellEdge(float2 p, float time)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                float best = 8.0, second = 8.0;
                [unroll] for (int y = -1; y <= 1; y++)
                {
                    [unroll] for (int x = -1; x <= 1; x++)
                    {
                        float2 o = float2(x, y);
                        float2 h = Hash22(cell + o);
                        float2 site = o + 0.5 + 0.35 * sin(time + h * 6.2831853);
                        float d = length(site - f);
                        if (d < best) { second = best; best = d; }
                        else if (d < second) second = d;
                    }
                }
                return second - best;
            }

            float EyeDepthFromRaw(float raw)
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

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float ppu = max(_PixelsPerUnit, 1.0);
                int2 pixel = int2(input.positionCS.xy);
                int2 maxPixel = int2(_ScaledScreenParams.xy) - 1;
                float3 positionWS = input.positionWS;
                float2 cellPx = floor(positionWS.xz * ppu);
                float2 xz = (cellPx + 0.5) / ppu;          // world XZ snapped to the art pixel
                float time = _Time.y;

                float surfaceEye = -TransformWorldToView(positionWS).z;
                float2 surfaceSlope = float2(ddx(surfaceEye), ddy(surfaceEye));

                // ---------- ripples from the simulation
                float ripple = 0;
                float2 slope = 0;
                if (_WaterSimParams.w > 0.5)
                {
                    float2 suv = (xz - _WaterSimParams.xy) * _WaterSimParams.z;
                    if (all(suv > 0.0) && all(suv < 1.0))
                    {
                        float t = max(_WaterSimTexel.x, 1e-4);
                        ripple = SAMPLE_TEXTURE2D_LOD(_WaterSimTex, sampler_WaterSimTex, suv, 0).r;
                        float hx = SAMPLE_TEXTURE2D_LOD(_WaterSimTex, sampler_WaterSimTex, suv + float2(t, 0), 0).r
                                 - SAMPLE_TEXTURE2D_LOD(_WaterSimTex, sampler_WaterSimTex, suv - float2(t, 0), 0).r;
                        float hz = SAMPLE_TEXTURE2D_LOD(_WaterSimTex, sampler_WaterSimTex, suv + float2(0, t), 0).r
                                 - SAMPLE_TEXTURE2D_LOD(_WaterSimTex, sampler_WaterSimTex, suv - float2(0, t), 0).r;
                        slope = float2(hx, hz) * _RippleStrength;
                        ripple *= _RippleStrength;
                    }
                }

                // ---------- refraction in whole pixels
                float2 flow = xz * _RefractionScale + time * _RefractionSpeed * float2(0.3, 0.2);
                float2 nudge = float2(Noise(flow) - 0.5, Noise(flow + 13.1) - 0.5) * 2.0 + slope * 6.0;
                int2 offset = int2(round(clamp(nudge, -1.5, 1.5) * _RefractionPixels));
                float sceneEye = EyeDepthFromRaw(LoadSceneDepth(uint2(pixel)));
                int2 source = clamp(pixel + offset, int2(0, 0), maxPixel);
                float sourceEye = EyeDepthFromRaw(LoadSceneDepth(uint2(source)));
                if (sourceEye < surfaceEye)
                {
                    // That pixel is in front of the water (a rock, a leg): don't pull it under.
                    source = pixel;
                    sourceEye = sceneEye;
                }
                float depth = max(0.0, sourceEye - surfaceEye);
                float shoreDepth = max(0.0, sceneEye - surfaceEye);
                half3 scene = LOAD_TEXTURE2D_X(_CameraOpaqueTexture, source).rgb;

                // ---------- depth colour in stepped bands
                float bands = max(_ColorBands, 1.0);
                float d = saturate(depth / max(_DepthDistance, 0.01));
                // Flat bands with hard edges.
                d = saturate(floor(d * bands + 0.5) / bands);
                half4 water = lerp(_ShallowColor, _DeepColor, d);
                half cover = lerp(1.0, water.a, _Clarity);
                half3 under = scene * lerp(half3(1, 1, 1), saturate(water.rgb * 1.4), 0.35);
                half3 color = lerp(under, water.rgb, cover);

                // ---------- light pattern, crests, ripples
                float2 patternPos = xz * _PatternScale + slope * 2.0 + time * _PatternSpeed * float2(0.2, 0.12);
                float edge = CellEdge(patternPos, time * _PatternSpeed * 2.0);
                bool pattern = edge < _PatternWidth;
                if (pattern) color = lerp(color, _PatternColor.rgb, _PatternColor.a);
                if (input.waveFog.x > _CrestThreshold) color = lerp(color, _CrestColor.rgb, _CrestColor.a);
                if (ripple > _RippleThreshold) color = lerp(color, _RippleColor.rgb, _RippleColor.a);
                else if (ripple < -_RippleThreshold * 1.5) color *= 0.86;

                // ---------- foam bands toward the shore
                if (_FoamBands > 0.5 && shoreDepth < _FoamDistance)
                {
                    float f = shoreDepth / max(_FoamDistance, 0.001);
                    float pulse = frac(f * _FoamBands - time * _FoamSpeed);
                    float breakup = Noise(xz * 3.0 + time * 0.2);
                    bool foam = f < 0.22 || (pulse < 0.3 && breakup > _FoamBreakup * f);
                    if (foam) color = lerp(color, _FoamColor.rgb, _FoamColor.a);
                }

                // ---------- 1-pixel shore line: any neighbour where something stands in front of the water
                if (_LineWidth > 0.5)
                {
                    bool onLine = shoreDepth < 0.5 / ppu;
                    int width = (int)round(_LineWidth);
                    [unroll] for (int w = 1; w <= 3; w++)
                    {
                        if (w > width) continue;
                        int2 o0 = int2(w, 0), o1 = int2(-w, 0), o2 = int2(0, w), o3 = int2(0, -w);
                        float e0 = EyeDepthFromRaw(LoadSceneDepth(uint2(clamp(pixel + o0, int2(0, 0), maxPixel))));
                        float e1 = EyeDepthFromRaw(LoadSceneDepth(uint2(clamp(pixel + o1, int2(0, 0), maxPixel))));
                        float e2 = EyeDepthFromRaw(LoadSceneDepth(uint2(clamp(pixel + o2, int2(0, 0), maxPixel))));
                        float e3 = EyeDepthFromRaw(LoadSceneDepth(uint2(clamp(pixel + o3, int2(0, 0), maxPixel))));
                        float s0 = surfaceEye + dot(float2(o0), surfaceSlope);
                        float s1 = surfaceEye + dot(float2(o1), surfaceSlope);
                        float s2 = surfaceEye + dot(float2(o2), surfaceSlope);
                        float s3 = surfaceEye + dot(float2(o3), surfaceSlope);
                        float eps = 0.5 / ppu;
                        onLine = onLine || e0 < s0 - eps || e1 < s1 - eps || e2 < s2 - eps || e3 < s3 - eps;
                    }
                    if (onLine) color = lerp(color, _LineColor.rgb, _LineColor.a);
                }

                // ---------- light and shadow
                Light sun = GetMainLight(TransformWorldToShadowCoord(positionWS));
                half3 light = lerp(half3(1, 1, 1), saturate(sun.color), _LightInfluence);
                light *= lerp(1.0, sun.shadowAttenuation, _ShadowStrength);
                color *= light;

                // ---------- sun glints (twinkle on the pattern where the sun is not shadowed)
                float glint = Hash21(cellPx + floor(time * 5.0) * 7.31);
                if (_GlintAmount > 0.0 && pattern && sun.shadowAttenuation > 0.5 && glint > 1.0 - _GlintAmount * 0.12)
                    color = _GlintColor.rgb * max(light, 0.6);

                color = MixFog(color, input.waveFog.y);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
