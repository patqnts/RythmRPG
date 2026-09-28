// Rain and snowfall for WeatherController, drawn as one instanced quad per drop / flake with no per-drop CPU work:
// everything is derived from the instance ID and the time, so drops are "stateless" and cost nothing to update.
//   - Drops fill a box around the view but are anchored to the world (they wrap at the box edges), so walking
//     does not drag the rain along with the camera.
//   - Rain: 1-pixel-wide streaks (exactly one pixel per row, snapped to the render texture's pixel grid) that end
//     in a 3-frame pixel splash on the ground.
//   - Snow: 1 or 2 pixel flakes that sway, drift with the wind, then lie on the ground and fade away.
// Depth-tested against the scene (no depth write), so roofs, trees and characters hide what is behind them.
Shader "Hidden/RythmRPG/WeatherPrecipitation"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+50" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Precipitation"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            // Per draw (MaterialPropertyBlock).
            float4 _PrecipMode;    // x = 0 rain / 1 snow, y = time, z = instance count, w = ground height
            float4 _PrecipFocus;   // xyz = box centre (on the ground), w = unused
            float4 _PrecipArea;    // x = size X, y = height, z = size Z
            float4 _PrecipMotion;  // x = fall speed, y = splash / settle seconds, z = streak min, w = streak max
            float4 _PrecipWind;    // xy = wind XZ (already scaled by slant / push), z = sway, w = sway speed
            float4 _PrecipExtra;   // x = splashes on (rain) / big flake share (snow)
            half4 _PrecipColor;    // colour * light

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
                nointerpolation float4 info : TEXCOORD1; // x = kind (0 streak, 1 splash, 2 flake), y = frame / fade, zw = quad size in pixels
            };

            uint HashU(uint x)
            {
                x ^= x >> 16; x *= 0x7feb352du;
                x ^= x >> 15; x *= 0x846ca68bu;
                x ^= x >> 16;
                return x;
            }

            float Hash01(uint x) { return (HashU(x) & 0x00FFFFFFu) / 16777216.0; }

            float2 ToPixels(float4 clip)
            {
                float2 ndc = clip.xy / clip.w;
                return (ndc * 0.5 + 0.5) * _ScreenParams.xy;
            }

            float4 FromPixels(float2 pixels, float4 clip)
            {
                float2 ndc = pixels / _ScreenParams.xy * 2.0 - 1.0;
                return float4(ndc * clip.w, clip.z, clip.w);
            }

            // A world position that stays fixed while the box follows the view: wrap into the box around the focus.
            float2 Anchored(float2 base01, float2 size, float2 centre)
            {
                float2 corner = centre - size * 0.5;
                float2 world = base01 * size;
                return corner + (world - corner) - floor((world - corner) / size) * size;
            }

            Varyings Hidden()
            {
                Varyings o;
                o.positionCS = float4(2, 2, 2, 1); // outside the clip volume
                o.uv = 0;
                o.info = 0;
                return o;
            }

            Varyings Vert(Attributes input)
            {
                uint id = input.instanceID;
                if ((float)id >= _PrecipMode.z) return Hidden();

                bool snow = _PrecipMode.x > 0.5;
                float time = _PrecipMode.y;
                float ground = _PrecipMode.w;
                float height = _PrecipArea.y;
                float speed = max(_PrecipMotion.x, 0.01);
                float fallTime = height / speed;
                float tail = max(_PrecipMotion.y, 0.0);
                float cycle = fallTime + tail;

                // Each drop runs its own cycle, re-randomising its position every time it restarts.
                float offset = Hash01(id * 3u + 1u) * cycle;
                float t = time + offset;
                float cycleIndex = floor(t / cycle);
                float local = t - cycleIndex * cycle;
                uint seed = id * 747796405u + (uint)(int)cycleIndex * 2891336453u;
                float2 base01 = float2(Hash01(seed), Hash01(seed + 17u));
                float2 xz = Anchored(base01, _PrecipArea.xz, _PrecipFocus.xz);

                // Drift during the fall (centred so the column stays in the box on average).
                float fallen = min(local, fallTime);
                xz += _PrecipWind.xy * (fallen - fallTime * 0.5);
                float y = ground + height - speed * fallen;

                Varyings o;
                o.uv = input.uv;
                if (!snow)
                {
                    if (local < fallTime)
                    {
                        // Streak from the head (current position) back up along the motion.
                        float3 velocity = float3(_PrecipWind.x, -speed, _PrecipWind.y);
                        float lengthWorld = lerp(_PrecipMotion.z, _PrecipMotion.w, Hash01(seed + 5u));
                        float3 head = float3(xz.x, y, xz.y);
                        float3 tailPos = head - normalize(velocity) * lengthWorld;
                        float4 headCS = TransformWorldToHClip(head);
                        float4 tailCS = TransformWorldToHClip(tailPos);
                        float2 hp = ToPixels(headCS);
                        float2 tp = ToPixels(tailCS);
                        // Column centres on pixel centres, ends on pixel edges: exactly one pixel per row.
                        hp = float2(floor(hp.x) + 0.5, round(hp.y));
                        tp = float2(floor(tp.x) + 0.5, round(tp.y));
                        if (abs(hp.y - tp.y) < 1.0) tp.y = hp.y + (tp.y >= hp.y ? 1.0 : -1.0);
                        float2 p = lerp(tp, hp, input.uv.y);
                        p.x += (input.uv.x - 0.5);
                        o.positionCS = FromPixels(p, lerp(tailCS, headCS, input.uv.y));
                        o.info = float4(0, 0, 1, abs(hp.y - tp.y));
                        return o;
                    }
                    if (_PrecipExtra.x < 0.5) return Hidden();
                    // Splash: a 7 x 4 pixel quad standing on the landing point.
                    float frame = floor(saturate((local - fallTime) / max(tail, 0.001)) * 2.999);
                    float4 landCS = TransformWorldToHClip(float3(xz.x, ground, xz.y));
                    float2 lp = ToPixels(landCS);
                    lp = float2(floor(lp.x), round(lp.y));
                    float2 corner = lp + float2(-3.0, 0.0);
                    float2 p = corner + input.uv * float2(7.0, 4.0);
                    o.positionCS = FromPixels(p, landCS);
                    o.info = float4(1, frame, 7, 4);
                    return o;
                }

                // Snow: sway and lie on the ground for a moment.
                float phase = Hash01(seed + 9u) * 6.2831853;
                float sway = sin(time * _PrecipWind.w + phase) * _PrecipWind.z * (local < fallTime ? 1.0 : 0.0);
                float3 flake = float3(xz.x + sway, max(y, ground), xz.y + sway * 0.4);
                float settle = local < fallTime ? 0.0 : saturate((local - fallTime) / max(tail, 0.001));
                float sizePx = Hash01(seed + 3u) < _PrecipExtra.x ? 2.0 : 1.0;
                float4 flakeCS = TransformWorldToHClip(flake);
                float2 fp = floor(ToPixels(flakeCS));
                float2 p = fp + input.uv * sizePx;
                o.positionCS = FromPixels(p, flakeCS);
                o.info = float4(2, settle, sizePx, sizePx);
                return o;
            }

            // Splash frames on a 7 x 4 grid (x = 0..6, y = 0 at the bottom).
            float SplashAlpha(int2 p, int frame)
            {
                if (frame == 0)
                {
                    if (p.y == 0 && abs(p.x - 3) <= 1) return 1.0;
                    if (p.y == 1 && p.x == 3) return 0.8;
                    return 0.0;
                }
                if (frame == 1)
                {
                    if (p.y == 0 && (p.x == 1 || p.x == 5)) return 0.8;
                    if (p.y == 2 && (p.x == 2 || p.x == 4)) return 1.0;
                    return 0.0;
                }
                if (p.y == 1 && (p.x == 0 || p.x == 6)) return 0.6;
                if (p.y == 3 && (p.x == 1 || p.x == 5)) return 0.45;
                return 0.0;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = _PrecipColor;
                if (input.info.x < 0.5)
                {
                    // Brighter at the head of the streak.
                    color.a *= lerp(0.35, 1.0, input.uv.y);
                    return color;
                }
                if (input.info.x < 1.5)
                {
                    int2 p = (int2)floor(input.uv * input.info.zw);
                    float a = SplashAlpha(p, (int)input.info.y);
                    clip(a - 0.01);
                    color.a *= a;
                    return color;
                }
                // Settled flakes fade out.
                color.a *= 1.0 - input.info.y;
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
