Shader "Hidden/PixelMetaballParticles/MergedOutlineComposite"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.035,0.025,0.06,1)
        _OutlinePixels ("Outline Pixels", Float) = 0.8
        _MetaballThreshold ("Metaball Threshold", Float) = 0.35
        _OutlineSoftness ("Outline Softness", Float) = 0.05
        _DebugMode ("Debug Mode", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
        }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "Composite"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X(_MergedParticleMask);

            float4 _OutlineColor;
            float4 _MaskTexelSize;
            float _OutlinePixels;
            float _MetaballThreshold;
            float _OutlineSoftness;
            float _DebugMode;

            float ReadField(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(
                    _MergedParticleMask,
                    sampler_LinearClamp,
                    saturate(uv)).r;
            }

            // Smooth threshold for the visible edge. This preserves the metaball
            // shape while giving boundary pixels partial coverage instead of a
            // hard binary staircase.
            float ThresholdSmooth(float value)
            {
                float softness = max(_OutlineSoftness, 0.0001);

                return smoothstep(
                    _MetaballThreshold - softness,
                    _MetaballThreshold + softness,
                    value);
            }

            float ExpandedField(float2 uv, float radiusPixels)
            {
                float2 px = _MaskTexelSize.xy * radiusPixels;

                // Normalized circular directions.
                const float d = 0.70710678;

                float value = ReadField(uv);

                // Cardinal directions.
                value = max(value, ReadField(uv + float2( px.x, 0.0)));
                value = max(value, ReadField(uv + float2(-px.x, 0.0)));
                value = max(value, ReadField(uv + float2(0.0,  px.y)));
                value = max(value, ReadField(uv + float2(0.0, -px.y)));

                // Diagonals use a normalized radius instead of a full square
                // corner offset. This is what removes the chunky 3x3 look.
                value = max(value, ReadField(uv + float2( px.x * d,  px.y * d)));
                value = max(value, ReadField(uv + float2(-px.x * d,  px.y * d)));
                value = max(value, ReadField(uv + float2( px.x * d, -px.y * d)));
                value = max(value, ReadField(uv + float2(-px.x * d, -px.y * d)));

                // Extra directions improve roundness without forcing a square kernel.
                const float a = 0.92387953; // cos 22.5
                const float b = 0.38268343; // sin 22.5

                value = max(value, ReadField(uv + float2( px.x * a,  px.y * b)));
                value = max(value, ReadField(uv + float2( px.x * b,  px.y * a)));
                value = max(value, ReadField(uv + float2(-px.x * b,  px.y * a)));
                value = max(value, ReadField(uv + float2(-px.x * a,  px.y * b)));
                value = max(value, ReadField(uv + float2(-px.x * a, -px.y * b)));
                value = max(value, ReadField(uv + float2(-px.x * b, -px.y * a)));
                value = max(value, ReadField(uv + float2( px.x * b, -px.y * a)));
                value = max(value, ReadField(uv + float2( px.x * a, -px.y * b)));

                return value;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                half4 scene =
                    SAMPLE_TEXTURE2D_X(
                        _BlitTexture,
                        sampler_LinearClamp,
                        uv);

                float rawField = ReadField(uv);

                // Keep debug threshold binary so the actual merged silhouette
                // remains easy to inspect.
                float binaryBlob =
                    step(_MetaballThreshold, rawField);

                // NORMAL edge uses smooth coverage.
                float blob =
                    ThresholdSmooth(rawField);

                float radius =
                    max(_OutlinePixels, 0.01);

                float expandedField =
                    ExpandedField(uv, radius);

                float expanded =
                    ThresholdSmooth(expandedField);

                // Fractional coverage produces a visually smoother 1px outline.
                float outline =
                    saturate(expanded - blob);

                // --------------------------------------------------------
                // DEBUG VIEWS
                // --------------------------------------------------------

                // 1 = additive scalar field
                if (_DebugMode > 0.5 && _DebugMode < 1.5)
                    return half4(rawField.xxx, 1.0);

                // 2 = thresholded merged/metaball silhouette
                if (_DebugMode >= 1.5 && _DebugMode < 2.5)
                    return half4(binaryBlob.xxx, 1.0);

                // 3 = final outline only
                if (_DebugMode >= 2.5)
                    return half4(
                        _OutlineColor.rgb * outline,
                        1.0);

                // --------------------------------------------------------
                // NORMAL COMPOSITE
                // --------------------------------------------------------

                scene.rgb =
                    lerp(
                        scene.rgb,
                        _OutlineColor.rgb,
                        outline * _OutlineColor.a);

                return scene;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
