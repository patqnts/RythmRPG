// Updates the grass interaction map (see GrassInteractionMap.cs). Blit from the previous map into the next one:
// scroll with the view, decay (grass springs back), then stamp every GrassInteractor and shockwave ring.
// RG = push direction * amount (world XZ, signed), B = flattened amount, A unused.
Shader "Hidden/RythmRPG/GrassInteraction"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "GrassInteractionUpdate"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define MAX_INTERACTORS 16
            #define MAX_SHOCKWAVES 8

            TEXTURE2D(_PrevTex);
            SAMPLER(sampler_PrevTex);

            float4 _MapParams;      // xy = world XZ of the map's corner, z = world size, w = 0 on reset
            float4 _Decay;          // x = push decay, y = flatten decay, zw = uv shift from the previous map
            float4 _Interactors[MAX_INTERACTORS];          // xy = world XZ, z = radius, w = strength
            float4 _InteractorVelocities[MAX_INTERACTORS]; // xy = world XZ velocity
            float _InteractorCount;
            float4 _Shockwaves[MAX_SHOCKWAVES];     // xy = centre XZ, z = radius now, w = strength now
            float4 _ShockwaveShape[MAX_SHOCKWAVES]; // x = front width, y = flatten, z = 0 ring / 1 cone / 2 line
            float4 _ShockwaveDir[MAX_SHOCKWAVES];   // xy = direction; cone: zw = cos(half angle), cos(outer edge); line: z = half length, w = soft edge
            float _ShockwaveCount;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;

                // Previous frame, scrolled so each texel keeps the same world spot.
                float4 state = 0;
                float2 prevUV = uv + _Decay.zw;
                if (_MapParams.w > 0.5 && all(prevUV >= 0.0) && all(prevUV <= 1.0))
                    state = SAMPLE_TEXTURE2D_LOD(_PrevTex, sampler_PrevTex, prevUV, 0);

                // Spring back.
                state.xy *= _Decay.x;
                state.z *= _Decay.y;

                float2 world = _MapParams.xy + uv * _MapParams.z;
                int count = (int)_InteractorCount;
                [loop] for (int i = 0; i < count; i++)
                {
                    float4 it = _Interactors[i];
                    float2 d = world - it.xy;
                    float dist = length(d);
                    float k = saturate(1.0 - dist / max(it.z, 0.0001));
                    k = k * k * (3.0 - 2.0 * k) * it.w;
                    if (k <= 0.0) continue;

                    // Away from the centre, leaning with the direction of travel.
                    float2 dir = dist > 0.0001 ? d / dist : float2(0, 0);
                    float2 velocity = _InteractorVelocities[i].xy;
                    float speed = length(velocity);
                    if (speed > 0.01)
                        dir = normalize(dir + (velocity / speed) * saturate(speed * 0.25) * 0.6 + 0.0001);

                    // Strongest push wins, so standing still keeps the grass down without it piling up.
                    if (k > length(state.xy)) state.xy = dir * k;
                    state.z = max(state.z, k);
                }

                // Shockwaves (ring, cone or line) pushing outward; behind them the grass springs back with the normal decay.
                int waveCount = (int)_ShockwaveCount;
                [loop] for (int w = 0; w < waveCount; w++)
                {
                    float4 wave = _Shockwaves[w];
                    float4 shape = _ShockwaveShape[w];
                    float4 direction = _ShockwaveDir[w];
                    float2 d = world - wave.xy;
                    float dist = length(d);
                    float front = dist;
                    float2 dir = dist > 0.0001 ? d / dist : float2(0, 0);
                    float side = 1.0;
                    if (shape.z > 1.5)
                    {
                        // Line: a straight wall moving along the direction, soft at its two ends.
                        front = dot(d, direction.xy);
                        dir = direction.xy;
                        float lateral = abs(d.x * direction.y - d.y * direction.x);
                        side = saturate((direction.z - lateral) / max(direction.w, 0.0001) + 0.5);
                    }
                    else if (shape.z > 0.5)
                    {
                        // Cone: only the slice of the ring around the direction, with soft edges.
                        side = saturate((dot(dir, direction.xy) - direction.w) / max(direction.z - direction.w, 0.0001));
                    }
                    float k = saturate(1.0 - abs(front - wave.z) / max(shape.x, 0.0001));
                    k = k * k * (3.0 - 2.0 * k) * wave.w * side;
                    if (k <= 0.0) continue;
                    if (k > length(state.xy)) state.xy = dir * k;
                    state.z = max(state.z, saturate(k) * shape.y);
                }
                return state;
            }
            ENDHLSL
        }
    }
}
