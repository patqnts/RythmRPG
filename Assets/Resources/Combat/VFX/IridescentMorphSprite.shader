// The character while it morphs into / out of the hit line: the sprite, with its colours washed into the same
// iridescent cycle as the line (IridescentCommon.cginc). _Iridescence 0 = the plain sprite, 1 = fully iridescent
// (sprite shading kept as brightness). Used by CharacterHitLineMorph on a copy of the player's sprite.
Shader "Rythm RPG/Combat/Iridescent Morph Sprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Iridescence ("Iridescence", Range(0, 1)) = 0
        _Glow ("Brighten", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "IridescentCommon.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float _Iridescence;
            float _Glow;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, i.texcoord) * i.color;
                float luma = dot(color.rgb, float3(0.299, 0.587, 0.114));
                float3 shimmer = IridescentAt(i.vertex.xy / _ScreenParams.xy) * lerp(0.55 + 0.45 * luma, 1.0, _Glow);
                color.rgb = lerp(color.rgb, shimmer, saturate(_Iridescence));
                return color;
            }
            ENDCG
        }
    }
}
