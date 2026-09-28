// Terrain Weather, step 1: every puff of one TerrainWeather adds its soft round "field" into one texture at the
// pixel camera's resolution (additive, so neighbouring puffs merge like metaballs). Depth-tested against the scene,
// so walls, trees and characters in front hide the puffs behind them.
//   R = density (sum of the puff kernels)
//   G, B = height inside the strongest nearby puff (0 at its bottom, 1 at its top; snow: 1 at the middle of the
//          drift), stored as G = sum(k^4 * height), B = sum(k^4), so each lobe keeps its own shading.
// Pass 0 "Billboard": upright puffs facing the camera (mist, fog), with drift, wobble, breathing and parting.
// Pass 1 "Flat": puffs lying on the ground (snow), with footprints / furrows cut out of them.
Shader "Hidden/RythmRPG/TerrainWeatherField"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off
        ColorMask RGB

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        StructuredBuffer<float4> _Puffs;   // [2n] = position xyz, radius; [2n+1] = x seed
        float4 _TwShape;     // x = width, y = height (billboard puffs, × radius), z = snow lift
        float4 _TwMotion;    // x = wobble, y = wobble speed, z = breathe, w = time
        float4 _TwDrift;     // xy = drift direction XZ, z = drift speed, w = drift range
        float4 _TwInteract;  // x = push, y = clear, z = trail depth
        TEXTURE2D(_WeatherMapTex);
        float4 _WeatherMapParams; // origin x, origin z, 1 / size, on

        struct Attributes
        {
            float4 positionOS : POSITION;   // quad corners in -1..1
            uint instanceID : SV_InstanceID;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;          // -1..1 inside the puff
            float3 positionWS : TEXCOORD1;
        };

        float4 SampleMap(float2 xz)
        {
            if (_WeatherMapParams.w < 0.5) return 0;
            float2 uv = (xz - _WeatherMapParams.xy) * _WeatherMapParams.z;
            if (any(uv < 0.0) || any(uv > 1.0)) return 0;
            return SAMPLE_TEXTURE2D_LOD(_WeatherMapTex, sampler_LinearClamp, uv, 0);
        }

        // Drift loop: puffs travel along the drift direction within the range and shrink away at the wrap.
        void Drift(float seed, float time, out float3 offset, out float fade)
        {
            offset = 0;
            fade = 1;
            float speed = _TwDrift.z;
            float range = max(_TwDrift.w, 0.1);
            if (speed <= 0.0001) return;
            float phase = frac(time * speed / range + seed * 7.13);
            offset = float3(_TwDrift.x, 0, _TwDrift.y) * (phase - 0.5) * range;
            fade = saturate(sin(phase * 3.14159265) * 3.0);
        }
        ENDHLSL

        Pass
        {
            Name "Billboard"

            HLSLPROGRAM
            #pragma vertex VertBillboard
            #pragma fragment FragBillboard

            Varyings VertBillboard(Attributes input)
            {
                float4 a = _Puffs[input.instanceID * 2];
                float4 b = _Puffs[input.instanceID * 2 + 1];
                float seed = b.x;
                float time = _TwMotion.w;
                float radius = a.w;

                float3 drift;
                float fade;
                Drift(seed, time, drift, fade);
                float w = _TwMotion.y;
                float3 wobble = _TwMotion.x * float3(sin(time * w + seed * 6.28318),
                                                     0.35 * sin(time * w * 1.31 + seed * 11.0),
                                                     cos(time * w * 0.83 + seed * 4.0));
                radius *= (1.0 + _TwMotion.z * sin(time * w * 1.7 + seed * 9.0)) * fade;

                float3 home = a.xyz + drift;
                // Characters push the puff aside and thin it (the map flows back by itself).
                float4 map = SampleMap(home.xz);
                float3 center = home + wobble + float3(map.g, 0, map.b) * _TwInteract.x;
                radius *= 1.0 - saturate(map.a * _TwInteract.y);

                // Upright, facing the camera's yaw (like the grass and sprites under the oblique projection).
                float3 right = float3(UNITY_MATRIX_V[0].x, 0, UNITY_MATRIX_V[0].z);
                right = dot(right, right) > 1e-6 ? normalize(right) : float3(1, 0, 0);
                float2 corner = input.positionOS.xy;
                float3 world = center + right * corner.x * radius * _TwShape.x + float3(0, 1, 0) * corner.y * radius * _TwShape.y;

                Varyings o;
                o.positionWS = world;
                o.positionCS = TransformWorldToHClip(world);
                o.uv = corner;
                return o;
            }

            float4 FragBillboard(Varyings input) : SV_Target
            {
                float q = dot(input.uv, input.uv);
                clip(1.0 - q);
                float k = (1.0 - q) * (1.0 - q);
                float w = k * k * k * k;
                return float4(k, w * (input.uv.y * 0.5 + 0.5), w, 0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Flat"

            HLSLPROGRAM
            #pragma vertex VertFlat
            #pragma fragment FragFlat

            Varyings VertFlat(Attributes input)
            {
                float4 a = _Puffs[input.instanceID * 2];
                float radius = a.w;
                float2 corner = input.positionOS.xy;
                float3 world = a.xyz + float3(corner.x * radius, _TwShape.z, corner.y * radius);
                Varyings o;
                o.positionWS = world;
                o.positionCS = TransformWorldToHClip(world);
                o.uv = corner;
                return o;
            }

            float4 FragFlat(Varyings input) : SV_Target
            {
                float q = dot(input.uv, input.uv);
                clip(1.0 - q);
                float k = (1.0 - q) * (1.0 - q);
                // Footprints / furrows cut the drift away (the prints refill on their own).
                float packed = SampleMap(input.positionWS.xz).r;
                k *= 1.0 - saturate(packed * _TwInteract.z);
                float w = k * k * k * k;
                return float4(k, w * (1.0 - sqrt(q)), w, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
