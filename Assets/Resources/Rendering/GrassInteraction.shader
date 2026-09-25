// Updates the grass interaction map (see GrassInteractionMap.cs). Blit from the previous map into the next one:
// scroll with the view, decay (grass springs back), then stamp every GrassInteractor.
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

            TEXTURE2D(_PrevTex);
            SAMPLER(sampler_PrevTex);

            float4 _MapParams;      // xy = world XZ of the map's corner, z = world size, w = 0 on reset
            float4 _Decay;          // x = push decay, y = flatten decay, zw = uv shift from the previous map
            float4 _Interactors[MAX_INTERACTORS];          // xy = world XZ, z = radius, w = strength
            float4 _InteractorVelocities[MAX_INTERACTORS]; // xy = world XZ velocity
            float _InteractorCount;

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
                return state;
            }
            ENDHLSL
        }
    }
}
