// Vertex / fragment programs for every pass of the RythmRPG pixel-art shaders. Included after PixelArtCommon.hlsl.
//
//   Forward          VertForward / FragForward      lit (PixelLight) or unlit colour, emission, hit flash, fog
//   ShadowCaster     VertShadow / FragDepthClip     alpha-tested shadows
//   DepthOnly        VertDepth / FragDepthOnly      alpha-tested depth (depth prepass / depth texture)
//   DepthNormals     VertDepth / FragDepthNormals   alpha-tested depth + normals
//   PixelOutlineMask VertDepth / FragOutlineMask    outline colour + width, and eye depth (MRT) for the outline feature
//   PixelXRay        VertDepth / FragXRay           x-ray colour + hidden/visible state for the x-ray feature

#ifndef RYTHMRPG_PIXEL_ART_PASSES_INCLUDED
#define RYTHMRPG_PIXEL_ART_PASSES_INCLUDED

struct PixelAttributes
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
    half4 color : COLOR;
    #if !defined(PIXEL_SPRITE)
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    #endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

// Sprites are flat cards facing -Z (toward the camera), with +X as tangent.
void PixelObjectNormal(PixelAttributes input, out float3 normalOS, out float4 tangentOS)
{
    #if defined(PIXEL_SPRITE)
        normalOS = float3(0, 0, -1);
        tangentOS = float4(PixelSpriteFlip().x, 0, 0, 1);
    #else
        normalOS = input.normalOS;
        tangentOS = input.tangentOS;
    #endif
}

half4 PixelVertexColor(half4 vertexColor)
{
    #if defined(PIXEL_SPRITE)
        return vertexColor * PixelSpriteColor();
    #else
        return vertexColor;
    #endif
}

// ================================================================ Forward

struct PixelForwardVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    half4 color : COLOR;
    float3 positionWS : TEXCOORD1;
    half3 normalWS : TEXCOORD2;
    half4 tangentWS : TEXCOORD3;
    half fogFactor : TEXCOORD4;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

PixelForwardVaryings VertForward(PixelAttributes input)
{
    PixelForwardVaryings output = (PixelForwardVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs position = GetVertexPositionInputs(PixelObjectPosition(input.positionOS.xyz));
    float3 normalOS;
    float4 tangentOS;
    PixelObjectNormal(input, normalOS, tangentOS);
    VertexNormalInputs normals = GetVertexNormalInputs(normalOS, tangentOS);

    output.positionCS = position.positionCS;
    output.positionWS = position.positionWS;
    output.normalWS = (half3)normals.normalWS;
    output.tangentWS = half4(normals.tangentWS, tangentOS.w * GetOddNegativeScale());
    output.uv = TRANSFORM_TEX(input.uv, _MainTex);
    output.color = PixelVertexColor(input.color);
    output.fogFactor = (half)ComputeFogFactor(position.positionCS.z);
    return output;
}

half3 PixelSurfaceNormal(PixelForwardVaryings input, float2 uv)
{
    half3 normalWS = normalize(input.normalWS);
    #if defined(_NORMALMAP)
        half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv), _NormalStrength);
        half3 tangentWS = normalize(input.tangentWS.xyz);
        half3 bitangentWS = cross(normalWS, tangentWS) * input.tangentWS.w;
        normalWS = normalize(TransformTangentToWorld(normalTS, half3x3(tangentWS, bitangentWS, normalWS)));
    #endif
    return normalWS;
}

half4 FragForward(PixelForwardVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float2 uv = input.uv;
    half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
    half4 tint = input.color * _BaseColor;
    half alpha = PixelAlpha(texel.a, tint.a, input.positionCS.xy, false);
    half3 albedo = texel.rgb * tint.rgb;

    // Derivatives must be taken outside any branch.
    float3 snappedWS = PixelTexelCenterPosition(input.positionWS, uv);
    float3 lightWS = _TexelSnap > 0.5h ? snappedWS : input.positionWS;
    float2 ditherCell = floor(uv * max(_MainTex_TexelSize.zw, 1.0));

    half3 color = albedo;
    #if defined(_PIXEL_LIT)
        half3 normalWS = PixelSurfaceNormal(input, uv);
        float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
        color = albedo * PixelLight(lightWS, normalWS, screenUV, ditherCell);
    #endif

    color += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uv).rgb * _EmissionColor.rgb;
    color = lerp(color, _FlashColor.rgb, _FlashAmount);
    color = MixFog(color, input.fogFactor);
    return half4(color, alpha);
}

// ================================================================ Depth-style passes (shared vertex)

struct PixelDepthVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    half4 color : COLOR;
    half3 normalWS : TEXCOORD1;
    float3 positionWS : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

PixelDepthVaryings VertDepth(PixelAttributes input)
{
    PixelDepthVaryings output = (PixelDepthVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float3 normalOS;
    float4 tangentOS;
    PixelObjectNormal(input, normalOS, tangentOS);
    float3 positionWS = TransformObjectToWorld(PixelObjectPosition(input.positionOS.xyz));
    output.positionCS = TransformWorldToHClip(positionWS);
    output.positionWS = positionWS;
    output.normalWS = (half3)TransformObjectToWorldNormal(normalOS);
    output.uv = TRANSFORM_TEX(input.uv, _MainTex);
    output.color = PixelVertexColor(input.color);
    return output;
}

// Coverage test shared by the depth-style passes. Transparent materials are treated as a 50% cutout here.
void PixelClipCoverage(PixelDepthVaryings input)
{
    half texAlpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
    half fade = input.color.a * _BaseColor.a;
    if (_Surface > 0.5h)
        clip(texAlpha * fade - 0.5h);
    else
        PixelAlpha(texAlpha, fade, input.positionCS.xy, true);
}

// ---------------------------------------------------------------- ShadowCaster

float3 _LightDirection;
float3 _LightPosition;

PixelDepthVaryings VertShadow(PixelAttributes input)
{
    PixelDepthVaryings output = (PixelDepthVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    float3 normalOS;
    float4 tangentOS;
    PixelObjectNormal(input, normalOS, tangentOS);
    float3 positionWS = TransformObjectToWorld(PixelObjectPosition(input.positionOS.xyz));
    float3 normalWS = TransformObjectToWorldNormal(normalOS);
    #if _CASTING_PUNCTUAL_LIGHT_SHADOW
        float3 lightDirectionWS = normalize(_LightPosition - positionWS);
    #else
        float3 lightDirectionWS = _LightDirection;
    #endif
    output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
    output.normalWS = (half3)normalWS;
    output.uv = TRANSFORM_TEX(input.uv, _MainTex);
    output.color = PixelVertexColor(input.color);
    return output;
}

half4 FragDepthClip(PixelDepthVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    PixelClipCoverage(input);
    return 0;
}

// ---------------------------------------------------------------- DepthOnly / DepthNormals

half FragDepthOnly(PixelDepthVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    PixelClipCoverage(input);
    return input.positionCS.z;
}

half4 FragDepthNormals(PixelDepthVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    PixelClipCoverage(input);
    float3 normalWS = normalize((float3)input.normalWS);
    #if defined(_GBUFFER_NORMALS_OCT)
        float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
        float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
        return half4(PackFloat2To888(remappedOctNormalWS), 0.0);
    #else
        return half4(NormalizeNormalPerPixel(normalWS), 0.0);
    #endif
}

// ---------------------------------------------------------------- PixelOutlineMask (PixelArtRendererFeature)

struct PixelOutlineMaskOutput
{
    half4 color : SV_Target0;   // rgb = outline colour, a = width / 3 (0 = no outline here)
    float depth : SV_Target1;   // eye depth of this surface
};

PixelOutlineMaskOutput FragOutlineMask(PixelDepthVaryings input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    clip(_Outline - 0.5h);
    PixelClipCoverage(input);
    PixelOutlineMaskOutput output;
    output.color = half4(_OutlineColor.rgb, saturate(round(_OutlineWidth) / 3.0h));
    output.depth = PixelEyeDepth(input.positionCS.z);
    return output;
}

// ---------------------------------------------------------------- PixelXRay (PixelArtRendererFeature)

float _PixelXRayDepthBias;

// Pixel Water surfaces (published by PixelWater): xy = min XZ, zw = max XZ; level x = surface height.
float4 _PixelWaterRects[8];
float4 _PixelWaterLevels[8];
float _PixelWaterCount;

// True when this point is under a Pixel Water surface as seen from the camera: the line from the point back to the
// camera crosses a water surface (so the water hides it, like a wall would).
bool PixelUnderWater(float3 positionWS)
{
    int count = min((int)_PixelWaterCount, 8);
    if (count <= 0)
        return false;

    float3 toCamera;
    if (unity_OrthoParams.w > 0.5)
    {
        // Orthographic (also sheared / oblique): the view direction both screen rows ignore.
        float3 v = cross(UNITY_MATRIX_P[0].xyz, UNITY_MATRIX_P[1].xyz);
        v = dot(v, v) > 1e-10 ? normalize(v) : float3(0, 0, -1);
        if (v.z > 0.0) v = -v;                       // view space looks down -z
        toCamera = -normalize(mul((float3x3)UNITY_MATRIX_I_V, v));
    }
    else
    {
        toCamera = normalize(_WorldSpaceCameraPos - positionWS);
    }
    if (toCamera.y <= 1e-3)
        return false;

    float bias = max(_PixelXRayDepthBias, 0.001);
    [loop] for (int i = 0; i < count; i++)
    {
        float level = _PixelWaterLevels[i].x;
        if (positionWS.y > level - bias)
            continue;
        float s = (level - positionWS.y) / toCamera.y;
        float2 crossing = positionWS.xz + toCamera.xz * s;
        float4 r = _PixelWaterRects[i];
        if (crossing.x >= r.x && crossing.x <= r.z && crossing.y >= r.y && crossing.y <= r.w)
            return true;
    }
    return false;
}

struct PixelXRayOutput
{
    half4 state : SV_Target0;   // rgb = x-ray outline colour, a = 1 hidden here / 0.5 visible here
    half4 fill : SV_Target1;    // rgb = fill colour (x-ray colour, or the sprite's own pixels), a = fill amount
    half4 style : SV_Target2;   // r = outline opacity, g = outline width / 3, b = solid fill (1) or dither (0)
};

PixelXRayOutput FragXRay(PixelDepthVaryings input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    clip(_XRay - 0.5h);
    PixelClipCoverage(input);

    float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
    float sceneEye = PixelEyeDepth(SampleSceneDepth(screenUV));
    float selfEye = PixelEyeDepth(input.positionCS.z);
    bool hidden = sceneEye < selfEye - max(_PixelXRayDepthBias, 0.001) || PixelUnderWater(input.positionWS);

    half3 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rgb * input.color.rgb * _BaseColor.rgb;
    half3 fillColor = lerp(_XRayColor.rgb, texel * _XRayColor.rgb * 1.6h, _XRaySpriteDetail);
    half pulse = 1.0h - _XRayPulse * (0.5h + 0.5h * sin(_Time.y * _XRayPulseSpeed));

    PixelXRayOutput output;
    output.state = half4(_XRayColor.rgb, hidden ? 1.0h : 0.5h);
    output.fill = half4(saturate(fillColor), saturate(_XRayFill * pulse));
    output.style = half4(saturate(_XRayOutlineOpacity * pulse), saturate(round(_XRayOutlineWidth) / 3.0h),
                         _XRayFillStyle > 0.5h ? 1.0h : 0.0h, 0.0h);
    return output;
}

#endif // RYTHMRPG_PIXEL_ART_PASSES_INCLUDED
