// Full-screen passes for WeatherRendererFeature (sky weather from the WeatherController):
//   0 "Tint"  multiply the view by the weather tint (storm gloom); off when the tint's alpha is 0.
//   1 "Flash" add the lightning flash.
//   2 "Heat"  heat shimmer: re-reads the frame with whole-pixel offsets (global heat, strongest near the ground,
//             plus Heat Haze volumes). Offsets come from rising noise anchored to the world, so it wobbles with the
//             scene, not the screen.
Shader "Hidden/RythmRPG/WeatherScreen"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off
        ZTest Always
        Cull Off

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        #define MAX_HAZES 8

        float4 _WeatherWind;      // wind x, wind z, speed, time (global, set by WeatherController)
        float4 _WxGround;         // x = ground height
        float4 _WxTint;           // rgb = tint, a = amount
        float4 _WxFlash;          // rgb = flash colour * brightness
        float4 _WxHeatParams;     // x = global heat, y = max offset px, z = 1 / scale, w = speed
        float4 _WxHeatBand;       // x = ground band height
        float4 _WxHazeRow0[MAX_HAZES];
        float4 _WxHazeRow1[MAX_HAZES];
        float4 _WxHazeRow2[MAX_HAZES];
        float4 _WxHazeParams[MAX_HAZES];  // x = shape (0 box / 1 sphere), y = strength, z = edge softness, w = fade upward
        int _WxHazeCount;

        float WxHash(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float WxNoise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(WxHash(i), WxHash(i + float2(1, 0)), f.x),
                        lerp(WxHash(i + float2(0, 1)), WxHash(i + float2(1, 1)), f.x), f.y);
        }

        float3 WxWorld(float2 uv, float rawDepth)
        {
            #if UNITY_REVERSED_Z
                float depth = rawDepth;
            #else
                float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
            #endif
            return ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
        }

        float3 WxRayDir(float2 uv)
        {
            #if UNITY_REVERSED_Z
                float nearDepth = 1.0, farDepth = 0.0;
            #else
                float nearDepth = UNITY_NEAR_CLIP_VALUE, farDepth = 1.0;
            #endif
            float3 a = ComputeWorldSpacePosition(uv, nearDepth, UNITY_MATRIX_I_VP);
            float3 b = ComputeWorldSpacePosition(uv, farDepth, UNITY_MATRIX_I_VP);
            return normalize(b - a);
        }

        // Ray (P - dir * t, t >= 0) against a unit box / sphere (radius 0.5) in the volume's local space.
        float2 WxVolumeSpan(float3 p, float3 dir, float4 r0, float4 r1, float4 r2, float shape, out float3 localP, out float3 localD)
        {
            localP = float3(dot(r0.xyz, p) + r0.w, dot(r1.xyz, p) + r1.w, dot(r2.xyz, p) + r2.w);
            localD = -float3(dot(r0.xyz, dir), dot(r1.xyz, dir), dot(r2.xyz, dir));
            if (shape < 0.5)
            {
                float3 inv = 1.0 / (abs(localD) > 1e-6 ? localD : 1e-6);
                float3 t0 = (-0.5 - localP) * inv;
                float3 t1 = (0.5 - localP) * inv;
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);
                return float2(max(max(tmin.x, tmin.y), max(tmin.z, 0.0)), min(tmax.x, min(tmax.y, tmax.z)));
            }
            float a = dot(localD, localD);
            float b = dot(localP, localD);
            float c = dot(localP, localP) - 0.25;
            float disc = b * b - a * c;
            if (disc <= 0.0) return float2(1, 0);
            float s = sqrt(disc);
            return float2(max((-b - s) / a, 0.0), (-b + s) / a);
        }

        float WxVolumeFade(float3 local, float shape, float softness)
        {
            float edge;
            if (shape < 0.5)
            {
                float3 q = abs(local) * 2.0;
                edge = 1.0 - max(q.x, max(q.y, q.z));
            }
            else
            {
                edge = 1.0 - length(local) * 2.0;
            }
            return saturate(edge / max(softness, 0.001));
        }
        ENDHLSL

        Pass
        {
            Name "Tint"
            Blend DstColor Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragTint

            half4 FragTint(Varyings input) : SV_Target
            {
                return half4(lerp(half3(1, 1, 1), _WxTint.rgb, _WxTint.a), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Flash"
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragFlash

            half4 FragFlash(Varyings input) : SV_Target
            {
                return half4(_WxFlash.rgb, 0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Heat"
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragHeat

            half4 FragHeat(Varyings input) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                int2 pixel = int2(input.positionCS.xy);
                int2 size = int2(_ScaledScreenParams.xy);
                float3 dir = WxRayDir(uv);
                float raw = SampleSceneDepth(uv);
                float3 p = WxWorld(uv, raw);

                // Global heat: strongest near the ground.
                float strength = _WxHeatParams.x * (1.0 - saturate((p.y - _WxGround.x) / max(_WxHeatBand.x, 0.01)));

                // Heat hazes: how much hot air the view ray crosses.
                for (int h = 0; h < _WxHazeCount; h++)
                {
                    float4 prm = _WxHazeParams[h];
                    float3 lp, ld;
                    float2 span = WxVolumeSpan(p, dir, _WxHazeRow0[h], _WxHazeRow1[h], _WxHazeRow2[h], prm.x, lp, ld);
                    if (span.y <= span.x) continue;
                    float tm = (span.x + span.y) * 0.5;
                    float3 localMid = lp + ld * tm;
                    float fade = WxVolumeFade(localMid, prm.x, prm.z);
                    if (prm.w > 0.5) fade *= saturate(0.5 - localMid.y);
                    strength += prm.y * fade * saturate((span.y - span.x) * 1.5);
                }

                if (strength <= 0.001)
                    return LOAD_TEXTURE2D_X(_BlitTexture, clamp(pixel, int2(0, 0), size - 1));

                float time = _WeatherWind.w * _WxHeatParams.w;
                float2 q = float2(p.x, p.y + p.z * 0.5) * _WxHeatParams.z;
                float wobble = WxNoise(float2(q.x * 1.3, q.y * 2.0 - time)) - 0.5;
                float lift = WxNoise(float2(q.x * 2.1 + 5.0, q.y * 1.4 - time * 0.7)) - 0.5;
                float maxOffset = _WxHeatParams.y * saturate(strength);
                int2 offset = int2(round(wobble * 2.0 * maxOffset), round(lift * 0.8 * maxOffset));
                return LOAD_TEXTURE2D_X(_BlitTexture, clamp(pixel + offset, int2(0, 0), size - 1));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
