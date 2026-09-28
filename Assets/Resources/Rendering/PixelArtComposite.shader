// Full-screen passes for PixelArtRendererFeature, drawn over the camera colour with alpha blending.
//   Pass 0 "PixelOutline": pixel outlines around everything that drew into _PixelOutlineMask (rgb = colour,
//          a = width / 3) with eye depth in _PixelOutlineDepth. A pixel gets the outline colour of the nearest
//          outlined surface within its width (diamond = classic pixel-art corners) that is in front of what is here.
//   Pass 1 "PixelXRay": for parts of x-ray objects hidden behind something (_PixelXRayMask a = 1; 0.5 = visible),
//          an ordered-dither fill inside and a solid 1 px outline around the hidden shape.
// Everything is read with pixel loads: the pass runs at the camera's own resolution (480x270 for the pixel camera).
Shader "Hidden/RythmRPG/PixelArtComposite"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        HLSLINCLUDE
        #pragma target 3.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        TEXTURE2D(_PixelOutlineMask);
        TEXTURE2D_FLOAT(_PixelOutlineDepth);
        TEXTURE2D(_PixelXRayMask);
        TEXTURE2D(_PixelXRayFill);
        TEXTURE2D(_PixelXRayStyle);

        float4 _PixelTargetSize;     // w, h, 1/w, 1/h
        float4 _PixelOutlineParams;  // x = depth step (eye units), y = opacity, z = square corners (0/1)
        float4 _PixelXRayParams;     // x = global strength (multiplies fill and outline opacity)

        half CompositeBayer4(uint2 cell)
        {
            uint2 q = cell % 4u;
            static const half m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            return (m[q.y * 4u + q.x] + 0.5h) / 16.0h;
        }

        float CompositeEyeDepth(float rawDepth)
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

        int2 ClampPixel(int2 p)
        {
            return clamp(p, int2(0, 0), int2(_PixelTargetSize.xy) - 1);
        }
        ENDHLSL

        Pass
        {
            Name "PixelOutline"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragOutline

            half4 FragOutline(Varyings input) : SV_Target
            {
                int2 p = ClampPixel(int2(input.positionCS.xy));
                half4 self = LOAD_TEXTURE2D(_PixelOutlineMask, p);
                float hereDepth = self.a > 0.0h
                    ? LOAD_TEXTURE2D(_PixelOutlineDepth, p).r
                    : CompositeEyeDepth(LoadSceneDepth(uint2(p)));
                float step = _PixelOutlineParams.x;
                bool square = _PixelOutlineParams.z > 0.5;

                float bestDepth = 1e20;
                half3 bestColor = half3(0, 0, 0);
                [unroll] for (int y = -3; y <= 3; y++)
                {
                    [unroll] for (int x = -3; x <= 3; x++)
                    {
                        int reach = square ? max(abs(x), abs(y)) : abs(x) + abs(y);
                        if (reach == 0 || reach > 3)
                            continue;
                        int2 q = ClampPixel(p + int2(x, y));
                        half4 m = LOAD_TEXTURE2D(_PixelOutlineMask, q);
                        if (m.a <= 0.0h || reach > (int)round(m.a * 3.0h))
                            continue;
                        float d = LOAD_TEXTURE2D(_PixelOutlineDepth, q).r;
                        if (d < hereDepth - step && d < bestDepth)
                        {
                            bestDepth = d;
                            bestColor = m.rgb;
                        }
                    }
                }
                clip(1e19 - bestDepth);
                return half4(bestColor, _PixelOutlineParams.y);
            }
            ENDHLSL
        }

        Pass
        {
            Name "PixelXRay"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragXRay

            half4 FragXRay(Varyings input) : SV_Target
            {
                int2 p = ClampPixel(int2(input.positionCS.xy));
                half4 s = LOAD_TEXTURE2D(_PixelXRayMask, p);
                half strength = _PixelXRayParams.x;

                if (s.a > 0.75h)
                {
                    // Hidden part: fill with this material's fill colour / amount / style.
                    half4 fill = LOAD_TEXTURE2D(_PixelXRayFill, p);
                    half4 style = LOAD_TEXTURE2D(_PixelXRayStyle, p);
                    half amount = fill.a * strength;
                    if (style.b < 0.5h)
                    {
                        clip(amount - CompositeBayer4(uint2(p)));
                        return half4(fill.rgb, 1.0h);
                    }
                    clip(amount - 0.001h);
                    return half4(fill.rgb, amount);
                }

                if (s.a < 0.25h)
                {
                    // Not the character here: outline if a hidden part within its outline width is nearby
                    // (diamond reach, like the pixel outline). The nearest one wins.
                    int bestReach = 99;
                    half3 color = half3(0, 0, 0);
                    half opacity = 0.0h;
                    [unroll] for (int y = -3; y <= 3; y++)
                    {
                        [unroll] for (int x = -3; x <= 3; x++)
                        {
                            int reach = abs(x) + abs(y);
                            if (reach == 0 || reach > 3 || reach >= bestReach)
                                continue;
                            int2 q = ClampPixel(p + int2(x, y));
                            half4 n = LOAD_TEXTURE2D(_PixelXRayMask, q);
                            if (n.a <= 0.75h)
                                continue;
                            half4 st = LOAD_TEXTURE2D(_PixelXRayStyle, q);
                            if (reach > (int)round(st.g * 3.0h))
                                continue;
                            bestReach = reach;
                            color = n.rgb;
                            opacity = st.r;
                        }
                    }
                    opacity *= strength;
                    clip(opacity - 0.001h);
                    return half4(color, opacity);
                }

                clip(-1.0);
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
