// Terrain Weather, step 2: turns the merged puff field of one TerrainWeather into a cel-shaded pixel form and blends
// it over the frame. Everything is decided per render-texture pixel with hard thresholds (no dithering):
//   - outside the merge threshold: a 1 (or 2) pixel outline where the shape is next to it, else nothing;
//   - inside: three flat tones (shadow / base / highlight). Each lobe (the strongest puff under the pixel) is lit
//     from above: dark underside, bright top, plus light on the sun's side from the field's slope. So merged puffs
//     read as a lumpy cloud / snow bank instead of a flat blob;
//   - rims: a 1-pixel highlight along the top edge and the sun-side edge, a shadow along the bottom edge
//     (2 pixels for snow, which gives the drifts some thickness);
//   - opacity: the thin outer part and the thick core (where puffs pile up) have their own opacity.
Shader "Hidden/RythmRPG/TerrainWeatherComposite"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Composite"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _TwHighlight;
            float4 _TwBase;
            float4 _TwShadow;
            float4 _TwOutline;
            float4 _TwLook;     // x = merge threshold, y = core depth (× threshold), z = rim on, w = outline pixels
            float4 _TwLight;    // rgb = scene light multiplier, w = sun side (-1 left .. 1 right)
            float4 _TwOpacity;  // x = outer opacity, y = core opacity, z = snow (1) / air (0)

            float4 Field(int2 p, int2 size)
            {
                return LOAD_TEXTURE2D_X(_BlitTexture, clamp(p, int2(0, 0), size - 1));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                int2 size = int2(_ScaledScreenParams.xy);
                int2 p = int2(input.positionCS.xy);
                float4 f = Field(p, size);
                float d = f.x;
                float threshold = max(_TwLook.x, 0.01);
                half3 light = _TwLight.rgb;
                bool snow = _TwOpacity.z > 0.5;

                // Which pixel row is "up" on screen (render targets can be flipped).
                float uvHere = GetNormalizedScreenSpaceUV(input.positionCS.xy).y;
                float uvNext = GetNormalizedScreenSpaceUV(input.positionCS.xy + float2(0, 1)).y;
                int2 up = int2(0, uvNext > uvHere ? 1 : -1);

                float dl = Field(p + int2(-1, 0), size).x;
                float dr = Field(p + int2(1, 0), size).x;
                float du = Field(p + up, size).x;
                float dd = Field(p - up, size).x;

                if (d < threshold)
                {
                    // Outline: this pixel touches the shape.
                    int width = (int)round(_TwLook.w);
                    if (width <= 0) discard;
                    bool touch = dl >= threshold || dr >= threshold || du >= threshold || dd >= threshold;
                    if (!touch && width >= 2)
                    {
                        touch = Field(p + int2(-2, 0), size).x >= threshold || Field(p + int2(2, 0), size).x >= threshold
                             || Field(p + up * 2, size).x >= threshold || Field(p - up * 2, size).x >= threshold
                             || Field(p + int2(-1, -1), size).x >= threshold || Field(p + int2(1, -1), size).x >= threshold
                             || Field(p + int2(-1, 1), size).x >= threshold || Field(p + int2(1, 1), size).x >= threshold;
                    }
                    if (!touch) discard;
                    return half4(_TwOutline.rgb * light, _TwOutline.a);
                }

                // Height inside the strongest lobe, remapped to the part of a puff that is actually visible.
                float lobe = saturate(f.y / max(f.z, 1e-8));
                float height = snow ? saturate((lobe - 0.45) * 1.8) : saturate((lobe - 0.25) * 2.0);
                // Sideways slope of the field: > 0 when the shape continues to the right (this is its left side).
                float slope = (dr - dl) / max(d, 0.05);
                float sun = _TwLight.w;
                float sideLight = clamp(-slope * sun * 1.5, -1.0, 1.0);

                float tone = snow
                    ? 0.42 + 0.42 * height + 0.3 * sideLight
                    : 0.2 + 0.75 * height + 0.3 * sideLight;
                half3 color = tone < 0.4 ? _TwShadow.rgb : tone < 0.74 ? _TwBase.rgb : _TwHighlight.rgb;

                if (_TwLook.z > 0.5)
                {
                    bool bottom = dd < threshold || (snow && Field(p - up * 2, size).x < threshold);
                    bool top = du < threshold;
                    bool sunEdge = abs(sun) > 0.05 && (sun > 0.0 ? dr : dl) < threshold;
                    if (bottom) color = _TwShadow.rgb;
                    else if (top || sunEdge) color = _TwHighlight.rgb;
                }

                float opacity = d >= threshold * _TwLook.y ? _TwOpacity.y : _TwOpacity.x;
                return half4(color * light, opacity);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
