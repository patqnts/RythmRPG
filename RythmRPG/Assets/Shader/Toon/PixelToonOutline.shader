Shader "RythmRPG/Pixel Toon Outline"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _ShadeColor ("Shadow Tint", Color) = (0.45, 0.50, 0.65, 1)
        _AmbientColor ("Ambient Color", Color) = (0.28, 0.30, 0.38, 1)
        _ShadowThreshold ("Light / Shadow Split", Range(0, 1)) = 0.5
        _BandSoftness ("Band Softness", Range(0.001, 0.25)) = 0.015
        _OutlineColor ("Outline Color", Color) = (0.035, 0.03, 0.055, 1)
        _OutlineWidth ("Outline Width (Pixels)", Range(0, 8)) = 2
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
        }

        // This is the regular object pass. SRPDefaultUnlit is intentional: it
        // lets this 3D material render in both URP's Forward and 2D renderers.
        Pass
        {
            Name "Pixel Toon"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ToonVertex
            #pragma fragment ToonFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadeColor;
                half4 _AmbientColor;
                half4 _OutlineColor;
                half _ShadowThreshold;
                half _BandSoftness;
                half _OutlineWidth;
                half _Cutoff;
                half _Cull;
            CBUFFER_END

            Varyings ToonVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                return output;
            }

            half4 ToonFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 albedo = texel * _BaseColor * input.color;
                clip(albedo.a - _Cutoff);

                half3 normalWS = normalize(input.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half nDotL = saturate(dot(normalWS, mainLight.direction));
                half attenuation = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                half lightAmount = nDotL * attenuation;
                half litBand = smoothstep(
                    _ShadowThreshold - _BandSoftness,
                    _ShadowThreshold + _BandSoftness,
                    lightAmount);

                half3 toonLight = lerp(_ShadeColor.rgb, half3(1, 1, 1), litBand);
                half3 direct = albedo.rgb * toonLight * mainLight.color;
                half3 ambient = albedo.rgb * _AmbientColor.rgb;
                return half4(direct + ambient, albedo.a);
            }
            ENDHLSL
        }

        // URP draws one tagged material pass per object. PixelToonOutlineFeature
        // requests this second pass after scene geometry has populated depth.
        Pass
        {
            Name "Pixel Outline"
            Tags { "LightMode" = "PixelToonOutline" }

            Cull Front
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadeColor;
                half4 _AmbientColor;
                half4 _OutlineColor;
                half _ShadowThreshold;
                half _BandSoftness;
                half _OutlineWidth;
                half _Cutoff;
                half _Cull;
            CBUFFER_END

            Varyings OutlineVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float4 positionCS = TransformWorldToHClip(positionWS);

                // Expanding in clip space makes the setting stay approximately
                // constant in low-resolution output pixels at any camera distance.
                float2 normalCS = TransformWorldToHClipDir(normalWS, true).xy;
                float normalLength = max(length(normalCS), 0.0001);
                float2 pixelOffset = (normalCS / normalLength) * _OutlineWidth * 2.0;
                positionCS.xy += pixelOffset * positionCS.w / _ScaledScreenParams.xy;

                output.positionCS = positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 OutlineFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                clip(alpha - _Cutoff);
                return _OutlineColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
