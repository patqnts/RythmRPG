// GPU grass for GrassField.cs. One procedural draw per (chunk, sprite variant); every blade is read from a
// StructuredBuffer by SV_InstanceID, so there are no GameObjects, no per-blade CPU work and no matrices.
// Blades are upright quads facing the camera's yaw (1:1 under the ObliqueProjection), bent by wind and by the
// GrassInteractionMap (trampling), optionally in whole-pixel steps for crisp pixel art.
Shader "Hidden/RythmRPG/PixelGrass"
{
    Properties
    {
        _BaseMap ("Sprite Texture", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    struct GrassBlade
    {
        float4 positionScale; // xyz = root (world), w = scale
        float4 data;          // x = flip (+1 / -1), y = shade, z = random 0..1, w = unused
    };

    StructuredBuffer<GrassBlade> _GrassBlades;
    float _GrassInstanceOffset; // first blade of this draw in _GrassBlades

    TEXTURE2D(_BaseMap);
    SAMPLER(sampler_BaseMap);

    TEXTURE2D(_GrassInteractionTex);
    SAMPLER(sampler_GrassInteractionTex);
    float4 _GrassInteractionParams; // xy = map corner XZ, z = 1 / size, w = 1 when active

    // Per draw (MaterialPropertyBlock): the variant's sprite.
    float4 _SpriteUV;     // xy = uv min, zw = uv size
    float4 _SpriteSize;   // xy = size in world units, zw = pivot (0..1)

    // Per field (material).
    float _Cutoff;
    float4 _GrassRight;       // xyz = world direction blades are wide along (camera right, flattened)
    float4 _Wind;             // xy = direction XZ (normalised), z = strength (units), w = speed
    float4 _WindShape;        // x = wave frequency, y = gust strength, z = gust scale, w = bend height (units)
    float4 _Push;             // x = push bend (units), y = flatten (0..1), z = trample darken, w = pixels per unit
    float4 _PixelOptions;     // x = snap bend to pixels (0/1)
    float4 _ColorA;
    float4 _ColorB;
    float4 _Patch;            // x = patch scale, y = shade variation, z = normal up blend, w = light bands (0 = smooth)

    float GrassHash(float2 p)
    {
        p = frac(p * float2(123.34, 456.21));
        p += dot(p, p + 45.32);
        return frac(p.x * p.y);
    }

    float GrassNoise(float2 p)
    {
        float2 i = floor(p);
        float2 f = frac(p);
        float2 u = f * f * (3.0 - 2.0 * f);
        float a = GrassHash(i);
        float b = GrassHash(i + float2(1, 0));
        float c = GrassHash(i + float2(0, 1));
        float d = GrassHash(i + float2(1, 1));
        return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
    }

    float SnapToPixels(float value)
    {
        return _PixelOptions.x > 0.5 ? round(value * _Push.w) / _Push.w : value;
    }

    struct GrassVertex
    {
        float3 positionWS;
        float2 uv;
        float shade;
        float3 rootWS;
    };

    GrassVertex BuildGrassVertex(float2 corner, uint instanceID)
    {
        GrassBlade blade = _GrassBlades[(uint)_GrassInstanceOffset + instanceID];
        float3 root = blade.positionScale.xyz;
        float scale = blade.positionScale.w;

        // Quad corner in the sprite's own frame (x across, y up), pivot at the root.
        float2 local = (corner - _SpriteSize.zw) * _SpriteSize.xy * scale;
        local.x *= blade.data.x;
        float3 right = _GrassRight.xyz;
        float heightAbove = max(local.y, 0.0);

        // 0 at the root, 1 at the bend height: the tip moves, the root stays planted.
        float bendHeight = max(_WindShape.w * scale, 0.0001);
        float t = saturate(heightAbove / bendHeight);
        float weight = t * t;

        // Interaction map (trampling).
        float2 push = 0;
        float flatten = 0;
        if (_GrassInteractionParams.w > 0.5)
        {
            float2 mapUV = (root.xz - _GrassInteractionParams.xy) * _GrassInteractionParams.z;
            if (all(mapUV >= 0.0) && all(mapUV <= 1.0))
            {
                float4 m = SAMPLE_TEXTURE2D_LOD(_GrassInteractionTex, sampler_GrassInteractionTex, mapUV, 0);
                push = m.xy;
                flatten = saturate(m.z);
            }
        }

        // Wind: a lean downwind that breathes with travelling waves and slow gusts.
        float2 windDir = _Wind.xy;
        float phase = dot(root.xz, windDir) * _WindShape.x - _Time.y * _Wind.w + blade.data.z * 1.7;
        float wave = sin(phase) * 0.6 + sin(phase * 2.31 + 1.3) * 0.4;
        float gust = GrassNoise(root.xz * _WindShape.z - windDir * (_Time.y * _Wind.w * 0.35));
        float windAmount = _Wind.z * (0.35 + 0.65 * lerp(1.0, gust, _WindShape.y)) * (0.55 + 0.45 * wave);
        float2 bend = windDir * windAmount * (1.0 - flatten) + push * _Push.x;

        float2 offsetXZ = bend * weight * scale;
        float squash = 1.0 - flatten * _Push.y;
        float yOffset = heightAbove * (squash - 1.0) - dot(offsetXZ, offsetXZ) * 0.35 / max(bendHeight, 0.0001);

        offsetXZ = float2(SnapToPixels(offsetXZ.x), SnapToPixels(offsetXZ.y));
        yOffset = SnapToPixels(yOffset);

        GrassVertex v;
        v.rootWS = root;
        v.positionWS = root + right * local.x + float3(0, local.y, 0) + float3(offsetXZ.x, yOffset, offsetXZ.y);
        v.uv = _SpriteUV.xy + corner * _SpriteUV.zw;
        v.shade = blade.data.y * lerp(1.0, _Push.z, flatten);
        return v;
    }

    half3 GrassNormal()
    {
        // Blend between "lit like the ground" and "lit like an upright card facing the camera".
        float3 facing = normalize(cross(_GrassRight.xyz, float3(0, 1, 0)));
        return (half3)normalize(lerp(-facing, float3(0, 1, 0), _Patch.z));
    }

    half4 SampleGrass(float2 uv, float3 rootWS, float shade)
    {
        half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
        clip(tex.a - _Cutoff);
        float patch = GrassNoise(rootWS.xz * _Patch.x);
        half3 tint = (half3)lerp(_ColorA.rgb, _ColorB.rgb, patch);
        return half4(tex.rgb * tint * (half)shade, tex.a);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 rootWS : TEXCOORD2;
                float2 shadeFog : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                GrassVertex g = BuildGrassVertex(input.uv, input.instanceID);
                Varyings output;
                output.positionWS = g.positionWS;
                output.positionCS = TransformWorldToHClip(g.positionWS);
                output.uv = g.uv;
                output.rootWS = g.rootWS;
                output.shadeFog = float2(g.shade, ComputeFogFactor(output.positionCS.z));
                return output;
            }

            half Band(half value)
            {
                return _Patch.w > 0.5 ? floor(value * _Patch.w + 0.5) / _Patch.w : value;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 albedo = SampleGrass(input.uv, input.rootWS, input.shadeFog.x);
                half3 normalWS = GrassNormal();

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                half3 color = albedo.rgb * SampleSH(normalWS);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half mainTerm = saturate(dot(normalWS, mainLight.direction) * 0.5h + 0.5h)
                                * mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                color += albedo.rgb * mainLight.color * Band(mainTerm);

                #if defined(_ADDITIONAL_LIGHTS)
                half4 shadowMask = half4(1, 1, 1, 1);
                uint pixelLightCount = GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint dirIndex = 0; dirIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); dirIndex++)
                {
                    Light dirLight = GetAdditionalLight(dirIndex, input.positionWS, shadowMask);
                    half dirTerm = saturate(dot(normalWS, dirLight.direction) * 0.5h + 0.5h)
                                   * dirLight.shadowAttenuation * dirLight.distanceAttenuation;
                    color += albedo.rgb * dirLight.color * Band(dirTerm);
                }
                #endif
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);
                    half term = saturate(dot(normalWS, light.direction) * 0.5h + 0.5h)
                                * light.shadowAttenuation * light.distanceAttenuation;
                    color += albedo.rgb * light.color * Band(term);
                LIGHT_LOOP_END
                #endif

                color = MixFog(color, input.shadeFog.y);
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

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                GrassVertex g = BuildGrassVertex(input.uv, input.instanceID);
                float3 normalWS = (float3)GrassNormal();
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - g.positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                Varyings output;
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(g.positionWS, normalWS, lightDirectionWS)));
                output.uv = g.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                GrassVertex g = BuildGrassVertex(input.uv, input.instanceID);
                Varyings output;
                output.positionCS = TransformWorldToHClip(g.positionWS);
                output.uv = g.uv;
                return output;
            }

            half Frag(Varyings input) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - _Cutoff);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #if defined(_GBUFFER_NORMALS_OCT)
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                GrassVertex g = BuildGrassVertex(input.uv, input.instanceID);
                Varyings output;
                output.positionCS = TransformWorldToHClip(g.positionWS);
                output.uv = g.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - _Cutoff);
                float3 normalWS = (float3)GrassNormal();
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
