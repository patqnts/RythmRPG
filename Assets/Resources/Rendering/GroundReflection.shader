Shader "RythmRPG/Ground Reflection Sprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _GroundReflectionTint ("Tint", Color) = (0.36, 0.46, 0.58, 1)
        [HideInInspector] _GroundReflectionSourceColor ("Source Color", Color) = (1, 1, 1, 1)
        _GroundReflectionStrength ("Strength", Range(0, 1)) = 0.3
        _GroundReflectionMaxLength ("Fade Length", Float) = 2.75
        _GroundReflectionDither ("Pixel Dither", Range(0, 1)) = 0.28
        [HideInInspector] _GroundReflectionPlaneY ("Ground Plane", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+25"
            "RenderType" = "Transparent"
            "CanUseSpriteAtlas" = "True"
        }

        Pass
        {
            Name "GroundReflection"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            Stencil
            {
                Ref 64
                ReadMask 64
                Comp Equal
                Pass Keep
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _GroundReflectionTint;
                half4 _GroundReflectionSourceColor;
                half _GroundReflectionStrength;
                half _GroundReflectionMaxLength;
                half _GroundReflectionDither;
            CBUFFER_END

            float _GroundReflectionPlaneY;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float distanceFromGround : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half Bayer4(float2 pixel)
            {
                uint2 q = uint2(floor(abs(pixel))) % 4u;
                static const half values[16] =
                {
                    0, 8, 2, 10,
                    12, 4, 14, 6,
                    3, 11, 1, 9,
                    15, 7, 13, 5
                };
                return (values[q.y * 4u + q.x] + 0.5h) / 16.0h;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float2 spriteFlip = unity_SpriteProps.xy;
                spriteFlip.x = abs(spriteFlip.x) > 0.5 ? spriteFlip.x : 1.0;
                spriteFlip.y = abs(spriteFlip.y) > 0.5 ? spriteFlip.y : 1.0;
                float3 positionOS = input.positionOS.xyz;
                positionOS.xy *= spriteFlip;
                float3 positionWS = TransformObjectToWorld(positionOS);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                bool isSprite = abs(unity_SpriteProps.x) > 0.5 || abs(unity_SpriteProps.y) > 0.5;
                half4 rendererColor = isSprite ? input.color * (half4)unity_SpriteColor : half4(1, 1, 1, 1);
                output.color = rendererColor * _GroundReflectionSourceColor;
                output.distanceFromGround = max(0.0, _GroundReflectionPlaneY - positionWS.y);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half fade = saturate(1.0h - input.distanceFromGround / max(_GroundReflectionMaxLength, 0.001h));
                half alpha = texel.a * input.color.a * _GroundReflectionStrength * fade;
                clip(alpha - 0.002h);

                half dithered = step(Bayer4(input.positionCS.xy), saturate(alpha * 1.35h));
                alpha *= lerp(1.0h, dithered, _GroundReflectionDither);
                clip(alpha - 0.002h);
                half3 color = texel.rgb * input.color.rgb * _GroundReflectionTint.rgb;
                return half4(color, alpha * _GroundReflectionTint.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
