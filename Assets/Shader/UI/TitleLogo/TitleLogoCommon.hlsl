// Shared by TitleLogoSDF.shader (TextMeshPro) and TitleLogoSprite.shader (UI Image / RawImage).
// The noise, dissolve field and release formula are mirrored exactly in TitleLogoNoise.cs so the CPU
// flakes detach where the GPU dissolves. Change them together.
//
// The including shader must declare, before this include, these material properties:
//   float4 _FaceColor, _GoldShadow, _GoldColor, _GoldHighlight, _CoreColor, _RingColor;
//   float4 _EmberColor, _EmberHotColor, _AshColor, _ShineColor, _FocusPoint;
//   float _GoldFill, _BurstRadius, _BurstSoftness, _BurstBreakup, _GradientMix, _CoreSize, _CoreIntensity;
//   float _GrungeScale, _GrungeStrength, _GrungeContrast, _GrungeFlow, _GrungeTexStrength, _GrungeTexTiling;
//   float _RingStrength, _RingRadius, _RingCount, _RingWidth, _RingSwirl, _RingTilt, _RingSpeed;
//   float _PulseAmount, _PulseSpeed, _Pixelate, _PixelDensity;
//   float _Disintegrate, _DisintegrateAngle, _DirectionBias, _DissolveScale, _EmberWidth, _CharWidth, _NoiseSeed;
//   float _ShineStrength, _ShineSpeed, _ShineWidth, _ShineAngle, _EffectAspect;
#ifndef RYTHMRPG_TITLE_LOGO_COMMON_INCLUDED
#define RYTHMRPG_TITLE_LOGO_COMMON_INCLUDED

TEXTURE2D(_GrungeTex);
SAMPLER(sampler_GrungeTex);

// ------------------------------------------------------------------------------------------------
// Noise (mirrored in TitleLogoNoise.cs)
// ------------------------------------------------------------------------------------------------
uint TitleHashU(uint x, uint y, uint seed)
{
    uint h = (x * 0x8da6b343u) ^ (y * 0xd8163841u) ^ (seed * 0xcb1ab31fu);
    h ^= h >> 13;
    h *= 0x5bd1e995u;
    h ^= h >> 15;
    return h;
}

float TitleHash01(int2 c, uint seed)
{
    return (float)(TitleHashU(asuint(c.x), asuint(c.y), seed) & 0xFFFFFFu) * (1.0 / 16777216.0);
}

float TitleValueNoise(float2 p, uint seed)
{
    float2 i = floor(p);
    float2 f = p - i;
    int2 c = (int2)i;
    float a = TitleHash01(c, seed);
    float b = TitleHash01(c + int2(1, 0), seed);
    float d0 = TitleHash01(c + int2(0, 1), seed);
    float d1 = TitleHash01(c + int2(1, 1), seed);
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(a, b, u.x), lerp(d0, d1, u.x), u.y);
}

float TitleFbm(float2 p, uint seed)
{
    float sum = 0;
    float amp = 0.5;
    [unroll] for (uint o = 0; o < 3; o++)
    {
        sum += TitleValueNoise(p, seed + o * 101u) * amp;
        p = p * 2.03 + 17.13;
        amp *= 0.5;
    }
    return sum / 0.875;
}

// ------------------------------------------------------------------------------------------------
// Effect space: centred on the logo, 1 unit = logo height, aspect-correct.
// ------------------------------------------------------------------------------------------------
float2 TitleEffectQ(float2 uv) { return float2((uv.x - 0.5) * _EffectAspect, uv.y - 0.5); }

// Pixel grid anchored at the bottom-left corner of the logo, so it lines up with a sprite's own pixels.
float2 TitlePixelize(float2 q)
{
    float2 corner = float2(0.5 * _EffectAspect, 0.5);
    return _Pixelate > 0.5 ? (floor((q + corner) * _PixelDensity) + 0.5) / _PixelDensity - corner : q;
}

// 0..1: when this point dissolves (low = early). Mirrored in TitleLogoNoise.DissolveField.
float TitleDissolveField(float2 q)
{
    float a = radians(_DisintegrateAngle);
    float2 dir = float2(cos(a), sin(a));
    float extent = 0.5 * (abs(dir.x) * _EffectAspect + abs(dir.y));
    float along = saturate((dot(q, dir) + extent) / max(2.0 * extent, 1e-4));
    float n = TitleFbm(q * _DissolveScale, (uint)_NoiseSeed);
    n = saturate((n - 0.5) * 1.8 + 0.5);
    return lerp(n, along, _DirectionBias);
}

// ------------------------------------------------------------------------------------------------
// Accent burst
// ------------------------------------------------------------------------------------------------
struct TitleAccent
{
    half3 accent;   // grungy accent ramp + hot core
    float burst;    // 0 = base (ink / sprite colors), 1 = accent
    half3 overlay;  // additive swirl + shine
};

TitleAccent TitleEvaluateAccent(float2 q, float time)
{
    TitleAccent o;
    uint seed = (uint)_NoiseSeed;
    float2 focus = TitleEffectQ(_FocusPoint.xy);
    float2 d = q - focus;
    float r = length(d);
    float pulse = 1 + _PulseAmount * sin(time * _PulseSpeed * 6.2831853);

    // Grunge: fbm body + fine dark pits (+ optional user texture).
    float2 flow = float2(1.0, 0.37) * (time * _GrungeFlow);
    float grunge = TitleFbm(q * _GrungeScale + flow, seed + 11u);
    grunge = saturate((grunge - 0.5) * _GrungeContrast + 0.5);
    float speck = TitleValueNoise(q * _GrungeScale * 3.1 + 5.7, seed + 23u);
    float pits = smoothstep(0.55, 0.9, speck) * _GrungeStrength;
    half texGrunge = SAMPLE_TEXTURE2D(_GrungeTex, sampler_GrungeTex, q * _GrungeTexTiling).r;

    // Burst mask: radial from the focus, with a torn, grungy boundary.
    float radius = max(_BurstRadius, 1e-4);
    float breakup = (TitleFbm(q * _GrungeScale * 0.45 + 31.7, seed + 5u) - 0.5) * _BurstBreakup;
    float burst = 1 - smoothstep(1 - _BurstSoftness, 1, r / radius + breakup);
    o.burst = saturate(max(burst, _GoldFill));

    // Accent ramp: hotter near the focus, optionally blended with a top-bright gradient.
    float heat = saturate(1 - r / radius);
    heat = heat * heat * (3 - 2 * heat);
    float g = lerp(heat, saturate(q.y + 0.5), _GradientMix);
    g = saturate(g + (grunge - 0.5) * _GrungeStrength * 0.8);
    half3 accent = g < 0.5
        ? lerp(_GoldShadow.rgb, _GoldColor.rgb, g * 2)
        : lerp(_GoldColor.rgb, _GoldHighlight.rgb, g * 2 - 1);
    accent *= 1 - pits * 0.6;
    accent *= lerp(1, texGrunge, _GrungeTexStrength);
    float coreSize = max(_CoreSize, 1e-4);
    accent += _CoreColor.rgb * (exp(-(r * r) / (coreSize * coreSize)) * _CoreIntensity * pulse);
    o.accent = accent;

    // Halo swirl: a tilted spiral around the focus.
    float2 dr = float2(d.x, d.y / max(_RingTilt, 0.05));
    float rrN = length(dr) / max(_RingRadius, 1e-4);
    float ang = atan2(dr.y, dr.x) * (1.0 / 6.2831853);
    float spiral = frac(rrN * _RingCount + ang * _RingSwirl - time * _RingSpeed);
    float lineDist = min(spiral, 1 - spiral);
    float lineHalf = 0.5 * _RingWidth * _RingCount / max(_RingRadius, 1e-4);
    float aa = max(fwidth(rrN * _RingCount), 1e-3);
    float ring = 1 - smoothstep(lineHalf, lineHalf + aa, lineDist);
    ring *= smoothstep(1.0, 0.75, rrN) * smoothstep(0.0, 0.15, rrN);

    // Optional shine sweep over the accent.
    float sa = radians(_ShineAngle);
    float along = dot(q, float2(cos(sa), sin(sa)));
    float sweep = frac(along * 0.32 - time * _ShineSpeed + 0.5) - 0.5;
    float shine = 1 - smoothstep(0, _ShineWidth, abs(sweep));

    o.overlay = _RingColor.rgb * (ring * _RingStrength * pulse)
              + _ShineColor.rgb * (shine * shine * _ShineStrength * o.burst);
    return o;
}

// Detached flakes: flash hot, cool to ember, then to ash.
half3 TitleFlakeColor(half3 finish, float age)
{
    age = saturate(age);
    half3 ember = lerp(_EmberHotColor.rgb, _EmberColor.rgb, saturate(age * 2.5));
    half3 flakeColor = lerp(ember, _AshColor.rgb, saturate(age * 1.6 - 0.15));
    return lerp(finish, flakeColor, saturate(0.55 + age));
}

// Char band -> ember edge -> gone, on a premultiplied color. Call from a uniform branch (uses fwidth).
void TitleApplyDisintegrate(inout half4 color, float2 qp)
{
    float E = _EmberWidth;
    float A = _CharWidth;
    float cut = _Disintegrate * (1.02 + E + A) - A;   // mirrored in TitleLogoNoise.ReleaseProgress
    float f = TitleDissolveField(qp);
    float s = f - cut;
    float aa = _Pixelate > 0.5 ? 1e-5 : max(fwidth(f) * 0.75, 1e-5);
    float alive = smoothstep(-E - aa, -E + aa, s);
    float emberBand = (1 - smoothstep(-aa, aa, s)) * alive;
    float charBand = (1 - smoothstep(0, max(A, 1e-4), s)) * (1 - emberBand);
    float heat = saturate(-s / max(E, 1e-4));
    half3 emberColor = lerp(_EmberColor.rgb, _EmberHotColor.rgb, heat);
    color.rgb = lerp(color.rgb, _AshColor.rgb * color.a, charBand * 0.85);
    color.rgb = lerp(color.rgb, emberColor * color.a, emberBand);
    color *= alive;
}

#endif
