// Snow mesh for TerrainWeather (Snow). A real 3D surface: lit by the sun (with its shadows), ambient and point /
// spot lights, casts shadows and writes depth, so characters standing in it have their feet in the snow.
//   - Footprints / furrows from the weather interaction map push the vertices down (up to the snow's own depth
//     × Trail Depth) and bend the normals, so prints are real dents with a lit and a shaded wall.
//   - Sunlit parts take Snow Color, the shaded side and shadows take Shadow Color. Scene Light sets how much the
//     scene's own light (sun colour, sky ambient) tints it on top (0 = exactly your two colours).
//   - Clear On Touch sweeps the snow down to the ground along a character's path (runtime mask).
//   - Optional light steps (painted pixel-art tones) without any dithering, and tiny sun glints on the pixel grid.
// uv1.x = snow thickness at the vertex (so prints never go below the ground).
Shader "Hidden/RythmRPG/TerrainSnow"
{
    Properties
    {
        _SnowColor ("Snow", Color) = (0.95, 0.97, 1, 1)
        _SnowShadowTint ("Shadow Color", Color) = (0.8, 0.84, 0.93, 1)
        _SnowParams ("Bands, Sparkle, Trail Depth, Pixels Per Unit", Vector) = (4, 0.35, 0.8, 32)
        _SnowLight ("Scene Light", Vector) = (0.35, 0, 0, 0)
        _ClearParams ("Clear Mask Origin XZ, 1 / Size XZ", Vector) = (0, 0, 0, 0)
        [NoScaleOffset] _ClearTex ("Clear Mask", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _SnowColor;
            half4 _SnowShadowTint;
            float4 _SnowParams;
            float4 _SnowLight;
            float4 _ClearParams;
        CBUFFER_END

        TEXTURE2D(_ClearTex);
        TEXTURE2D(_WeatherMapTex);
        float4 _WeatherMapParams; // origin x, origin z, 1 / size, on

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv1 : TEXCOORD1;
        };

        float PackedSnow(float2 xz)
        {
            if (_WeatherMapParams.w < 0.5) return 0;
            float2 uv = (xz - _WeatherMapParams.xy) * _WeatherMapParams.z;
            if (any(uv < 0.0) || any(uv > 1.0)) return 0;
            return SAMPLE_TEXTURE2D_LOD(_WeatherMapTex, sampler_LinearClamp, uv, 0).r;
        }

        // Swept clear (Clear On Touch), 0..1.
        float Cleared(float2 xz)
        {
            if (_ClearParams.z <= 0.0) return 0;
            float2 uv = (xz - _ClearParams.xy) * _ClearParams.zw;
            if (any(uv < 0.0) || any(uv > 1.0)) return 0;
            return SAMPLE_TEXTURE2D_LOD(_ClearTex, sampler_LinearClamp, uv, 0).r;
        }

        // How far the snow is pressed down here (0 = untouched, 1 = down to the ground).
        float Press(float2 xz)
        {
            return saturate(PackedSnow(xz) * _SnowParams.z + Cleared(xz));
        }

        // World position with footprints pressed in and swept paths cleared.
        float3 SnowPosition(Attributes input)
        {
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            positionWS.y -= Press(positionWS.xz) * input.uv1.x;
            return positionWS;
        }

        // Normal bent by the prints (their walls face in toward the print).
        float3 SnowNormal(float3 baseNormalWS, float3 positionWS, float thickness)
        {
            float e = 1.0 / max(_SnowParams.w, 1.0);
            float dx = Press(positionWS.xz + float2(e, 0)) - Press(positionWS.xz - float2(e, 0));
            float dz = Press(positionWS.xz + float2(0, e)) - Press(positionWS.xz - float2(0, e));
            float press = thickness / (2.0 * e);
            return normalize(baseNormalWS + float3(dx * press, 0, dz * press));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 thicknessFog : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = SnowPosition(input);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.thicknessFog = float2(input.uv1.x, ComputeFogFactor(o.positionCS.z));
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half Band(half value)
            {
                float bands = _SnowParams.x;
                return bands >= 1.0 ? floor(value * bands + 0.5) / bands : value;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = SnowNormal(normalize(input.normalWS), input.positionWS, input.thicknessFog.x);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                half3 ambient = SampleSH(normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half sun = saturate(dot(normalWS, mainLight.direction)) * mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half sunBand = Band(sun);

                // Your two colours decide the look: Snow Color in the sun, Shadow Color in the shade.
                half3 color = lerp(_SnowShadowTint.rgb, _SnowColor.rgb, sunBand);
                // Scene light on top, as much as Scene Light asks (normalised so a bright day stays about 1).
                half3 sceneLight = ambient + mainLight.color * sunBand;
                half sceneMax = max(max(sceneLight.r, sceneLight.g), max(sceneLight.b, 1e-3h));
                sceneLight /= max(sceneMax, 1.0h);
                color *= lerp(half3(1, 1, 1), sceneLight, saturate(_SnowLight.x));

                // Lamps and other lights add their light on top.
                half3 light = 0;

                #if defined(_ADDITIONAL_LIGHTS)
                half4 shadowMask = half4(1, 1, 1, 1);
                uint pixelLightCount = GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint dirIndex = 0; dirIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); dirIndex++)
                {
                    Light dirLight = GetAdditionalLight(dirIndex, input.positionWS, shadowMask);
                    half dirTerm = saturate(dot(normalWS, dirLight.direction)) * dirLight.shadowAttenuation * dirLight.distanceAttenuation;
                    light += dirLight.color * Band(dirTerm);
                }
                #endif
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light l = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);
                    half term = saturate(dot(normalWS, l.direction) * 0.8h + 0.2h) * l.shadowAttenuation * l.distanceAttenuation;
                    light += l.color * Band(term);
                LIGHT_LOOP_END
                #endif

                color += _SnowColor.rgb * light;

                // Sun glints: single pixels on the world pixel grid that twinkle on and off.
                float ppu = max(_SnowParams.w, 1.0);
                float2 cell = floor(input.positionWS.xz * ppu);
                float glint = Hash21(cell + floor(_Time.y * 3.0 + Hash21(cell) * 7.0) * 3.17);
                if (_SnowParams.y > 0.0 && sun > 0.6 && glint > 1.0 - _SnowParams.y * 0.04)
                    color = max(color, mainLight.color * 1.2);

                color = MixFog(color, input.thicknessFog.y);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            float4 Vert(Attributes input) : SV_POSITION
            {
                float3 positionWS = SnowPosition(input);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
            }

            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Vert(Attributes input) : SV_POSITION
            {
                return TransformWorldToHClip(SnowPosition(input));
            }

            half Frag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #if defined(_GBUFFER_NORMALS_OCT)
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            #endif

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float thickness : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = SnowPosition(input);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.thickness = input.uv1.x;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = SnowNormal(normalize(input.normalWS), input.positionWS, input.thickness);
                #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                return half4(PackFloat2To888(remappedOctNormalWS), 0.0);
                #else
                return half4(NormalizeNormalPerPixel(normalWS), 0.0);
                #endif
            }
            ENDHLSL
        }
    }
    Fallback Off
}
