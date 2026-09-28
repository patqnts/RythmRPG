// Pixel-art shader for 3D meshes (walls, floors, props) with pixel-art textures. Same pixel-stepped "light buffer"
// lighting as RythmRPG/Pixel Sprite, using the mesh normals, plus the optional pixel outline and x-ray outline drawn
// by PixelArtRendererFeature. Shared code: PixelArtCommon.hlsl, PixelArtPasses.hlsl. Inspector: PixelArtShaderGUI.
Shader "RythmRPG/Pixel Mesh"
{
    Properties
    {
        [Header(Surface)]
        [MainTexture] _MainTex ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        [Enum(Cutout, 0, Transparent, 1)] _Surface ("Surface", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [ToggleUI] _DitherFade ("Dither Fade (Cutout: tint alpha dissolves in pixels)", Float) = 0

        [Header(Lighting)]
        [Toggle(_PIXEL_LIT)] _Lit ("Lit", Float) = 1
        _LightBands ("Light Steps (0 = smooth)", Range(0, 8)) = 4
        _LightDither ("Step Dither", Range(0, 1)) = 0.5
        [ToggleUI] _TexelSnap ("Light Per Art Pixel", Float) = 1
        [ToggleUI] _SceneAmbient ("Use Scene Ambient", Float) = 1
        _AmbientColor ("Ambient Colour (when not scene)", Color) = (0.32, 0.34, 0.45, 1)
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 1
        _NormalInfluence ("Normal Influence", Range(0, 1)) = 1
        _Wrap ("Light Wrap", Range(0, 1)) = 0.5
        [ToggleUI] _UseNormalMap ("Use Normal Map", Float) = 0
        [Normal] _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0, 2)) = 1
        _ShadowStrength ("Sun Shadow Strength", Range(0, 1)) = 1
        _MaxLight ("Max Brightness", Range(1, 4)) = 2

        [Header(Emission and Hit Flash)]
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 0)
        [NoScaleOffset] _EmissionMap ("Emission Mask", 2D) = "white" {}
        _FlashColor ("Flash Colour", Color) = (1, 1, 1, 1)
        _FlashAmount ("Flash Amount", Range(0, 1)) = 0

        [Header(Pixel Outline)]
        [ToggleUI] _Outline ("Pixel Outline", Float) = 0
        _OutlineColor ("Outline Colour", Color) = (0.06, 0.04, 0.09, 1)
        [IntRange] _OutlineWidth ("Outline Width (pixels)", Range(1, 3)) = 1

        [Header(XRay When Hidden)]
        [ToggleUI] _XRay ("Show When Hidden (X-Ray)", Float) = 0
        _XRayColor ("X-Ray Colour", Color) = (0.45, 0.85, 1, 1)
        [IntRange] _XRayOutlineWidth ("X-Ray Outline Width (pixels, 0 = none)", Range(0, 3)) = 1
        _XRayOutlineOpacity ("X-Ray Outline Opacity", Range(0, 1)) = 1
        _XRayFill ("X-Ray Fill Amount (0 = outline only)", Range(0, 1)) = 0.35
        [Enum(Dither, 0, Solid, 1)] _XRayFillStyle ("X-Ray Fill Style", Float) = 0
        _XRaySpriteDetail ("X-Ray Show Sprite Detail", Range(0, 1)) = 0
        _XRayPulse ("X-Ray Pulse", Range(0, 1)) = 0
        _XRayPulseSpeed ("X-Ray Pulse Speed", Range(0, 10)) = 3

        [Header(Advanced)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [HideInInspector] _SrcBlend ("__src", Float) = 1
        [HideInInspector] _DstBlend ("__dst", Float) = 0
        [HideInInspector] _ZWrite ("__zw", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "PixelForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VertForward
            #pragma fragment FragForward
            #pragma shader_feature_local _PIXEL_LIT
            #pragma shader_feature_local_fragment _NORMALMAP
            #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "PixelArtCommon.hlsl"
            #include "PixelArtPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VertShadow
            #pragma fragment FragDepthClip
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "PixelArtCommon.hlsl"
            #include "PixelArtPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VertDepth
            #pragma fragment FragDepthOnly
            #pragma multi_compile_instancing
            #include "PixelArtCommon.hlsl"
            #include "PixelArtPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VertDepth
            #pragma fragment FragDepthNormals
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing
            #include "PixelArtCommon.hlsl"
            #include "PixelArtPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "PixelOutlineMask"
            Tags { "LightMode" = "PixelOutlineMask" }
            ZWrite Off
            ZTest LEqual
            Cull [_Cull]
            Blend Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VertDepth
            #pragma fragment FragOutlineMask
            #pragma multi_compile_instancing
            #include "PixelArtCommon.hlsl"
            #include "PixelArtPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "PixelXRay"
            Tags { "LightMode" = "PixelXRay" }
            ZWrite Off
            ZTest Always
            Cull [_Cull]
            Blend Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VertDepth
            #pragma fragment FragXRay
            #pragma multi_compile_instancing
            #include "PixelArtCommon.hlsl"
            #include "PixelArtPasses.hlsl"
            ENDHLSL
        }
    }

    Fallback Off
    CustomEditor "PixelArtShaderGUI"
}
