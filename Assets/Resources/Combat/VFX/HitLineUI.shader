// UI shader for the world-space Perfect Hit Line canvas. Same as UI/Default, but depth-tested against the scene so
// characters (and anything else that writes depth) standing between the camera and the line cover it, like any other
// object in the world. A small depth bias toward the camera keeps the ground or props right at the line from
// swallowing it. Set Depth Test to Always to get the old draw-over-everything behaviour back.
// Iridescence (0-1): white / grey parts shimmer through the ember yellow -> ember hot green -> ash pink -> accent cycle
// (IridescentCommon.cginc; colours set by CombatLanePresentation3D from the lane theme). Saturated colours are kept.
Shader "Rythm RPG/Combat/Hit Line UI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth Test", Float) = 4
        _DepthOffset ("Depth Offset (negative = toward camera)", Range(-8, 0)) = -1
        _Iridescence ("Iridescence", Range(0, 1)) = 0

        // UI stencil (as in UI/Default). CrispWorldUI.MakeOccludable sets these so characters and notes, drawn
        // into stencil bit 128 by the Crisp World UI Camera, cover the line. Defaults: no stencil test.
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
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
        ZTest [_ZTest]
        Offset [_DepthOffset], [_DepthOffset]
        Blend SrcAlpha OneMinusSrcAlpha

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

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
                if (_Iridescence > 0.0)
                    color.rgb = ApplyIridescence(color.rgb, i.color.rgb, i.vertex.xy / _ScreenParams.xy, _Iridescence);
                return color;
            }
            ENDCG
        }
    }
}
