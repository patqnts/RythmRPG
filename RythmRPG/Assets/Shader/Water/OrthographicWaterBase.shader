Shader "JonQuin/URP/OrthographicWaterBase"
{
    Properties
    {
        [Header(Depth Color)]
        [HDR]_ShallowColor("Shallow Color", Color) = (0.10, 0.72, 0.78, 0.72)
        [HDR]_DeepColor("Deep Color", Color) = (0.015, 0.18, 0.38, 0.94)
        _DepthDistance("Depth Distance", Range(0.05, 20)) = 3.0
        _DepthContrast("Depth Contrast", Range(0.25, 4)) = 1.0
        _Opacity("Water Opacity", Range(0, 1)) = 0.82

        [Header(Refraction)]
        _RefractionStrength("Refraction Strength", Range(0, 0.1)) = 0.015
        _RefractionScale("Refraction Scale", Range(0.05, 20)) = 2.0
        _RefractionSpeed("Refraction Speed XY", Vector) = (0.08, 0.045, 0, 0)
        _RefractionDepthInfluence("Refraction Depth Influence", Range(0, 1)) = 0.65

        [Header(Intersection Foam)]
        [HDR]_FoamColor("Foam Color", Color) = (0.95, 1.0, 1.0, 1)
        _FoamDistance("Foam Width", Range(0.005, 2)) = 0.18
        _FoamStrength("Foam Strength", Range(0, 1)) = 1.0
        _FoamNoiseScale("Foam Noise Scale", Range(0.1, 30)) = 6.0
        _FoamNoiseAmount("Foam Noise Amount", Range(0, 1)) = 0.25
        _FoamSpeed("Foam Noise Speed", Range(0, 5)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }

        Pass
        {
            Name "Water"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                float _DepthDistance;
                float _DepthContrast;
                float _Opacity;

                float _RefractionStrength;
                float _RefractionScale;
                float4 _RefractionSpeed;
                float _RefractionDepthInfluence;

                half4 _FoamColor;
                float _FoamDistance;
                float _FoamStrength;
                float _FoamNoiseScale;
                float _FoamNoiseAmount;
                float _FoamSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                float fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;

                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);

                return o;
            }

            // Smooth, texture-free distortion. Kept intentionally simple so this
            // base shader has no external texture dependencies.
            float2 WaterDistortion(float2 worldXZ)
            {
                float2 p = worldXZ * _RefractionScale;
                float2 t = _RefractionSpeed.xy * _Time.y;

                float x =
                    sin(p.x * 1.17 + p.y * 0.73 + t.x * 6.0) +
                    sin(p.y * 1.91 - t.y * 4.3);

                float y =
                    cos(p.y * 1.31 - p.x * 0.61 + t.y * 5.2) +
                    cos(p.x * 1.67 + t.x * 3.7);

                return float2(x, y) * 0.5;
            }

            float FoamNoise(float2 worldXZ)
            {
                float2 p = worldXZ * _FoamNoiseScale;
                float t = _Time.y * _FoamSpeed;

                float n =
                    sin(p.x + sin(p.y * 1.31 + t)) +
                    sin(p.y * 1.73 - p.x * 0.47 - t * 0.83);

                return n * 0.25 + 0.5;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 screenUV = input.screenPos.xy / input.screenPos.w;

                // ------------------------------------------------------------
                // ORTHOGRAPHIC DEPTH / THICKNESS
                //
                // Both values are measured in camera/view space along the
                // orthographic camera's forward axis. Their difference gives
                // the visible water thickness behind this water fragment.
                // ------------------------------------------------------------
                float rawSceneDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(rawSceneDepth, _ZBufferParams);

                float3 waterPositionVS = TransformWorldToView(input.positionWS);
                float waterEyeDepth = -waterPositionVS.z;

                float thickness = max(0.0, sceneEyeDepth - waterEyeDepth);

                // ------------------------------------------------------------
                // 1. DEPTH COLOR
                // ------------------------------------------------------------
                float depth01 = saturate(thickness / max(_DepthDistance, 0.0001));
                depth01 = pow(depth01, max(_DepthContrast, 0.0001));

                half3 depthColor = lerp(
                    _ShallowColor.rgb,
                    _DeepColor.rgb,
                    depth01
                );

                // ------------------------------------------------------------
                // 2. REFRACTION
                //
                // Sample the URP Opaque Texture with a moving UV offset.
                // Shallow water can be kept calmer while deeper water receives
                // more of the requested distortion.
                // ------------------------------------------------------------
                float2 distortion = WaterDistortion(input.positionWS.xz);

                float depthRefraction = lerp(
                    1.0,
                    depth01,
                    _RefractionDepthInfluence
                );

                float2 refractedUV =
                    screenUV +
                    distortion *
                    _RefractionStrength *
                    depthRefraction;

                refractedUV = clamp(refractedUV, 0.001, 0.999);

                half3 refractedScene = SampleSceneColor(refractedUV);

                // Tint the refracted scene rather than replacing it.
                half3 color = refractedScene * depthColor;

                // ------------------------------------------------------------
                // 3. AUTOMATIC INTERSECTION FOAM
                //
                // thickness ~= 0 where opaque depth-writing geometry reaches
                // the water surface. This creates a border without physics,
                // tags, layers, or per-object scripts.
                // ------------------------------------------------------------
                float foamDistance = max(_FoamDistance, 0.0001);

                // Noise slightly changes the effective foam width, producing
                // an organic edge while preserving a continuous contact line.
                float noise = saturate(FoamNoise(input.positionWS.xz));
                float widthVariation =
                    lerp(
                        1.0 - _FoamNoiseAmount,
                        1.0 + _FoamNoiseAmount,
                        noise
                    );

                float effectiveFoamDistance =
                    foamDistance * widthVariation;

                float foam =
                    1.0 -
                    smoothstep(
                        0.0,
                        effectiveFoamDistance,
                        thickness
                    );

                foam = saturate(foam * _FoamStrength);

                color = lerp(
                    color,
                    _FoamColor.rgb,
                    foam * _FoamColor.a
                );

                color = MixFog(color, input.fogFactor);

                // Depth can also influence alpha:
                // shallow = shallow color alpha
                // deep    = deep color alpha
                float depthAlpha =
                    lerp(
                        _ShallowColor.a,
                        _DeepColor.a,
                        depth01
                    );

                float alpha =
                    saturate(
                        depthAlpha * _Opacity +
                        foam * _FoamColor.a * 0.25
                    );

                return half4(color, alpha);
            }

            ENDHLSL
        }
    }

    FallBack Off
}
