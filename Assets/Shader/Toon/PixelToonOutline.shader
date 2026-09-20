Shader "RythmRPG/Pixel Toon Outline"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)
        _ShadeColor ("Shadow Tint", Color) = (0.45,0.50,0.65,1)
        _AmbientColor ("Ambient Color", Color) = (0.28,0.30,0.38,1)
        _ShadowThreshold ("Light / Shadow Split", Range(0,1)) = 0.5
        _BandSoftness ("Band Softness", Range(0.001,0.25)) = 0.015
        _OutlineColor ("Outline Color", Color) = (0.035,0.03,0.055,1)
        _OutlineWidth ("Outline Width (Pixels)", Range(0,8)) = 2
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="TransparentCutout"
            "Queue"="AlphaTest"
        }

        Pass
        {
            Name "Pixel Toon"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes {
                float4 positionOS:POSITION; float3 normalOS:NORMAL;
                float2 uv:TEXCOORD0; half4 color:COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings {
                float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0;
                half3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; half4 color:COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor,_ShadeColor,_AmbientColor,_OutlineColor;
            half _ShadowThreshold,_BandSoftness,_OutlineWidth,_Cutoff,_Cull;
            CBUFFER_END

            Varyings Vert(Attributes i) {
                Varyings o=(Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i,o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p=GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS=p.positionCS; o.positionWS=p.positionWS;
                o.normalWS=TransformObjectToWorldNormal(i.normalOS);
                o.uv=TRANSFORM_TEX(i.uv,_BaseMap); o.color=i.color; return o;
            }

            half Band(half x) {
                return smoothstep(_ShadowThreshold-_BandSoftness,
                                  _ShadowThreshold+_BandSoftness,x);
            }

            half3 Additional(half3 albedo, Light l) {
                // Normal-independent on purpose for vertical 2D sprite planes.
                half a=l.distanceAttenuation*l.shadowAttenuation;
                half3 tint=lerp(_ShadeColor.rgb,half3(1,1,1),Band(a));
                return albedo*tint*l.color*a;
            }

            half4 Frag(Varyings i):SV_Target {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 s=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv)*_BaseColor*i.color;
                clip(s.a-_Cutoff);

                half3 n=normalize(i.normalWS);
                float4 sc=TransformWorldToShadowCoord(i.positionWS);
                Light main=GetMainLight(sc);
                half ma=main.distanceAttenuation*main.shadowAttenuation;
                half ndl=saturate(dot(n,main.direction));
                half3 tint=lerp(_ShadeColor.rgb,half3(1,1,1),Band(ndl*ma));
                half3 result=s.rgb*tint*main.color*ma;

                #if defined(_ADDITIONAL_LIGHTS)
                uint count=GetAdditionalLightsCount();

                #if USE_FORWARD_PLUS
                UNITY_LOOP for(uint li=0;li<min(URP_FP_DIRECTIONAL_LIGHTS_COUNT,MAX_VISIBLE_LIGHTS);li++) {
                    FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
                    Light l=GetAdditionalLight(li,i.positionWS,half4(1,1,1,1));
                    result+=Additional(s.rgb,l);
                }
                #endif

                LIGHT_LOOP_BEGIN(count)
                    Light l=GetAdditionalLight(lightIndex,i.positionWS,half4(1,1,1,1));
                    result+=Additional(s.rgb,l);
                LIGHT_LOOP_END
                #endif

                result+=s.rgb*_AmbientColor.rgb;
                return half4(result,s.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Pixel Outline"
            Tags { "LightMode"="PixelToonOutline" }
            Cull Front
            ZWrite Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex OVert
            #pragma fragment OFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST; half4 _BaseColor,_ShadeColor,_AmbientColor,_OutlineColor;
            half _ShadowThreshold,_BandSoftness,_OutlineWidth,_Cutoff,_Cull;
            CBUFFER_END

            V OVert(A i) {
                V o=(V)0; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i,o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws=TransformObjectToWorld(i.positionOS.xyz);
                float3 nw=TransformObjectToWorldNormal(i.normalOS);
                float4 cs=TransformWorldToHClip(ws);
                float2 nc=TransformWorldToHClipDir(nw,true).xy;
                float2 po=(nc/max(length(nc),0.0001))*_OutlineWidth*2.0;
                cs.xy+=po*cs.w/_ScaledScreenParams.xy;
                o.positionCS=cs; o.uv=TRANSFORM_TEX(i.uv,_BaseMap); return o;
            }
            half4 OFrag(V i):SV_Target {
                half a=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).a*_BaseColor.a;
                clip(a-_Cutoff); return _OutlineColor;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ZTest LEqual
            ColorMask R
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DVert
            #pragma fragment DFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST; half4 _BaseColor,_ShadeColor,_AmbientColor,_OutlineColor;
            half _ShadowThreshold,_BandSoftness,_OutlineWidth,_Cutoff,_Cull;
            CBUFFER_END

            V DVert(A i) {
                V o=(V)0; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i,o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS=TransformObjectToHClip(i.positionOS.xyz);
                o.uv=TRANSFORM_TEX(i.uv,_BaseMap); o.color=i.color; return o;
            }
            half DFrag(V i):SV_Target {
                half a=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).a*_BaseColor.a*i.color.a;
                clip(a-_Cutoff); return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
