// Title logo finish for UI Image / RawImage (sprites), URP. Same accent burst, grunge, swirl, pixelate and
// Disintegrate as TitleLogoSDF.shader, but coverage comes from the sprite's alpha instead of a font SDF.
//
// Driven by TitleLogoImage.cs, which writes the effect UV (0..1 over the drawn image) into TEXCOORD1,
// sets _EffectAspect / _FocusPoint / _PixelDensity, and draws the flakes (second draw with _IsFlake = 1,
// flake age in TEXCOORD0.z). Shared code: TitleLogoCommon.hlsl (mirrored in TitleLogoNoise.cs).
Shader "RythmRPG/UI/Title Logo Sprite (URP)"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        [Header(Base)]
        _FaceColor ("Ink Color", Color) = (0.02,0.018,0.016,1)
        _InkMix ("Ink Replace (0 = keep sprite colors)", Range(0,1)) = 0
        _DetailKeep ("Keep Sprite Shading In Accent", Range(0,1)) = 0.35
        _SpriteOutlineColor ("Outline Color", Color) = (0,0,0,1)
        _SpriteOutlineWidth ("Outline Width (texels)", Range(0,4)) = 0

        [Header(Accent Burst)]
        _GoldFill ("Fill Whole Image", Range(0,1)) = 0
        _BurstRadius ("Burst Radius (image heights)", Range(0,3)) = 0.33
        _BurstSoftness ("Burst Softness", Range(0.01,1)) = 0.5
        _BurstBreakup ("Burst Edge Breakup", Range(0,1)) = 0.6
        _GoldShadow ("Accent Shadow", Color) = (0.22,0.09,0.02,1)
        _GoldColor ("Accent", Color) = (0.86,0.52,0.1,1)
        _GoldHighlight ("Accent Highlight", Color) = (1,0.9,0.45,1)
        _GradientMix ("Vertical Gradient Mix", Range(0,1)) = 0.2
        [HDR] _CoreColor ("Core Glow", Color) = (1,0.95,0.7,1)
        _CoreSize ("Core Size", Range(0,1)) = 0.06
        _CoreIntensity ("Core Intensity", Range(0,4)) = 1

        [Header(Grunge)]
        _GrungeScale ("Grunge Scale", Range(1,80)) = 24
        _GrungeStrength ("Grunge Strength", Range(0,1)) = 0.75
        _GrungeContrast ("Grunge Contrast", Range(0.5,4)) = 1.8
        _GrungeFlow ("Grunge Flow Speed", Range(0,1)) = 0.02
        [NoScaleOffset] _GrungeTex ("Grunge Texture (optional, R)", 2D) = "white" {}
        _GrungeTexStrength ("Grunge Texture Strength", Range(0,1)) = 0
        _GrungeTexTiling ("Grunge Texture Tiling", Float) = 2

        [Header(Halo Swirl)]
        [HDR] _RingColor ("Swirl Color", Color) = (1,0.97,0.88,1)
        _RingStrength ("Swirl Strength", Range(0,2)) = 0.9
        _RingRadius ("Swirl Radius", Range(0,1)) = 0.12
        _RingCount ("Swirl Turns", Range(1,6)) = 2.5
        _RingWidth ("Swirl Line Width", Range(0.002,0.06)) = 0.008
        _RingSwirl ("Swirl Twist", Range(0,2)) = 1
        _RingTilt ("Swirl Tilt (Y squash)", Range(0.1,1)) = 0.5
        _RingSpeed ("Swirl Spin Speed", Range(-3,3)) = 0.35

        [Header(Pulse)]
        _PulseAmount ("Pulse Amount", Range(0,1)) = 0.12
        _PulseSpeed ("Pulse Speed", Range(0,4)) = 0.5

        [Header(Pixelate)]
        [MaterialToggle] _Pixelate ("Pixelate", Float) = 0
        _PixelDensity ("Pixels Per Image Height", Range(8,512)) = 64

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

        [Header(Driven By TitleLogoImage)]
        _EffectAspect ("Effect Aspect (w/h)", Float) = 1
        _FocusPoint ("Focus Point (effect UV)", Vector) = (0.5,0.5,0,0)
        [HideInInspector] _IsFlake ("Is Flake", Float) = 0
        [HideInInspector] _PreviewTime ("Preview Time", Float) = -1

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("UI Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline" "PreviewType"="Plane" "CanUseSpriteAtlas"="True"
        }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "TitleLogoSprite"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_TexelSize;
                float4 _FaceColor, _SpriteOutlineColor, _GoldShadow, _GoldColor, _GoldHighlight, _CoreColor, _RingColor;
                float4 _EmberColor, _EmberHotColor, _AshColor, _ShineColor, _FocusPoint;
                float _InkMix, _DetailKeep, _SpriteOutlineWidth;
                float _GoldFill, _BurstRadius, _BurstSoftness, _BurstBreakup, _GradientMix, _CoreSize, _CoreIntensity;
                float _GrungeScale, _GrungeStrength, _GrungeContrast, _GrungeFlow, _GrungeTexStrength, _GrungeTexTiling;
                float _RingStrength, _RingRadius, _RingCount, _RingWidth, _RingSwirl, _RingTilt, _RingSpeed;
                float _PulseAmount, _PulseSpeed, _Pixelate, _PixelDensity;
                float _Disintegrate, _DisintegrateAngle, _DirectionBias, _DissolveScale, _EmberWidth, _CharWidth, _NoiseSeed;
                float _ShineStrength, _ShineSpeed, _ShineWidth, _ShineAngle;
                float _EffectAspect, _IsFlake, _PreviewTime;
            CBUFFER_END
            // Set by the Canvas per draw.
            float4 _ClipRect;
            float4 _TextureSampleAdd;
            float _UIMaskSoftnessX, _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            #include "TitleLogoCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float4 uv : TEXCOORD0;       // xy sprite UV, z flake age (flakes only)
                float2 effectUV : TEXCOORD1; // 0..1 over the drawn image (written by TitleLogoImage)
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 uv : TEXCOORD0;
                float2 effectUV : TEXCOORD1;
                float4 mask : TEXCOORD2;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv.xyz;
                output.effectUV = input.effectUV;
                output.color = input.color;
                #ifndef UNITY_COLORSPACE_GAMMA
                if (_UIVertexColorAlwaysGammaSpace != 0)
                    output.color.rgb = SRGBToLinear(output.color.rgb);
                #endif
                float2 pixelSize = output.positionCS.w / max(abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy)), 0.0001);
                float4 rect = clamp(_ClipRect, -2e10, 2e10);
                output.mask = float4(input.positionOS.xy * 2 - rect.xy - rect.zw,
                    0.25 / (0.25 * float2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize)));
                return output;
            }

            half SampleAlpha(float2 uv)
            {
                return (SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) + _TextureSampleAdd).a;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float time = _PreviewTime >= 0 ? _PreviewTime : _Time.y;
                float2 qp = TitlePixelize(TitleEffectQ(input.effectUV));
                bool isFlake = _IsFlake > 0.5;

                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv.xy) + _TextureSampleAdd;
                half3 spriteRGB = tex.rgb * input.color.rgb;
                half alpha = tex.a;

                // Base: the sprite's own colors, optionally replaced by a flat ink.
                TitleAccent a = TitleEvaluateAccent(qp, time);
                half3 baseRGB = lerp(spriteRGB, _FaceColor.rgb, _InkMix);
                // Keep some of the sprite's shading inside the accent so pixel-art detail survives.
                half luma = dot(spriteRGB, half3(0.299, 0.587, 0.114));
                half shading = lerp(1.0, 0.55 + luma * 0.9, _DetailKeep);
                half3 finish = lerp(baseRGB, a.accent * shading, a.burst) + a.overlay;
                if (isFlake) finish = TitleFlakeColor(finish, input.uv.z);

                half4 color = half4(finish * alpha, alpha);

                // Pixel outline: opaque neighbours within N texels, drawn behind the sprite.
                if (_SpriteOutlineWidth > 0.001 && !isFlake)
                {
                    float2 o = _MainTex_TexelSize.xy * _SpriteOutlineWidth;
                    half n = SampleAlpha(input.uv.xy + float2(o.x, 0));
                    n = max(n, SampleAlpha(input.uv.xy - float2(o.x, 0)));
                    n = max(n, SampleAlpha(input.uv.xy + float2(0, o.y)));
                    n = max(n, SampleAlpha(input.uv.xy - float2(0, o.y)));
                    n = max(n, SampleAlpha(input.uv.xy + o));
                    n = max(n, SampleAlpha(input.uv.xy - o));
                    n = max(n, SampleAlpha(input.uv.xy + float2(o.x, -o.y)));
                    n = max(n, SampleAlpha(input.uv.xy + float2(-o.x, o.y)));
                    half outlineA = saturate(n) * _SpriteOutlineColor.a * (1 - color.a);
                    color += half4(_SpriteOutlineColor.rgb * outlineA, outlineA);
                }

                if (!isFlake && _Disintegrate > 0.0001)
                    TitleApplyDisintegrate(color, qp);

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
