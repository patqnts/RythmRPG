// Space flight backdrop for combat (SpaceCombatBackdrop, RythmRPG.Combat). Drawn on a camera-facing quad at the far
// plane (behind everything), inside the pixel render. Everything is procedural and laid out in render-texture pixels (the screen
// position of each pixel), so stars are exact pixel-art pixels however the camera moves or zooms:
// - a vertical gradient,
// - a slow, posterized nebula (value noise),
// - stars, in one of two motions (_Motion):
//   Forward (1): flying into the screen. Stars come out of a vanishing point and rush outward toward the viewer,
//     growing and leaving radial streaks, over a still field of distant stars (the far layer).
//   Upward (0): three star layers scroll down the screen at their own speed (far, mid, near); the near layer draws
//     streaks. Scroll offsets come from the component in whole pixels (no shimmer) and wrap seamlessly (the star hash
//     repeats every 4096 cells vertically).
// Stars twinkle.
Shader "Rythm RPG/Space Backdrop"
{
    Properties
    {
        _TopColor ("Top Color", Color) = (0.02, 0.02, 0.06, 1)
        _BottomColor ("Bottom Color", Color) = (0.05, 0.02, 0.10, 1)
    }

    SubShader
    {
        // First of the transparent range (2501): drawn after the opaque world (which covers it by depth) and before every
        // transparent sprite, note and effect. Kept out of the opaque pass so depth priming cannot reject it.
        Tags { "Queue" = "Transparent-499" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _TopColor;
            float4 _BottomColor;
            float4 _NebulaColorA;
            float4 _NebulaColorB;
            float4 _NebulaParams;   // x = scale (px), y = strength, z = posterize levels, w = seed
            float4 _NebulaScroll;   // xy = offset (px)
            float4 _LayerScroll;    // xyz = vertical offset per layer (whole px), w = time (s)
            float4 _LayerCell;      // xyz = cell size per layer (px)
            float4 _LayerDensity;   // xyz = chance of a star per cell
            float4 _LayerSize;      // xyz = star size (px)
            float4 _LayerStreak;    // xyz = streak length behind the star (px)
            float4 _StarColor0;
            float4 _StarColor1;
            float4 _StarColor2;
            float _Twinkle;
            float _Motion;          // 0 = upward scroll, 1 = forward (into the screen)
            float4 _Forward;        // x = travel (depth layers passed), y = cell (px, far), z = density, w = streak share
            float4 _ForwardStar;    // colour (a = strength)
            float4 _Vanish;         // xy = vanishing point (0-1 screen)

            static const float HashPeriod = 4096.0;
            static const int ForwardLayers = 6;

            struct appdata_t
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 screen : TEXCOORD0;
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.screen = ComputeScreenPos(o.vertex);
                return o;
            }

            float4 Hash4(float2 p)
            {
                float4 q = float4(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)),
                                  dot(p, float2(419.2, 371.9)), dot(p, float2(223.3, 97.1)));
                return frac(sin(q) * 43758.5453);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash4(i).x;
                float b = Hash4(i + float2(1, 0)).x;
                float c = Hash4(i + float2(0, 1)).x;
                float d = Hash4(i + float2(1, 1)).x;
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // Brightness 0..1 of one star layer at pixel `px`.
            float StarLayer(float2 px, float scroll, float cell, float density, float size, float streak, float seed)
            {
                cell = max(cell, 2.0);
                float2 p = float2(px.x, px.y + scroll);
                float2 c = floor(p / cell);
                float2 local = p - c * cell;
                c.y -= HashPeriod * floor(c.y / HashPeriod); // repeats: the component wraps the scroll to match
                float4 h = Hash4(c + seed * 17.13);
                if (h.x > density) return 0.0;

                float roomX = max(1.0, cell - size);
                float roomY = max(1.0, cell - size - streak);
                float2 star = floor(float2(h.y * roomX, h.z * roomY));
                float2 d = local - star;
                float b = 0.0;
                if (d.x >= 0.0 && d.x < size)
                {
                    if (d.y >= 0.0 && d.y < size) b = 1.0;
                    // Stars move down the screen: the streak trails above them.
                    else if (streak > 0.0 && d.y >= size && d.y < size + streak) b = 1.0 - (d.y - size + 1.0) / (streak + 1.0);
                }
                float twinkle = 0.5 + 0.5 * sin(_LayerScroll.w * (1.5 + h.w * 3.0) + h.w * 6.2831);
                return b * lerp(1.0 - _Twinkle, 1.0, twinkle);
            }

            // Flying forward: ForwardLayers depth slices of stars, each slice moving from far (scale 1) toward the viewer
            // (scale ~25) and wrapping back with new stars. A star is drawn as a short radial streak ending at its
            // position (toward the vanishing point), brightest at the head. Distances are measured in screen pixels,
            // so lines are 1 pixel wide far away and thicker up close.
            float ForwardStars(float2 px)
            {
                float2 d = px + 0.5 - _Vanish.xy * _ScreenParams.xy;
                float cell = max(_Forward.y, 2.0);
                float result = 0.0;
                for (int i = 0; i < ForwardLayers; i++)
                {
                    float cycle = (float)i / ForwardLayers + _Forward.x;
                    float layer = frac(cycle);                  // 0 = far, 1 = at the viewer
                    float scale = 1.0 / max(0.04, 1.0 - layer); // perspective
                    float2 q = d / scale;                        // the slice's own space (pixels at the far distance)
                    float seed = floor(cycle) * 7.31 + i * 13.7;
                    float fade = smoothstep(0.0, 0.35, layer);
                    float width = lerp(0.5, 1.25, layer);
                    // A star whose streak covers q has its head between q and q / (1 - streak), further out on the
                    // same ray. Look at q's cell and its neighbours, then at the cells along that stretch (every half
                    // cell, up to 24 of them).
                    float reach = 1.0 / max(0.05, 1.0 - _Forward.w);
                    float stretch = length(q) * (reach - 1.0);
                    int samples = (int)clamp(ceil(stretch / (0.5 * cell)), 0.0, 24.0);
                    [loop] for (int s = 0; s <= samples; s++)
                    {
                        float2 c0 = floor(q * lerp(1.0, reach, samples > 0 ? (float)s / samples : 0.0) / cell);
                        int neighbours = s == 0 ? 9 : 1;
                        [loop] for (int n = 0; n < neighbours; n++)
                        {
                            float2 c = neighbours == 9 ? c0 + float2(n % 3 - 1, n / 3 - 1) : c0;
                            float4 h = Hash4(c + seed);
                            if (h.x > _Forward.z) continue;
                            float2 head = (c + h.yz) * cell;
                            float2 tail = head * (1.0 - _Forward.w);
                            float2 ab = head - tail;
                            float2 aq = q - tail;
                            float k = saturate(dot(aq, ab) / max(dot(ab, ab), 1e-5));
                            float dist = length(aq - ab * k) * scale;
                            if (dist < width)
                            {
                                float twinkle = 0.5 + 0.5 * sin(_LayerScroll.w * (2.0 + h.w * 3.0) + h.w * 6.2831);
                                float b = fade * lerp(0.35, 1.0, k) * lerp(1.0 - _Twinkle * 0.5, 1.0, twinkle);
                                result = max(result, b);
                            }
                        }
                    }
                }
                return result;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.screen.xy / max(i.screen.w, 0.0001);
                float2 px = floor(uv * _ScreenParams.xy);

                float3 color = lerp(_BottomColor.rgb, _TopColor.rgb, saturate(uv.y));

                // Nebula: two octaves of value noise, posterized into a few bands for a pixel-art look.
                if (_NebulaParams.y > 0.0)
                {
                    float2 np = (px + _NebulaScroll.xy) / max(_NebulaParams.x, 1.0) + _NebulaParams.w;
                    float n = ValueNoise(np) * 0.65 + ValueNoise(np * 2.03 + 11.7) * 0.35;
                    n = saturate((n - 0.45) * 2.2);
                    float levels = max(_NebulaParams.z, 1.0);
                    n = floor(n * levels) / levels;
                    float3 nebula = lerp(_NebulaColorA.rgb, _NebulaColorB.rgb, n);
                    color = lerp(color, nebula, n * _NebulaParams.y);
                }

                if (_Motion > 0.5)
                {
                    // Distant stars stay put; the near ones rush past.
                    float far = StarLayer(px, 0.0, _LayerCell.x, _LayerDensity.x, _LayerSize.x, 0.0, 1.0);
                    color = lerp(color, _StarColor0.rgb, far * _StarColor0.a);
                    color = lerp(color, _ForwardStar.rgb, ForwardStars(px) * _ForwardStar.a);
                    return fixed4(color, 1.0);
                }

                float s0 = StarLayer(px, _LayerScroll.x, _LayerCell.x, _LayerDensity.x, _LayerSize.x, _LayerStreak.x, 1.0);
                float s1 = StarLayer(px, _LayerScroll.y, _LayerCell.y, _LayerDensity.y, _LayerSize.y, _LayerStreak.y, 2.0);
                float s2 = StarLayer(px, _LayerScroll.z, _LayerCell.z, _LayerDensity.z, _LayerSize.z, _LayerStreak.z, 3.0);
                color = lerp(color, _StarColor0.rgb, s0 * _StarColor0.a);
                color = lerp(color, _StarColor1.rgb, s1 * _StarColor1.a);
                color = lerp(color, _StarColor2.rgb, s2 * _StarColor2.a);
                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }
}
