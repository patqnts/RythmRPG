// Iridescent colour shared by the hit line / key markers ("Hit Line UI") and the character morph sprite. The colours
// and motion are globals set every frame by CombatLanePresentation3D from its theme (Iridescence section), so the
// line, the markers and the morphing character shimmer as one surface.
#ifndef RYTHMRPG_IRIDESCENT_COMMON
#define RYTHMRPG_IRIDESCENT_COMMON

float4 _HitLineIriA;      // ember (yellow)
float4 _HitLineIriB;      // ember hot (green)
float4 _HitLineIriC;      // ash (pink)
float4 _HitLineIriD;      // accent
float4 _HitLineIriParams; // x = hue cycles across the screen, y = cycles per second, z = steps (0 = smooth), w = keep-saturation cutoff

// Cyclic ramp A -> B -> C -> D -> A.
float3 IridescentRamp(float hue)
{
    if (_HitLineIriParams.z >= 1.0) hue = (floor(hue * _HitLineIriParams.z) + 0.5) / _HitLineIriParams.z;
    float x = frac(hue) * 4.0;
    if (x < 1.0) return lerp(_HitLineIriA.rgb, _HitLineIriB.rgb, x);
    if (x < 2.0) return lerp(_HitLineIriB.rgb, _HitLineIriC.rgb, x - 1.0);
    if (x < 3.0) return lerp(_HitLineIriC.rgb, _HitLineIriD.rgb, x - 2.0);
    return lerp(_HitLineIriD.rgb, _HitLineIriA.rgb, x - 3.0);
}

// Hue from the normalised screen position (0-1), so every camera (pixel or crisp) agrees on the colour at a spot.
float3 IridescentAt(float2 screenUV)
{
    float hue = (screenUV.x + screenUV.y * 0.35) * _HitLineIriParams.x + _Time.y * _HitLineIriParams.y;
    return IridescentRamp(hue);
}

// Replaces the colour of white / grey parts (the idle line and outlines) with the iridescent colour at the same
// brightness. Saturated tints (pressed, judgement flash, hold colour...) keep their own colour.
float3 ApplyIridescence(float3 color, float3 tint, float2 screenUV, float amount)
{
    float high = max(tint.r, max(tint.g, tint.b));
    float saturation = high - min(tint.r, min(tint.g, tint.b));
    float keep = max(_HitLineIriParams.w, 0.001);
    float weight = saturate(amount) * saturate(1.0 - saturation / keep);
    float brightness = max(color.r, max(color.g, color.b));
    return lerp(color, IridescentAt(screenUV) * brightness, weight);
}

#endif
