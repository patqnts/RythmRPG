Shader "RythmRPG/UI/Artifact Living Outline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _RectSize ("Rect size", Vector) = (100,100,0,0)
        _RectCenter ("Rect center", Vector) = (0,0,0,0)
        [HideInInspector] _ComponentSurface ("Whole component mask", Float) = 0
        _PokeAmount ("Edge disturbance", Float) = 0
        _PokePhase ("Noise flow", Float) = 0
        _WobblePixels ("Displacement pixels", Float) = 18
        _GlitchAmount ("Edge disintegration", Float) = 0
        _Disintegrate ("Edge dissolve progress", Float) = 0
        _EdgeDepth ("Depth inward from outer sides", Float) = 24
        _EmberWidth ("Dissolve ember band", Float) = .034
        _CharWidth ("Dissolve char band", Float) = .035
        _AshWidth ("Dissolve ash band", Float) = .02
        [HideInInspector] _DirectionBias ("Direction bias", Float) = 0
        [HideInInspector] _DisintegrateAngle ("Direction", Float) = 0
        [HideInInspector] _EffectAspect ("Aspect", Float) = 1
        [HideInInspector] _Pixelate ("Pixel dissolve", Float) = 1
        _PixelSize ("Pixel step", Float) = 2
        _DissolveScale ("Dissolve noise scale", Float) = 5
        _NoiseSeed ("Noise seed", Float) = 7
        _NoiseAmount ("Noise amount", Float) = 1
        [HDR] _EmberColor ("Ember", Color) = (1,.45,.08,1)
        [HDR] _EmberHotColor ("Hot ember", Color) = (1,.92,.6,1)
        _AshColor ("Ash", Color) = (.16,.14,.13,1)
        _CharColor ("Char", Color) = (.015,.02,.025,1)
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            float4 _RectSize, _RectCenter;
            float _PokeAmount, _PokePhase, _WobblePixels, _GlitchAmount, _PixelSize, _ComponentSurface, _EdgeDepth, _NoiseAmount;
            float4 _CharColor;
            float _AshWidth;
            // Declare the title include's inputs; only its shared noise functions are used here.
            float4 _FaceColor, _GoldShadow, _GoldColor, _GoldHighlight, _CoreColor, _RingColor;
            float4 _EmberColor, _EmberHotColor, _AshColor, _ShineColor, _FocusPoint;
            float _GoldFill, _BurstRadius, _BurstSoftness, _BurstBreakup, _GradientMix, _CoreSize, _CoreIntensity;
            float _GrungeScale, _GrungeStrength, _GrungeContrast, _GrungeFlow, _GrungeTexStrength, _GrungeTexTiling;
            float _RingStrength, _RingRadius, _RingCount, _RingWidth, _RingSwirl, _RingTilt, _RingSpeed;
            float _PulseAmount, _PulseSpeed, _Pixelate, _PixelDensity;
            float _Disintegrate, _DisintegrateAngle, _DirectionBias, _DissolveScale, _EmberWidth, _CharWidth, _NoiseSeed;
            float _ShineStrength, _ShineSpeed, _ShineWidth, _ShineAngle, _EffectAspect;
            float4 _ClipRect;
            float _UIMaskSoftnessX, _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;
            #include "TitleLogoCommon.hlsl"
            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 local : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 local : TEXCOORD0; float4 mask : TEXCOORD1; half4 color : COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.local = input.local;
                o.color = input.color;
                #ifndef UNITY_COLORSPACE_GAMMA
                if (_UIVertexColorAlwaysGammaSpace != 0) o.color.rgb = SRGBToLinear(o.color.rgb);
                #endif
                float2 pixelSize = o.positionCS.w / max(abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy)), .0001);
                float4 rect = clamp(_ClipRect, -2e10, 2e10);
                o.mask = float4(input.positionOS.xy * 2 - rect.xy - rect.zw,
                    .25 / (.25 * float2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize)));
                return o;
            }
            half4 ClipOutline(half4 c, Varyings i)
            {
                #ifdef UNITY_UI_CLIP_RECT
                half2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(i.mask.xy)) * i.mask.zw);
                c *= mask.x * mask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a - .001);
                #endif
                return c;
            }
            float OutlineDistance(float2 p)
            {
                float2 corner = abs(p) - (_RectSize.xy * .5 - 10);
                return length(max(corner, 0)) + min(max(corner.x, corner.y), 0) - 8;
            }
            float EdgeInfluence(float2 p)
            {
                float depth = min(max(0, _EdgeDepth), min(_RectSize.x, _RectSize.y) * .45);
                return depth > 0 ? saturate(1 + min(0, OutlineDistance(p)) / depth) : 0;
            }
            float EdgeDissolveRemaining(float2 p, float2 q)
            {
                // The same field clips all contents, but its progress falls to zero toward the center.
                float cut = _Disintegrate * EdgeInfluence(p) * (1.02 + _EmberWidth + _CharWidth) - _CharWidth;
                float field = saturate(.5 + (TitleDissolveField(q) - .5) * _NoiseAmount);
                return field - cut;
            }
            void ApplyEdgeDisintegrate(inout half4 color, float2 p, float2 q)
            {
                // Title-menu char -> ember -> transparent, confined to the item's outer region.
                if (_Disintegrate <= 0 || EdgeInfluence(p) <= 0) return;
                float s = EdgeDissolveRemaining(p, q);
                float alive = smoothstep(-_EmberWidth - 1e-5, -_EmberWidth + 1e-5, s);
                float ember = (1 - smoothstep(-1e-5, 1e-5, s)) * alive;
                float charred = (1 - smoothstep(0, max(_CharWidth, 1e-4), s)) * (1 - ember);
                float ash = _AshWidth > 0 ? (1 - smoothstep(_CharWidth, _CharWidth + _AshWidth, s)) * (1 - ember) * (1 - charred) : 0;
                half3 hot = lerp(_EmberColor.rgb, _EmberHotColor.rgb, saturate(-s / max(_EmberWidth, 1e-4)));
                color.rgb = lerp(color.rgb, _AshColor.rgb * color.a, ash * .6);
                color.rgb = lerp(color.rgb, _CharColor.rgb * color.a, charred * .85);
                color.rgb = lerp(color.rgb, hot * color.a, ember);
                color *= alive;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float pixel = max(1, _PixelSize);
                float2 p = (floor((i.local + _RectSize.xy * .5) / pixel) + .5) * pixel - _RectSize.xy * .5;
                float distance = OutlineDistance(p);
                if (_PokeAmount + _GlitchAmount < .0001)
                {
                    if (_ComponentSurface > 1.5) return half4(0, 0, 0, 0);
                    half alpha = (_ComponentSurface > .5 ? 1 - smoothstep(2.5, 3.5, distance) : 1 - smoothstep(1.5, 2.5, abs(distance))) * i.color.a;
                    return ClipOutline(half4(i.color.rgb * alpha, alpha), i);
                }
                float2 q = p / max(48, _RectSize.y);
                float2 flow = float2(_PokePhase * .38, -_PokePhase * .21);
                uint seed = (uint)_NoiseSeed;
                float noise = .5 + (TitleFbm(q * _DissolveScale + flow, seed) - .5) * _NoiseAmount;
                float pulse = cos(_PokePhase * 6.2831853);
                float warp = (noise - .5) * 3.5 * _WobblePixels * _PokeAmount * pulse;
                distance -= warp;
                if (_ComponentSurface > .5)
                {
                    // Include the full four-pixel stroke at the component's moving boundary.
                    float coverage = 1 - smoothstep(2.5, 3.5, distance);
                    half4 surface = half4(i.color.rgb * coverage, coverage);
                    ApplyEdgeDisintegrate(surface, p, q + flow);
                    if (_ComponentSurface > 1.5)
                    {
                        // The shared transition band only crosses contents close to the outer sides.
                        float remaining = EdgeDissolveRemaining(p, q + flow);
                        float band = (1 - smoothstep(0, max(_CharWidth + _AshWidth, .001), remaining)) * saturate(_GlitchAmount * 3) * step(1e-5, EdgeInfluence(p));
                        surface *= band;
                    }
                    surface *= i.color.a;
                    return ClipOutline(surface, i);
                }
                float edge = abs(distance);
                float white = 1 - smoothstep(1.5, 2.5, edge);
                // Apply the title's actual alpha-erasing dissolve to the displaced stroke.
                // Its char/ember bands sit on surviving fragments, leaving real transparent gaps.
                half4 c = half4(i.color.rgb * white, white);
                ApplyEdgeDisintegrate(c, p, q + flow);

                // Sparse pixel fragments lift away from erased portions of the same outline.
                float2 side = abs(p) - _RectSize.xy * .5;
                float2 normal = side.x > side.y ? float2(sign(p.x), 0) : float2(0, sign(p.y));
                float2 drift = (normal * (4 + _PokePhase * 4) + float2(_PokePhase * 2, _PokePhase * 3)) * saturate(_GlitchAmount * 2);
                float2 source = p - drift;
                float2 sourceQ = source / max(48, _RectSize.y);
                float sourceNoise = .5 + (TitleFbm(sourceQ * _DissolveScale + flow, seed) - .5) * _NoiseAmount;
                float sourceDistance = OutlineDistance(source) - (sourceNoise - .5) * 3.5 * _WobblePixels * _PokeAmount * pulse;
                float sourceStroke = 1 - smoothstep(1.5, 2.5, abs(sourceDistance));
                float gone = 1 - smoothstep(-_EmberWidth - .002, -_EmberWidth + .002, EdgeDissolveRemaining(source, sourceQ + flow));
                float speck = step(.72, TitleHash01((int2)floor(source / pixel), seed + 71u));
                float flakeAlpha = sourceStroke * gone * speck * saturate(_GlitchAmount * 2) * saturate(_NoiseAmount) * .9;
                half3 flake = TitleFlakeColor(_EmberHotColor.rgb, saturate(_PokePhase * .45));
                c = half4(flake * flakeAlpha, flakeAlpha) + c * (1 - flakeAlpha);
                c *= i.color.a;
                return ClipOutline(c, i);
            }
            ENDHLSL
        }
    }
}
