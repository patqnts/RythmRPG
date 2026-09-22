// TMP SDF coverage and padding conventions follow the imported TMP mobile shader.
// Rank effects use TMP texture mapping UVs, so Canvas batching cannot shift the finish.
Shader "RythmRPG/UI/Rank Text SDF (URP)"
{
    Properties
    {
        [Header(Rank Finish)]
        _FaceColor ("Face Color", Color) = (1,1,1,1)
        _ShadowColor ("Gradient Low", Color) = (0.15,0.2,0.4,1)
        _HighlightColor ("Gradient High", Color) = (0.8,0.9,1,1)
        _GradientStrength ("Gradient Strength", Range(0,1)) = 1
        _MetallicBands ("Metallic Bands", Range(0,1)) = 0.4
        _Iridescence ("Iridescence", Range(0,1)) = 0
        _IridescenceSpeed ("Iridescence Speed", Range(0,1)) = 0.12
        _IridescenceScale ("Iridescence Bands", Range(0.1,5)) = 1.1
        _Pearl ("Pearl Highlight", Range(0,1)) = 0.3
        _EffectScale ("Effect Scale", Range(0.1,5)) = 1
        _EffectOffset ("Effect Offset XY", Vector) = (0,0,0,0)
        _VertexTint ("Use Text Vertex RGB", Range(0,1)) = 0

        [Header(Shine)]
        _ShineColor ("Shine Color", Color) = (1,0.98,0.85,1)
        _ShineStrength ("Shine Strength", Range(0,2)) = 0.5
        _ShineSpeed ("Shine Speed", Range(0,2)) = 0.22
        _ShineWidth ("Shine Width", Range(0.01,0.5)) = 0.09
        _ShineAngle ("Shine Angle", Range(-180,180)) = -25

        [Header(SDF Edge)]
        _FaceDilate ("Face Dilate", Range(-1,1)) = 0
        _OutlineColor ("Outline Color", Color) = (0.06,0.04,0.1,1)
        _OutlineWidth ("Outline Thickness", Range(0,1)) = 0.12
        _OutlineSoftness ("Outline Softness", Range(0,1)) = 0
        [Toggle(GLOW_ON)] _EnableGlow ("Enable Edge Glow", Float) = 0
        _GlowColor ("Glow Color", Color) = (0.5,0.6,1,0.3)
        _GlowOffset ("Glow Offset", Range(-1,1)) = 0
        _GlowOuter ("Glow Width", Range(0,1)) = 0.25
        _GlowPower ("Glow Falloff", Range(0.1,4)) = 1.5

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
        // Set to a nonnegative value for a deterministic still preview. -1 uses Unity time.
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
            Name "RankText"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _ GLOW_ON
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _FaceColor, _ShadowColor, _HighlightColor, _OutlineColor, _GlowColor, _ShineColor;
                float4 _EffectOffset, _ClipRect;
                float _GradientStrength, _MetallicBands, _Iridescence, _IridescenceSpeed;
                float _IridescenceScale, _Pearl, _EffectScale, _VertexTint;
                float _ShineStrength, _ShineSpeed, _ShineWidth, _ShineAngle;
                float _FaceDilate, _OutlineWidth, _OutlineSoftness, _EnableGlow;
                float _GlowOffset, _GlowOuter, _GlowPower;
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
                float4 uv : TEXCOORD0; // Unity 6 TMP stores signed SDF scale in w.
                float2 effectUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 sdf : TEXCOORD1;
                float4 mask : TEXCOORD2;
                float2 effectPosition : TEXCOORD3;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

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
                output.uv = input.uv.xy;
                output.color = input.color;
                #ifndef UNITY_COLORSPACE_GAMMA
                if (_UIVertexColorAlwaysGammaSpace != 0)
                    output.color.rgb = SRGBToLinear(output.color.rgb);
                #endif
                output.effectPosition = (input.effectUV - 0.5 + _EffectOffset.xy) * _EffectScale;
                float4 rect = clamp(_ClipRect, -2e10, 2e10);
                float2 softness = max(float2(_MaskSoftnessX, _MaskSoftnessY), float2(_UIMaskSoftnessX, _UIMaskSoftnessY));
                output.mask = float4(position.xy * 2 - rect.xy - rect.zw, 0.25 / (0.25 * softness + abs(pixelSize)));
                return output;
            }

            half3 RankFinish(float2 p, float time)
            {
                float gradient = saturate(p.y + 0.5);
                float band = 0.5 + 0.5 * sin((p.y + p.x * 0.12) * 10.0 + 0.7);
                gradient = lerp(gradient, smoothstep(0.1, 0.9, band), _MetallicBands);
                half3 baseColor = lerp(_FaceColor.rgb,
                    _FaceColor.rgb * lerp(_ShadowColor.rgb, _HighlightColor.rgb, gradient), _GradientStrength);

                // Stylized thin-film color: visible on a flat orthographic Canvas, without lights.
                float phase = (p.x * 0.55 + p.y + 0.16 * sin(p.x * 5 + time * 0.6)) * _IridescenceScale;
                phase += time * _IridescenceSpeed;
                half3 spectrum = 0.5 + 0.5 * cos(6.2831853 * (phase + float3(0, 0.33333, 0.66667)));
                spectrum = lerp(spectrum, half3(1, 0.98, 1), _Pearl * 0.6);
                half3 pearl = spectrum * (0.68 + 0.32 * gradient);
                baseColor = lerp(baseColor, pearl * _FaceColor.rgb, _Iridescence);

                float angle = radians(_ShineAngle);
                float along = dot(p, float2(cos(angle), sin(angle)));
                float sweep = frac(along * 0.32 - time * _ShineSpeed + 0.5) - 0.5;
                float shine = 1 - smoothstep(0, _ShineWidth, abs(sweep));
                return baseColor + _ShineColor.rgb * (shine * shine * _ShineStrength);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float time = _PreviewTime >= 0 ? _PreviewTime : _Time.y;
                half distance = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a * input.sdf.x;
                half outerCoverage = saturate(distance - input.sdf.y);
                half faceCoverage = saturate(distance - input.sdf.z);
                half3 finish = RankFinish(input.effectPosition, time) * lerp(half3(1,1,1), input.color.rgb, _VertexTint);
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
                // Multiply all premultiplied channels, including the glow, for correct fades.
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
