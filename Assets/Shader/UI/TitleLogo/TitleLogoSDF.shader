// Title logo finish for TextMeshPro (UGUI or world TMP), URP.
// Ink letters with a grungy gold "burst" radiating from a focus point, a hot core, a halo swirl,
// optional pixel snapping, and a noise + directional "Disintegrate" with an ember edge.
//
// SDF coverage, padding, masking and TexCoord conventions follow RankTextSDF.shader (Unity 6 TMP layout:
// signed SDF scale in TEXCOORD0.w, texture-mapping UV in TEXCOORD1). The effect space is TMP's
// Paragraph texture mapping, so Canvas batching never shifts the pattern.
//
// Normally driven by TitleLogoText.cs, which sets _EffectAspect / _FocusPoint from the laid-out text,
// keeps the font atlas properties in sync, and spawns the drifting flakes (a second draw with _IsFlake = 1).
// Shared finish / noise / dissolve live in TitleLogoCommon.hlsl (mirrored in TitleLogoNoise.cs).
Shader "RythmRPG/UI/Title Logo SDF (URP)"
{
    Properties
    {
        [Header(Ink)]
        _FaceColor ("Ink Color", Color) = (0.02,0.018,0.016,1)
        _VertexTint ("Use Text Vertex RGB", Range(0,1)) = 0

        [Header(Accent Burst)]
        _GoldFill ("Fill Whole Text", Range(0,1)) = 0
        _BurstRadius ("Burst Radius (text heights)", Range(0,3)) = 0.85
        _BurstSoftness ("Burst Softness", Range(0.01,1)) = 0.5
        _BurstBreakup ("Burst Edge Breakup", Range(0,1)) = 0.6
        _GoldShadow ("Accent Shadow", Color) = (0.22,0.09,0.02,1)
        _GoldColor ("Accent", Color) = (0.86,0.52,0.1,1)
        _GoldHighlight ("Accent Highlight", Color) = (1,0.9,0.45,1)
        _GradientMix ("Vertical Gradient Mix", Range(0,1)) = 0.2
        [HDR] _CoreColor ("Core Glow", Color) = (1,0.95,0.7,1)
        _CoreSize ("Core Size", Range(0,1)) = 0.09
        _CoreIntensity ("Core Intensity", Range(0,4)) = 1.1

        [Header(Grunge)]
        _GrungeScale ("Grunge Scale", Range(1,80)) = 16
        _GrungeStrength ("Grunge Strength", Range(0,1)) = 0.75
        _GrungeContrast ("Grunge Contrast", Range(0.5,4)) = 1.8
        _GrungeFlow ("Grunge Flow Speed", Range(0,1)) = 0.02
        [NoScaleOffset] _GrungeTex ("Grunge Texture (optional, R)", 2D) = "white" {}
        _GrungeTexStrength ("Grunge Texture Strength", Range(0,1)) = 0
        _GrungeTexTiling ("Grunge Texture Tiling", Float) = 2

        [Header(Halo Swirl)]
        [HDR] _RingColor ("Swirl Color", Color) = (1,0.97,0.88,1)
        _RingStrength ("Swirl Strength", Range(0,2)) = 1.2
        _RingRadius ("Swirl Radius", Range(0,1)) = 0.3
        _RingCount ("Swirl Turns", Range(1,6)) = 2.5
        _RingWidth ("Swirl Line Width", Range(0.002,0.06)) = 0.02
        _RingSwirl ("Swirl Twist", Range(0,2)) = 1
        _RingTilt ("Swirl Tilt (Y squash)", Range(0.1,1)) = 0.5
        _RingSpeed ("Swirl Spin Speed", Range(-3,3)) = 0.35

        [Header(Pulse)]
        _PulseAmount ("Pulse Amount", Range(0,1)) = 0.12
        _PulseSpeed ("Pulse Speed", Range(0,4)) = 0.5

        [Header(Pixelate)]
        [MaterialToggle] _Pixelate ("Pixelate", Float) = 0
        _PixelDensity ("Pixels Per Text Height", Range(8,256)) = 48

        [Header(Disintegrate)]
        _Disintegrate ("Disintegrate", Range(0,1)) = 0
        _DisintegrateAngle ("Direction (deg, 0 = left to right)", Range(-180,180)) = 0
        _DirectionBias ("Directional vs Noise", Range(0,1)) = 0.55
        _DissolveScale ("Dissolve Noise Scale", Range(1,80)) = 18
        _EmberWidth ("Ember Edge Width", Range(0,0.3)) = 0.07
        _CharWidth ("Char Band Width", Range(0,0.3)) = 0.08
        [HDR] _EmberColor ("Ember", Color) = (1,0.45,0.08,1)
        [HDR] _EmberHotColor ("Ember Hot", Color) = (1,0.92,0.6,1)
        _AshColor ("Ash", Color) = (0.16,0.14,0.13,1)
        _NoiseSeed ("Noise Seed", Float) = 7

        [Header(Shine)]
        _ShineColor ("Shine Color", Color) = (1,0.98,0.85,1)
        _ShineStrength ("Shine Strength", Range(0,2)) = 0
        _ShineSpeed ("Shine Speed", Range(0,2)) = 0.22
        _ShineWidth ("Shine Width", Range(0.01,0.5)) = 0.08
        _ShineAngle ("Shine Angle", Range(-180,180)) = -25

        [Header(SDF Edge)]
        _FaceDilate ("Face Dilate", Range(-1,1)) = 0
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth ("Outline Thickness", Range(0,1)) = 0
        _OutlineSoftness ("Outline Softness", Range(0,1)) = 0
        [Toggle(GLOW_ON)] _EnableGlow ("Enable Edge Glow", Float) = 0
        [HDR] _GlowColor ("Glow Color", Color) = (1,0.6,0.2,0.35)
        _GlowOffset ("Glow Offset", Range(-1,1)) = 0
        _GlowOuter ("Glow Width", Range(0,1)) = 0.3
        _GlowPower ("Glow Falloff", Range(0.1,4)) = 1.5

        [Header(Driven By TitleLogoText)]
        _EffectAspect ("Effect Aspect (w/h)", Float) = 4
        _FocusPoint ("Focus Point (effect UV)", Vector) = (0.5,0.5,0,0)
        [HideInInspector] _IsFlake ("Is Flake", Float) = 0

        [Header(Font Atlas)]
        _MainTex ("Font Atlas", 2D) = "white" {}
        _GradientScale ("Gradient Scale", Float) = 5
        _TextureWidth ("Texture Width", Float) = 512
        _TextureHeight ("Texture Height", Float) = 512
        _WeightNormal ("Weight Normal", Float) = 0
        _WeightBold ("Weight Bold", Float) = 0.5
        _Sharpness ("Sharpness", Range(-1,1)) = 0
        _PerspectiveFilter ("Perspective Correction", Range(0,1)) = 0.875
        [HideInInspector] _ScaleX ("Scale X", Float) = 1
        [HideInInspector] _ScaleY ("Scale Y", Float) = 1
        [HideInInspector] _ScaleRatioA ("Scale Ratio A", Float) = 1
        [HideInInspector] _ScaleRatioB ("Scale Ratio B", Float) = 1
        [HideInInspector] _ScaleRatioC ("Scale Ratio C", Float) = 1
        [HideInInspector] _ShaderFlags ("Flags", Float) = 0
        [HideInInspector] _VertexOffsetX ("Vertex Offset X", Float) = 0
        [HideInInspector] _VertexOffsetY ("Vertex Offset Y", Float) = 0
        [HideInInspector] _ClipRect ("Clip Rect", Vector) = (-32767,-32767,32767,32767)
        [HideInInspector] _MaskSoftnessX ("Mask Softness X", Float) = 0
        [HideInInspector] _MaskSoftnessY ("Mask Softness Y", Float) = 0
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [Enum(UnityEngine.Rendering.CullMode)] _CullMode ("Cull Mode", Float) = 0
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("UI Alpha Clip", Float) = 0
        // Nonnegative = deterministic still preview. -1 uses Unity time.
        [HideInInspector] _PreviewTime ("Preview Time", Float) = -1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull [_CullMode]
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "TitleLogo"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _ GLOW_ON
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _FaceColor, _GoldShadow, _GoldColor, _GoldHighlight, _CoreColor, _RingColor;
                float4 _EmberColor, _EmberHotColor, _AshColor, _ShineColor, _OutlineColor, _GlowColor;
                float4 _FocusPoint, _ClipRect;
                float _VertexTint, _GoldFill, _BurstRadius, _BurstSoftness, _BurstBreakup, _GradientMix;
                float _CoreSize, _CoreIntensity;
                float _GrungeScale, _GrungeStrength, _GrungeContrast, _GrungeFlow, _GrungeTexStrength, _GrungeTexTiling;
                float _RingStrength, _RingRadius, _RingCount, _RingWidth, _RingSwirl, _RingTilt, _RingSpeed;
                float _PulseAmount, _PulseSpeed, _Pixelate, _PixelDensity;
                float _Disintegrate, _DisintegrateAngle, _DirectionBias, _DissolveScale;
                float _EmberWidth, _CharWidth, _NoiseSeed;
                float _ShineStrength, _ShineSpeed, _ShineWidth, _ShineAngle;
                float _FaceDilate, _OutlineWidth, _OutlineSoftness, _EnableGlow;
                float _GlowOffset, _GlowOuter, _GlowPower;
                float _EffectAspect, _IsFlake;
                float _GradientScale, _TextureWidth, _TextureHeight, _WeightNormal, _WeightBold;
                float _Sharpness, _PerspectiveFilter, _ScaleX, _ScaleY;
                float _ScaleRatioA, _ScaleRatioB, _ScaleRatioC, _ShaderFlags;
                float _VertexOffsetX, _VertexOffsetY, _MaskSoftnessX, _MaskSoftnessY;
                float _PreviewTime;
            CBUFFER_END
            // Canvas supplies these per draw, including CanvasGroup opacity in vertex alpha.
            float _UIMaskSoftnessX, _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float4 uv : TEXCOORD0;      // xy atlas UV, z flake age (flakes only), w signed SDF scale
                float2 effectUV : TEXCOORD1; // TMP texture mapping (Paragraph) UV
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 uv : TEXCOORD0;       // xy atlas UV, z flake age
                float4 sdf : TEXCOORD1;
                float4 mask : TEXCOORD2;
                float2 effectUV : TEXCOORD3;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            #include "TitleLogoCommon.hlsl"

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 position = input.positionOS.xyz + float3(_VertexOffsetX, _VertexOffsetY, 0);
                output.positionCS = TransformObjectToHClip(position);
                float2 pixelSize = output.positionCS.w / max(
                    float2(_ScaleX, _ScaleY) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy)), 0.0001);
                float scale = rsqrt(dot(pixelSize, pixelSize));
                scale *= abs(input.uv.w) * _GradientScale * (_Sharpness + 1);
                if (UNITY_MATRIX_P[3][3] == 0)
                {
                    float facing = abs(dot(TransformObjectToWorldNormal(input.normalOS),
                        GetWorldSpaceNormalizeViewDir(TransformObjectToWorld(position))));
                    scale = lerp(scale * (1 - _PerspectiveFilter), scale, facing);
                }
                float bold = step(input.uv.w, 0);
                float weight = (lerp(_WeightNormal, _WeightBold, bold) / 4 + _FaceDilate) * _ScaleRatioA * 0.5;
                scale /= 1 + _OutlineSoftness * _ScaleRatioA * scale;
                float bias = (0.5 - weight) * scale - 0.5;
                float outline = _OutlineWidth * _ScaleRatioA * 0.5 * scale;
                output.sdf = float4(scale, bias - outline, bias + outline, bias);
                output.uv = input.uv.xyz;
                output.color = input.color;
                #ifndef UNITY_COLORSPACE_GAMMA
                if (_UIVertexColorAlwaysGammaSpace != 0)
                    output.color.rgb = SRGBToLinear(output.color.rgb);
                #endif
                output.effectUV = input.effectUV;
                float4 rect = clamp(_ClipRect, -2e10, 2e10);
                float2 softness = max(float2(_MaskSoftnessX, _MaskSoftnessY), float2(_UIMaskSoftnessX, _UIMaskSoftnessY));
                output.mask = float4(position.xy * 2 - rect.xy - rect.zw, 0.25 / (0.25 * softness + abs(pixelSize)));
                return output;
            }

            half3 LogoFinish(float2 q, float time)
            {
                TitleAccent a = TitleEvaluateAccent(q, time);
                return lerp(_FaceColor.rgb, a.accent, a.burst) + a.overlay;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float time = _PreviewTime >= 0 ? _PreviewTime : _Time.y;
                float2 qp = TitlePixelize(TitleEffectQ(input.effectUV));
                bool isFlake = _IsFlake > 0.5;

                half distance = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv.xy).a * input.sdf.x;
                half outerCoverage = saturate(distance - input.sdf.y);
                half faceCoverage = saturate(distance - input.sdf.z);

                half3 finish = LogoFinish(qp, time) * lerp(half3(1,1,1), input.color.rgb, _VertexTint);
                if (isFlake) finish = TitleFlakeColor(finish, input.uv.z);

                half4 face = half4(finish * _FaceColor.a, _FaceColor.a);
                half4 outline = half4(_OutlineColor.rgb * _OutlineColor.a, _OutlineColor.a);
                outline = lerp(face, outline, sqrt(saturate(input.sdf.z - input.sdf.y)));
                half4 color = lerp(outline, face, faceCoverage) * outerCoverage;
                #ifdef GLOW_ON
                float glowWidth = max(0.001, _GlowOuter * _ScaleRatioB * 0.5 * input.sdf.x);
                float outside = max(0, input.sdf.w - distance - _GlowOffset * _ScaleRatioB * 0.5 * input.sdf.x);
                half glow = pow(saturate(1 - outside / (1 + glowWidth)), _GlowPower) * _GlowColor.a;
                glow *= sqrt(saturate(glowWidth)) * (1 - color.a);
                color += half4(_GlowColor.rgb * glow, glow);
                #endif

                // Disintegrate: char band -> ember edge -> gone. Flakes carry the gone pieces away.
                if (!isFlake && _Disintegrate > 0.0001)
                    TitleApplyDisintegrate(color, qp);

                // Multiply all premultiplied channels for correct CanvasGroup / text alpha fades.
                color *= input.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                half2 clipAmount = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * input.mask.zw);
                color *= clipAmount.x * clipAmount.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
