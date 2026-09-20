Shader "PixelMetaballParticles/Pixel Metaball Outlined Particle"
{
    Properties
    {
        [MainTexture] _BaseMap ("Particle Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1,1,1,1)

        [Header(Visible Particle)]
        _AlphaCutoff ("Visible Alpha Cutoff", Range(0,1)) = 0.01

        [Header(Metaball Field)]
        _MetaballFieldStrength ("Field Strength", Range(0.05,4)) = 1
        _MetaballFieldPower ("Field Power", Range(0.1,4)) = 1
        _MetaballMaskCutoff ("Tiny Alpha Cutoff", Range(0,0.2)) = 0.001
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        // ================================================================
        // NORMAL VISIBLE PARTICLE
        // ================================================================
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _AlphaCutoff;
                float _MetaballFieldStrength;
                float _MetaballFieldPower;
                float _MetaballMaskCutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS =
                    TransformObjectToHClip(input.positionOS.xyz);

                output.uv =
                    TRANSFORM_TEX(input.uv, _BaseMap);

                output.color = input.color;

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 tex =
                    SAMPLE_TEXTURE2D(
                        _BaseMap,
                        sampler_BaseMap,
                        input.uv);

                half4 color =
                    tex * _BaseColor * input.color;

                clip(color.a - _AlphaCutoff);

                return color;
            }
            ENDHLSL
        }

        // ================================================================
        // SHARED METABALL FIELD
        //
        // URP does NOT draw this during its normal transparent pass because
        // it has a custom LightMode. The Renderer Feature explicitly requests
        // this pass and draws every matching particle into ONE R8 texture.
        //
        // Additive blending is intentional:
        //     field = particleA + particleB + particleC ...
        //
        // Soft alpha edges can therefore cross the renderer feature's
        // threshold together, which gives a proper metaball-like fusion.
        // ================================================================
        Pass
        {
            Name "MergedParticleMask"
            Tags { "LightMode" = "MergedParticleMask" }

            ZWrite Off
            ZTest LEqual
            Cull Off

            Blend One One
            BlendOp Add
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VertMask
            #pragma fragment FragMask
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _AlphaCutoff;
                float _MetaballFieldStrength;
                float _MetaballFieldPower;
                float _MetaballMaskCutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings VertMask(Attributes input)
            {
                Varyings output;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS =
                    TransformObjectToHClip(input.positionOS.xyz);

                output.uv =
                    TRANSFORM_TEX(input.uv, _BaseMap);

                output.color = input.color;

                return output;
            }

            half4 FragMask(Varyings input) : SV_Target
            {
                half alpha =
                    SAMPLE_TEXTURE2D(
                        _BaseMap,
                        sampler_BaseMap,
                        input.uv).a;

                alpha *= _BaseColor.a * input.color.a;

                // Power controls the shape of each particle's scalar field:
                //
                // < 1 : softer/wider influence, easier merging
                // = 1 : original texture alpha
                // > 1 : tighter influence
                half field =
                    pow(
                        saturate(alpha),
                        max((half)0.1, (half)_MetaballFieldPower));

                field *= _MetaballFieldStrength;

                clip(field - _MetaballMaskCutoff);

                return half4(field, 0, 0, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
