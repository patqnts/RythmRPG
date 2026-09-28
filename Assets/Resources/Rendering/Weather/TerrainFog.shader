// Mist / fog smoke for TerrainWeather. Every puff is a soft 3D smoke volume (an ellipsoid), drawn on one quad that
// faces the view. Per pixel the view ray is traced through the ellipsoid and cut where it meets the scene (depth
// texture), so:
//   - things in front of the smoke are not covered at all,
//   - things standing inside it are only covered by the smoke in front of them (their feet more than their heads
//     when Ground Hug is up), which is what gives it volume,
//   - where the smoke meets the ground / walls it fades out softly over Soft Distance (like the God Rays).
// The density is swirling 3D noise, thicker at the bottom, lit from above. Smooth: no bands, no dithering.
// Optional dust motes: single render-texture pixels drifting in the smoke.
Shader "Hidden/RythmRPG/TerrainFog"
{
    Properties
    {
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 5
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 10
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "TerrainFog"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest Always   // the ray is cut by the scene depth in the shader (so smoke in front of a wall still shows)
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            StructuredBuffer<float4> _Puffs;  // [2n] = position xyz, radius; [2n+1] = x seed
            float4 _FogShape;      // x = width (× radius), y = height (× radius), z = ground hug, w = soft distance
            float4 _FogDensity;    // x = density, y = max opacity, z = glow (1) / smoke (0)
            float4 _FogNoise;      // x = 1 / noise scale, y = wispiness, z = swirl speed
            float4 _FogMotion;     // x = wobble, y = wobble speed, z = breathe, w = time
            float4 _FogDrift;      // xy = drift direction XZ, z = drift speed, w = drift range
            float4 _FogInteract;   // x = push, y = clear
            float4 _FogLightColor;
            float4 _FogShadeColor;
            float4 _FogLight;      // rgb = scene light multiplier
            float4 _FogViewDir;    // xyz = view ray direction of the game camera (oblique)
            float4 _FogMotes;      // x = amount, y = spacing, z = pixels per unit

            TEXTURE2D(_WeatherMapTex);
            float4 _WeatherMapParams; // origin x, origin z, 1 / size, on
            TEXTURE2D(_ClearTex);     // Clear On Touch mask (1 = cleared)
            float4 _ClearParams;      // origin x, origin z, 1 / size x, 1 / size z (0 = none)

            #define FOG_STEPS 8

            struct Attributes
            {
                float4 positionOS : POSITION;   // quad corners in -1..1
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                nointerpolation float4 center : TEXCOORD1;   // xyz = centre, w = seed
                nointerpolation float4 radii : TEXCOORD2;    // xyz = ellipsoid radii, w = fade
                float2 plane : TEXCOORD3;                     // position on the quad in world units (for motes)
            };

            float4 SampleMap(float2 xz)
            {
                if (_WeatherMapParams.w < 0.5) return 0;
                float2 uv = (xz - _WeatherMapParams.xy) * _WeatherMapParams.z;
                if (any(uv < 0.0) || any(uv > 1.0)) return 0;
                return SAMPLE_TEXTURE2D_LOD(_WeatherMapTex, sampler_LinearClamp, uv, 0);
            }

            float Cleared(float2 xz)
            {
                if (_ClearParams.z <= 0.0) return 0;
                float2 uv = (xz - _ClearParams.xy) * _ClearParams.zw;
                if (any(uv < 0.0) || any(uv > 1.0)) return 0;
                return SAMPLE_TEXTURE2D_LOD(_ClearTex, sampler_LinearClamp, uv, 0).r;
            }

            float3 RayDirection(float3 positionWS)
            {
                if (unity_OrthoParams.w > 0.5)
                {
                    // Orthographic (also sheared / oblique): the view-space direction that keeps the same screen
                    // position is the one both the x and y rows of the projection ignore.
                    float3 v = cross(UNITY_MATRIX_P[0].xyz, UNITY_MATRIX_P[1].xyz);
                    if (dot(v, v) < 1e-10) return normalize(_FogViewDir.xyz);
                    v = normalize(v);
                    if (v.z > 0.0) v = -v;   // view space looks down -z
                    return normalize(mul((float3x3)UNITY_MATRIX_I_V, v));
                }
                return normalize(positionWS - _WorldSpaceCameraPos);
            }

            Varyings Vert(Attributes input)
            {
                float4 a = _Puffs[input.instanceID * 2];
                float4 b = _Puffs[input.instanceID * 2 + 1];
                float seed = b.x;
                float time = _FogMotion.w;
                float radius = a.w;

                // Drift loop: travels along the drift direction within the range, shrinking away at the wrap.
                float3 drift = 0;
                float fade = 1;
                if (_FogDrift.z > 0.0001)
                {
                    float range = max(_FogDrift.w, 0.1);
                    float phase = frac(time * _FogDrift.z / range + seed * 7.13);
                    drift = float3(_FogDrift.x, 0, _FogDrift.y) * (phase - 0.5) * range;
                    fade = saturate(sin(phase * 3.14159265) * 3.0);
                }
                float w = _FogMotion.y;
                float3 wobble = _FogMotion.x * float3(sin(time * w + seed * 6.28318),
                                                      0.25 * sin(time * w * 1.31 + seed * 11.0),
                                                      cos(time * w * 0.83 + seed * 4.0));
                radius *= 1.0 + _FogMotion.z * sin(time * w * 1.7 + seed * 9.0);

                float3 home = a.xyz + drift;
                float4 map = SampleMap(home.xz);
                float3 center = home + wobble + float3(map.g, 0, map.b) * _FogInteract.x;
                float3 radii = max(radius, 0.01) * float3(_FogShape.x, _FogShape.y, _FogShape.x);

                // A quad through the centre facing the view ray, big enough to hold the ellipsoid's outline.
                float3 d = RayDirection(center);
                float3 right = cross(float3(0, 1, 0), d);
                right = dot(right, right) > 1e-4 ? normalize(right) : float3(1, 0, 0);
                float3 up = normalize(cross(d, right));
                float extent = max(radii.x, radii.y) * 1.02;
                float2 corner = input.positionOS.xy * extent;
                float3 world = center + right * corner.x + up * corner.y;

                Varyings o;
                o.positionWS = world;
                o.positionCS = TransformWorldToHClip(world);
                o.center = float4(center, seed);
                o.radii = float4(radii, fade);
                o.plane = corner;
                return o;
            }

            float EyeDepth(float rawDepth)
            {
                if (unity_OrthoParams.w > 0.5)
                {
                    #if UNITY_REVERSED_Z
                    rawDepth = 1.0 - rawDepth;
                    #endif
                    return lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
                }
                return LinearEyeDepth(rawDepth, _ZBufferParams);
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash31(i);
                float n100 = Hash31(i + float3(1, 0, 0));
                float n010 = Hash31(i + float3(0, 1, 0));
                float n110 = Hash31(i + float3(1, 1, 0));
                float n001 = Hash31(i + float3(0, 0, 1));
                float n101 = Hash31(i + float3(1, 0, 1));
                float n011 = Hash31(i + float3(0, 1, 1));
                float n111 = Hash31(i + float3(1, 1, 1));
                float x00 = lerp(n000, n100, f.x);
                float x10 = lerp(n010, n110, f.x);
                float x01 = lerp(n001, n101, f.x);
                float x11 = lerp(n011, n111, f.x);
                return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
            }

            // Rolling smoke: two octaves, the second one turning against the first.
            float Smoke(float3 p, float time, float seed)
            {
                float swirl = _FogNoise.z;
                float3 q = p * _FogNoise.x + seed * 17.0;
                q += float3(time * swirl * 0.35, time * swirl * 0.22, -time * swirl * 0.18);
                float n = ValueNoise3(q) * 0.62;
                float3 r = q * 2.13 + float3(-time * swirl * 0.5, time * swirl * 0.4, time * swirl * 0.3);
                n += ValueNoise3(r + n * 1.7) * 0.38;
                return n;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 center = input.center.xyz;
                float seed = input.center.w;
                float3 radii = input.radii.xyz;
                float fade = input.radii.w;
                float time = _FogMotion.w;
                if (fade <= 0.001) discard;

                float3 P = input.positionWS;
                float3 D = RayDirection(P);

                // Ray / ellipsoid (in the ellipsoid's unit-sphere space; t stays in world units along D).
                float3 o = (P - center) / radii;
                float3 d = D / radii;
                float A = dot(d, d);
                float B = dot(o, d);
                float C = dot(o, o) - 1.0;
                float disc = B * B - A * C;
                if (disc <= 0.0) discard;
                float s = sqrt(disc);
                float t0 = (-B - s) / A;
                float t1 = (-B + s) / A;

                // Where the ray hits the scene (relative to this quad pixel).
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneEye = EyeDepth(SampleSceneDepth(screenUV));
                float fragEye = EyeDepth(input.positionCS.z);
                float3 forward = -UNITY_MATRIX_V[2].xyz;
                float tScene = (sceneEye - fragEye) / max(dot(D, forward), 0.05);
                if (unity_OrthoParams.w < 0.5) t0 = max(t0, -length(P - _WorldSpaceCameraPos));

                float tEnd = min(t1, tScene);
                if (tEnd <= t0) discard;

                float soft = max(_FogShape.w, 0.01);
                float hug = _FogShape.z;
                float wisp = _FogNoise.y;
                float dt = (tEnd - t0) / FOG_STEPS;

                float optical = 0;
                float lit = 0;
                [unroll]
                for (int i = 0; i < FOG_STEPS; i++)
                {
                    float t = t0 + (i + 0.5) * dt;
                    float3 pos = P + D * t;
                    float3 local = (pos - center) / radii;
                    float q = dot(local, local);
                    if (q >= 1.0) continue;

                    // Soft round falloff, heavier toward the bottom when hugging the ground.
                    float body = 1.0 - q;
                    body *= body;
                    float height = saturate(local.y * 0.5 + 0.5);
                    body *= lerp(1.0, saturate(1.25 - height * 1.2), hug);

                    // Characters push the smoke along (noise shifted) and clear it (thinner).
                    float4 map = SampleMap(pos.xz);
                    float3 noisePos = pos - float3(map.g, 0, map.b) * _FogInteract.x;
                    float n = Smoke(noisePos, time, seed);
                    float shaped = lerp(1.0, smoothstep(0.28, 0.8, n) * 1.7, wisp);
                    float clear = (1.0 - saturate(map.a * _FogInteract.y)) * (1.0 - Cleared(pos.xz));

                    // Fade out softly as the ray gets close to the ground / wall behind.
                    float nearScene = saturate((tScene - t) / soft);

                    float rho = body * shaped * clear * nearScene;
                    optical += rho * dt;
                    lit += rho * dt * saturate(height * 0.8 + n * 0.45);
                }

                float litAmount = saturate(lit / max(optical, 1e-5));
                optical *= _FogDensity.x * fade;
                float alpha = (1.0 - exp(-optical)) * _FogDensity.y;
                if (alpha <= 0.002) discard;

                // Lit from above: the top of the smoke takes the light colour, the underside the shade colour.
                half3 color = lerp(_FogShadeColor.rgb, _FogLightColor.rgb, litAmount) * _FogLight.rgb;

                // Dust motes: single pixels drifting upward through the thicker part of the smoke.
                if (_FogMotes.x > 0.0)
                {
                    float spacing = max(_FogMotes.y, 0.05);
                    float2 mp = input.plane;
                    mp.y -= time * 0.12;
                    mp.x += sin(time * 0.6 + mp.y * 1.3 + seed * 6.0) * 0.08;
                    float2 cell = floor(mp / spacing);
                    float2 cellLocal = frac(mp / spacing);
                    float pick = Hash21(cell + seed * 37.0);
                    if (pick < _FogMotes.x)
                    {
                        float2 centre = 0.15 + 0.7 * float2(Hash21(cell + 5.3), Hash21(cell + 9.1));
                        float2 offsetPixels = abs(cellLocal - centre) * spacing * max(_FogMotes.z, 1.0);
                        if (max(offsetPixels.x, offsetPixels.y) < 0.5)
                        {
                            float twinkle = 0.55 + 0.45 * sin(time * 2.7 + pick * 60.0);
                            float presence = saturate(alpha * 2.5) * twinkle;
                            color = lerp(color, _FogLightColor.rgb * _FogLight.rgb * 1.25, presence);
                            alpha = max(alpha, presence * 0.9);
                        }
                    }
                }

                if (_FogDensity.z > 0.5) return half4(color * alpha, alpha);  // glow (One / OneMinusSrcColor)
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
