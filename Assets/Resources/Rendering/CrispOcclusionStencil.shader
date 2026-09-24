// Used by CrispWorldUICamera (RythmRPG.Core): a full-screen pass drawn first by the Crisp World UI Camera. It samples
// the occlusion mask (characters and notes, drawn with the pixel camera's view) and sets stencil bit 128 on every
// screen pixel they cover. Materials set up with CrispWorldUI.MakeOccludable (hit line, key markers) skip those
// pixels, so the pixel characters and notes underneath show through, as if drawn in front. Writes no colour or depth.
Shader "Hidden/Rythm RPG/Crisp Occlusion Stencil"
{
    SubShader
    {
        // Transparent-100 with the lowest sorting order: before every crisp canvas, and outside the opaque pass
        // (depth priming could otherwise reject it).
        Tags { "Queue" = "Transparent-100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            ColorMask 0

            Stencil
            {
                Ref 128
                WriteMask 128
                Comp Always
                Pass Replace
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 maskUV : TEXCOORD0;
            };

            sampler2D _CrispOcclusionMask;
            float4 _CrispPixelRect; // where the pixel render lands on screen (x, y, width, height in 0..1)

            v2f vert(appdata_t v)
            {
                v2f o;
                float2 ndc = v.vertex.xy; // the quad's corners are already clip-space -1..1
                // Follow the projection flip when rendering into a texture, like any camera-projected object.
                o.vertex = float4(ndc.x, ndc.y * _ProjectionParams.x, 0.5, 1.0);
                float2 screenUV = ndc * 0.5 + 0.5;
                o.maskUV = (screenUV - _CrispPixelRect.xy) / _CrispPixelRect.zw;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                clip(i.maskUV);
                clip(1.0 - i.maskUV);
                clip(tex2D(_CrispOcclusionMask, i.maskUV).r - 0.5);
                return 0;
            }
            ENDCG
        }
    }
}
