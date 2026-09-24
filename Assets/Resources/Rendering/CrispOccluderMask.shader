// Used by CrispWorldUICamera (RythmRPG.Core): draws the silhouette of an occluder (player, enemy, note) into the
// low-resolution occlusion mask, with the pixel camera's view (_CrispOccluderViewProjection) so the mask has exactly
// the pixel render's grid. Output is 1 where the sprite's alpha is above the cutoff, nothing elsewhere.
// Sprites: _MainTex is the sprite texture (bound by the SpriteRenderer); flip and alpha come from globals set per
// draw, because sprite flip/colour are shader-side. Meshes: no _MainTex bound -> white -> the whole mesh covers.
Shader "Hidden/Rythm RPG/Crisp Occluder Mask"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "IgnoreProjector" = "True" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4x4 _CrispOccluderViewProjection;
            float4 _CrispOccluderFlip;
            float _CrispOccluderAlpha;
            float _CrispOccluderCutoff;

            v2f vert(appdata_t v)
            {
                v2f o;
                float3 local = float3(v.vertex.xy * _CrispOccluderFlip.xy, v.vertex.z);
                float4 world = mul(unity_ObjectToWorld, float4(local, 1.0));
                o.vertex = mul(_CrispOccluderViewProjection, world);
                o.texcoord = v.texcoord;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                clip(tex2D(_MainTex, i.texcoord).a * _CrispOccluderAlpha - _CrispOccluderCutoff);
                return 1;
            }
            ENDCG
        }
    }
}
