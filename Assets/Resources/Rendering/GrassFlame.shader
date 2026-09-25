// Flipbook flames for burning grass (GrassField "Flame Frames"). One procedural draw for every tuft in flames:
// each is an upright quad facing the camera's yaw (1:1 under the ObliqueProjection), standing on the burning
// edge, playing the assigned sprite frames from its own start frame. Point-sampled and alpha-clipped so the
// pixel art stays crisp; drawn after opaques with alpha blending for soft edges if the sprites have them.
Shader "Hidden/RythmRPG/GrassFlame"
{
    Properties
    {
        _BaseMap ("Flame Sprite Sheet", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "GrassFlame"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define MAX_FLAME_FRAMES 32

            struct GrassFlame
            {
                float4 positionSeed; // xyz = where the flame stands (world), w = random 0..1
                float4 data;         // x = size (0..1+), y = brightness
            };

            StructuredBuffer<GrassFlame> _GrassFlames;

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            float4 _FlameFrames[MAX_FLAME_FRAMES]; // uv min (xy) and size (zw) of every frame
            float4 _FlameInfo;   // x = frame count, y = frames per second, z = alpha cutoff, w = pixels per unit (0 = no snap)
            float4 _FlameSize;   // xy = frame size in world units, zw = pivot (0..1)
            float4 _FlameTint;   // HDR tint
            float4 _GrassRight;  // xyz = camera right, flattened
            float4 _GrassTime;   // x = game time

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
                float brightness : TEXCOORD1;
            };

            float Snap(float value)
            {
                return _FlameInfo.w > 0.0 ? round(value * _FlameInfo.w) / _FlameInfo.w : value;
            }

            Varyings Vert(Attributes input)
            {
                GrassFlame flame = _GrassFlames[input.instanceID];
                float2 corner = input.uv;
                float count = max(_FlameInfo.x, 1.0);
                float frame = fmod(floor(_GrassTime.x * _FlameInfo.y + flame.positionSeed.w * count), count);
                float4 rect = _FlameFrames[(int)frame];
                bool flip = frac(flame.positionSeed.w * 7.31) > 0.5;

                float2 local = (corner - _FlameSize.zw) * _FlameSize.xy * flame.data.x;
                local.x *= flip ? -1.0 : 1.0;
                float3 root = flame.positionSeed.xyz;
                root = float3(Snap(root.x), Snap(root.y), Snap(root.z));
                float3 positionWS = root + _GrassRight.xyz * Snap(local.x) + float3(0, Snap(local.y), 0);

                Varyings output;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = rect.xy + corner * rect.zw;
                output.brightness = flame.data.y;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                clip(tex.a - _FlameInfo.z);
                half4 color = tex * (half4)_FlameTint;
                color.rgb *= (half)input.brightness;
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
