// Palette swap for the pixel dialogue bubbles (uGUI). Each texel is matched to the nearest of up to 8 key colours
// (the colours the bubble sprites are painted with) and replaced by that key's palette colour; texels far from every
// key are left as they are. Colours are set per palette by PixelDialogueTheme. Otherwise identical to UI/Default
// (tint, masking, RectMask2D clipping, alpha clip).
Shader "RythmRPG/UI/Pixel Palette"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _Key0 ("Key 0", Color) = (4,4,4,1)
        _Key1 ("Key 1", Color) = (4,4,4,1)
        _Key2 ("Key 2", Color) = (4,4,4,1)
        _Key3 ("Key 3", Color) = (4,4,4,1)
        _Key4 ("Key 4", Color) = (4,4,4,1)
        _Key5 ("Key 5", Color) = (4,4,4,1)
        _Key6 ("Key 6", Color) = (4,4,4,1)
        _Key7 ("Key 7", Color) = (4,4,4,1)
        _Col0 ("Colour 0", Color) = (1,1,1,1)
        _Col1 ("Colour 1", Color) = (1,1,1,1)
        _Col2 ("Colour 2", Color) = (1,1,1,1)
        _Col3 ("Colour 3", Color) = (1,1,1,1)
        _Col4 ("Colour 4", Color) = (1,1,1,1)
        _Col5 ("Colour 5", Color) = (1,1,1,1)
        _Col6 ("Colour 6", Color) = (1,1,1,1)
        _Col7 ("Colour 7", Color) = (1,1,1,1)
        _MatchDistance ("Max Match Distance", Float) = 0.12

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            float4 _Key0, _Key1, _Key2, _Key3, _Key4, _Key5, _Key6, _Key7;
            float4 _Col0, _Col1, _Col2, _Col3, _Col4, _Col5, _Col6, _Col7;
            float _MatchDistance;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            void Match(float3 source, float4 key, float4 target, inout float best, inout float4 result)
            {
                float3 d = source - key.rgb;
                float distance = dot(d, d);
                if (distance < best)
                {
                    best = distance;
                    result = target;
                }
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 texel = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;

                float best = _MatchDistance * _MatchDistance;
                float4 swapped = float4(texel.rgb, 1.0);
                Match(texel.rgb, _Key0, _Col0, best, swapped);
                Match(texel.rgb, _Key1, _Col1, best, swapped);
                Match(texel.rgb, _Key2, _Col2, best, swapped);
                Match(texel.rgb, _Key3, _Col3, best, swapped);
                Match(texel.rgb, _Key4, _Col4, best, swapped);
                Match(texel.rgb, _Key5, _Col5, best, swapped);
                Match(texel.rgb, _Key6, _Col6, best, swapped);
                Match(texel.rgb, _Key7, _Col7, best, swapped);

                half4 color = half4(swapped.rgb, texel.a * swapped.a) * IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
        ENDCG
        }
    }
}
