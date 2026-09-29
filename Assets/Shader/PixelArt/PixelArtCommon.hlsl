// Shared code for the RythmRPG pixel-art shaders ("RythmRPG/Pixel Sprite" and "RythmRPG/Pixel Mesh").
//
// Look (Eastward-style "light buffer"): every light that reaches a pixel (scene ambient or a flat ambient colour,
// the sun with its shadows, and any number of point / spot lights through URP's light loop, Forward+ included) is
// added into one light value, computed at the centre of the texture's texel, then posterised into a few steps with
// ordered dithering anchored to the texels. The surface colour is multiplied by that light. So light and shadow
// edges land on whole art pixels and step like pixel art instead of smearing smoothly.
//
// Define PIXEL_SPRITE before including for SpriteRenderer use (sprite flip + SpriteRenderer colour, flat card normal).

#ifndef RYTHMRPG_PIXEL_ART_COMMON_INCLUDED
#define RYTHMRPG_PIXEL_ART_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

TEXTURE2D(_MainTex);        SAMPLER(sampler_MainTex);
TEXTURE2D(_NormalMap);      SAMPLER(sampler_NormalMap);
TEXTURE2D(_EmissionMap);    SAMPLER(sampler_EmissionMap);

// One UnityPerMaterial layout for every pass of both shaders (SRP Batcher compatible).
CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
    float4 _MainTex_TexelSize;
    half4 _BaseColor;
    half _Cutoff;
    half _DitherFade;
    half _Surface;
    half _Lit;

    half4 _AmbientColor;
    half _SceneAmbient;
    half _AmbientStrength;
    half _LightBands;
    half _LightDither;
    half _TexelSnap;
    half _NormalInfluence;
    half _Wrap;
    half _NormalStrength;
    half _ShadowStrength;
    half _MaxLight;

    half4 _EmissionColor;
    half4 _FlashColor;
    half _FlashAmount;

    half _Outline;
    half4 _OutlineColor;
    half _OutlineWidth;

    half _XRay;
    half4 _XRayColor;
    half _XRayOutlineWidth;
    half _XRayOutlineOpacity;
    half _XRayFill;
    half _XRayFillStyle;
    half _XRaySpriteDetail;
    half _XRayPulse;
    half _XRayPulseSpeed;

    half _Cull;
    half _SrcBlend;
    half _DstBlend;
    half _ZWrite;
    half _UseNormalMap;
CBUFFER_END

// ---------------------------------------------------------------- small helpers

// 4x4 Bayer threshold, 0..1.
half PixelBayer4(float2 cell)
{
    uint2 q = uint2(floor(abs(cell))) % 4u;
    static const half m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    return (m[q.y * 4u + q.x] + 0.5h) / 16.0h;
}

// Eye (view) depth from a raw depth-buffer value; orthographic cameras included (the pixel camera is orthographic,
// and the oblique projection leaves depth unchanged).
float PixelEyeDepth(float rawDepth)
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

#if defined(PIXEL_SPRITE)
// Sprite flip and colour come from the SpriteRenderer. Other renderers leave them at zero: then flip = 1, colour = 1.
float2 PixelSpriteFlip()
{
    float2 flip = unity_SpriteProps.xy;
    return float2(abs(flip.x) > 0.5 ? flip.x : 1.0, abs(flip.y) > 0.5 ? flip.y : 1.0);
}

half4 PixelSpriteColor()
{
    float2 flip = unity_SpriteProps.xy;
    bool isSprite = abs(flip.x) > 0.5 || abs(flip.y) > 0.5;
    return isSprite ? (half4)unity_SpriteColor : half4(1, 1, 1, 1);
}
#endif

float3 PixelObjectPosition(float3 positionOS)
{
    #if defined(PIXEL_SPRITE)
        positionOS.xy *= PixelSpriteFlip();
    #endif
    return positionOS;
}

// Surface coverage and fade. Cutout: hard alpha test, plus the renderer / tint alpha dissolving through an ordered
// dither (so fades still work while the sprite writes depth). Transparent: plain alpha.
half PixelAlpha(half textureAlpha, half fade, float2 screenPixel, bool forceCutout)
{
    if (_Surface < 0.5 || forceCutout)
    {
        clip(textureAlpha - _Cutoff);
        if (_DitherFade > 0.5)
            clip(fade - PixelBayer4(screenPixel) * 0.999h);
        else
            clip(fade - 0.001h);
        return 1.0h;
    }
    half a = textureAlpha * fade;
    clip(a - 0.002h);
    return a;
}

// ---------------------------------------------------------------- lighting

// World position moved to the centre of the texel under this pixel, so lighting and shadows are constant per art
// pixel. Solved from screen derivatives of uv and position.
float3 PixelTexelCenterPosition(float3 positionWS, float2 uv)
{
    float2 size = max(_MainTex_TexelSize.zw, 1.0);
    float2 t = uv * size;
    float2 toCenter = (floor(t) + 0.5) - t;
    float2 tdx = ddx(t);
    float2 tdy = ddy(t);
    float3 pdx = ddx(positionWS);
    float3 pdy = ddy(positionWS);
    float det = tdx.x * tdy.y - tdx.y * tdy.x;
    if (abs(det) < 1e-6)
        return positionWS;
    float a = (toCenter.x * tdy.y - toCenter.y * tdy.x) / det;
    float b = (tdx.x * toCenter.y - tdx.y * toCenter.x) / det;
    // Never move further than about one texel (guards against degenerate derivatives at silhouettes).
    a = clamp(a, -1.0, 1.0);
    b = clamp(b, -1.0, 1.0);
    return positionWS + a * pdx + b * pdy;
}

half PixelDiffuse(half3 normalWS, half3 lightDirection)
{
    half ndl = dot(normalWS, lightDirection);
    half wrapped = saturate((ndl + _Wrap) / (1.0h + _Wrap));
    return lerp(1.0h, wrapped, _NormalInfluence);
}

// Posterise the summed light by its brightest channel (the hue is kept) into flat, hard-edged steps.
// No dithering (Step Dither is ignored). Light dimmer than half the first step is kept as it is instead of being
// rounded down to black, so unlit sprites stay dark but readable.
half3 PixelPosterize(half3 light, float2 ditherCell)
{
    if (_LightBands < 0.5h)
        return light;
    half level = max(max(light.r, light.g), light.b);
    if (level <= 1e-4h)
        return light;
    half stepped = floor(level * _LightBands + 0.5h) / _LightBands;
    if (stepped <= 0.0h)
        return light;
    return light * (stepped / level);
}

// The whole "light buffer" for one pixel.
half3 PixelLight(float3 positionWS, half3 normalWS, float2 normalizedScreenUV, float2 ditherCell)
{
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalWS = normalWS;
    inputData.normalizedScreenSpaceUV = normalizedScreenUV;
    inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);

    half3 ambient = _SceneAmbient > 0.5h ? SampleSH(normalWS) : _AmbientColor.rgb;
    half3 light = ambient * _AmbientStrength;

    Light mainLight = GetMainLight(inputData.shadowCoord);
    half mainShadow = lerp(1.0h, mainLight.shadowAttenuation, _ShadowStrength);
    light += mainLight.color * (mainLight.distanceAttenuation * mainShadow * PixelDiffuse(normalWS, mainLight.direction));

    #if defined(_ADDITIONAL_LIGHTS)
        half4 shadowMask = half4(1, 1, 1, 1);
        uint pixelLightCount = GetAdditionalLightsCount();
        #if USE_CLUSTER_LIGHT_LOOP
        [loop] for (uint dirIndex = 0; dirIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); dirIndex++)
        {
            Light dirLight = GetAdditionalLight(dirIndex, positionWS, shadowMask);
            half dirShadow = lerp(1.0h, dirLight.shadowAttenuation, _ShadowStrength);
            light += dirLight.color * (dirLight.distanceAttenuation * dirShadow * PixelDiffuse(normalWS, dirLight.direction));
        }
        #endif
        LIGHT_LOOP_BEGIN(pixelLightCount)
            Light pointLight = GetAdditionalLight(lightIndex, positionWS, shadowMask);
            half pointShadow = lerp(1.0h, pointLight.shadowAttenuation, _ShadowStrength);
            light += pointLight.color * (pointLight.distanceAttenuation * pointShadow * PixelDiffuse(normalWS, pointLight.direction));
        LIGHT_LOOP_END
    #endif

    light = min(light, (half3)_MaxLight);
    return PixelPosterize(light, ditherCell);
}

#endif // RYTHMRPG_PIXEL_ART_COMMON_INCLUDED
