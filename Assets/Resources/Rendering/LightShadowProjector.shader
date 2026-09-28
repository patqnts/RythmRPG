// Projected light / shadow for LightShadowProjector.cs (window light, blinds, leaf dapple, cloud shadows, the shadow of a
// dragon flying overhead...).
//
// The mesh is the projector's box volume. Each pixel it covers reads the scene depth, rebuilds the world position of
// whatever is already drawn there (floor, walls, props, grass, characters: anything that writes depth), moves it into
// the projector's box, and samples the cookie image there. So the pattern lands on every opaque surface in the volume
// and follows its shape (a shadow climbs a wall, a character walking through window light gets lit).
//
// Blending
//   Shadow / Light (Multiply): "2x multiply" (Blend DstColor SrcColor, result = 2 * src * dst). The shader outputs
//   factor * 0.5, so 0.5 leaves the scene unchanged, lower darkens (tinted), higher brightens.
//   Light (Additive): Blend One One.
//
// Pixel art: the image-plane position is snapped to the world pixel grid (Pixels Per Unit), the cookie is point
// sampled, and the result is posterised into bands with ordered (Bayer) dithering in the projector's own pixel grid,
// so the pattern steps on whole pixels and does not crawl when the camera moves.
Shader "Hidden/RythmRPG/LightShadowProjector"
{
    Properties
    {
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 2
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 3
        _MainTex ("Cookie", 2D) = "white" {}
        _SpriteRect ("Sprite Rect (uv offset xy, uv size zw)", Vector) = (0, 0, 1, 1)
        [HDR] _Color ("Colour (shadow tint / light colour)", Color) = (0.25, 0.27, 0.4, 1)
        _Params ("Strength, Mode (0 shadow 1 light mul 2 light add), Channel, Invert", Vector) = (0.75, 0, 0, 0)
        _UVTransform ("Tiling XY, Rotation (rad), Repeat", Vector) = (1, 1, 0, 0)
        _UVAnim ("Scroll XY, Sway, Sway Speed", Vector) = (0, 0, 0, 1)
        _Look ("Softness (image uv), Bands, Dither, Pixels Per Unit", Vector) = (0, 3, 1, 32)
        _Fade ("Near, Far, Edge, Facing", Vector) = (0.05, 0.25, 0.05, 0)
        _Flicker ("Amount, Speed, Seed, Point Sample", Vector) = (0, 3, 0, 1)
        _Flip ("Flip X (+-1), Flip Y (+-1), Size X, Size Y (world)", Vector) = (1, 1, 2, 2)
        _ProjDir ("Projection direction (world)", Vector) = (0, -1, 0, 0)
        _ProjRow0 ("World to box row 0", Vector) = (1, 0, 0, 0)
        _ProjRow1 ("World to box row 1", Vector) = (0, 1, 0, 0)
        _ProjRow2 ("World to box row 2", Vector) = (0, 0, 1, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            // First thing in the transparent pass: after the depth copy, before particles, sprites and god rays.
            "Queue" = "Transparent-499"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "DisableBatching" = "True"
        }

        Pass
        {
            Name "LightShadowProjector"
            Tags { "LightMode" = "UniversalForward" }
            // Colour: the chosen blend. Alpha: keep what is there.
            Blend [_SrcBlend] [_DstBlend], Zero One
            ZWrite Off
            // Back faces with no depth test: works with the camera inside the box too. Pixels whose scene point
            // is outside the box are clipped in the fragment shader.
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            // multi_compile (not shader_feature): the material is made at runtime, so every variant must be in builds.
            #pragma multi_compile_local _ _RESPECT_SUN_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #if defined(_RESPECT_SUN_SHADOWS)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #endif

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_lsp_point_clamp);
            SAMPLER(sampler_lsp_linear_clamp);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;
                float4 _SpriteRect;
                half4 _Color;
                float4 _Params;
                float4 _UVTransform;
                float4 _UVAnim;
                float4 _Look;
                float4 _Fade;
                float4 _Flicker;
                float4 _Flip;
                float4 _ProjDir;
                float4 _ProjRow0;
                float4 _ProjRow1;
                float4 _ProjRow2;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            // 4x4 Bayer threshold (0..1).
            float Bayer4(float2 pixel)
            {
                uint2 q = uint2(floor(pixel)) % 4u;
                static const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
                return (m[q.y * 4u + q.x] + 0.5) / 16.0;
            }

            // Image-plane position (0..1 across the box) -> cookie uv (0..1 inside the sprite), after flip, sway,
            // rotation, tiling and scroll. valid = 0 outside the image when Repeat is off.
            float2 CookieUV(float2 plane, out float valid)
            {
                float t = _Time.y;
                float2 p = (plane - 0.5) * _Flip.xy;
                if (_UVAnim.z > 0.0)
                {
                    float w = _UVAnim.w;
                    p += _UVAnim.z * float2(sin(t * w + p.y * 6.2831), cos(t * w * 0.83 + p.x * 5.1));
                }
                float s, c;
                sincos(_UVTransform.z, s, c);
                p = float2(c * p.x - s * p.y, s * p.x + c * p.y);
                p = p * _UVTransform.xy + 0.5 + _UVAnim.xy * t;

                if (_UVTransform.w > 0.5)
                {
                    p = frac(p);
                    valid = 1.0;
                }
                else
                {
                    float2 inside = step(0.0, p) * step(p, 1.0);
                    valid = inside.x * inside.y;
                    p = saturate(p);
                }
                return p;
            }

            // Coverage (x) and colour (yzw) of the cookie at one image-plane position.
            float4 SampleCookie(float2 plane, bool pointSample)
            {
                float valid;
                float2 uv = CookieUV(plane, valid);
                float2 atlas = _SpriteRect.xy + uv * _SpriteRect.zw;

                half4 c;
                if (pointSample)
                {
                    // Texel centre: crisp cookie pixels.
                    float2 size = _MainTex_TexelSize.zw;
                    atlas = (floor(atlas * size) + 0.5) / size;
                    c = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_lsp_point_clamp, atlas, 0);
                }
                else
                {
                    c = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_lsp_linear_clamp, atlas, 0);
                }

                float channel = _Params.z;
                float m;
                half3 tint = half3(1, 1, 1);
                if (channel < 0.5)      m = c.a;                                              // Alpha
                else if (channel < 1.5) m = c.r;                                              // Red
                else if (channel < 2.5) m = dot(c.rgb, half3(0.299, 0.587, 0.114)) * c.a;     // Luminance
                else { m = c.a; tint = c.rgb; }                                               // Colour + alpha
                if (_Params.w > 0.5) m = 1.0 - m;
                return float4(m * valid, tint);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // ---- Scene position under this pixel ----
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                #if UNITY_REVERSED_Z
                    float depth = SampleSceneDepth(screenUV);
                #else
                    float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, SampleSceneDepth(screenUV));
                #endif
                float3 positionWS = ComputeWorldSpacePosition(screenUV, depth, UNITY_MATRIX_I_VP);

                // ---- Into the projector box: x, y in -0.5..0.5 across the image, z 0..1 along the throw ----
                float4 p4 = float4(positionWS, 1.0);
                float3 box = float3(dot(_ProjRow0, p4), dot(_ProjRow1, p4), dot(_ProjRow2, p4));
                clip(0.5 - max(abs(box.x), abs(box.y)));
                clip(box.z);
                clip(1.0 - box.z);

                // ---- Pixel grid of the image plane ----
                float2 sizeWS = max(_Flip.zw, 1e-4);
                float ppu = _Look.w;
                float2 plane = box.xy + 0.5;
                float2 gridPixel = input.positionCS.xy;
                if (ppu > 0.0)
                {
                    float2 cells = sizeWS * ppu;
                    gridPixel = floor(plane * cells);
                    plane = (gridPixel + 0.5) / cells;
                }

                // ---- Cookie (optionally blurred) ----
                float softness = _Look.x;
                float4 cookie;
                if (softness <= 1e-5)
                {
                    cookie = SampleCookie(plane, _Flicker.w > 0.5);
                }
                else
                {
                    // Centre + 8 taps on two rings. Softness is in world units; the plane is 0..1 per axis.
                    float2 r = softness / sizeWS;
                    cookie = SampleCookie(plane, false) * 2.0;
                    float weight = 2.0;
                    [unroll] for (int i = 0; i < 8; i++)
                    {
                        float a = i * 0.785398 + 0.3927 * (i & 1);
                        float ring = (i & 1) ? 1.0 : 0.55;
                        float2 o = float2(cos(a), sin(a)) * r * ring;
                        cookie += SampleCookie(plane + o, false);
                        weight += 1.0;
                    }
                    cookie /= weight;
                }

                float mask = cookie.x;
                half3 tint = (half3)cookie.yzw;

                // ---- Fades ----
                float nearFade = _Fade.x > 1e-4 ? saturate(box.z / _Fade.x) : 1.0;
                float farFade = _Fade.y > 1e-4 ? saturate((1.0 - box.z) / _Fade.y) : 1.0;
                float edgeDist = 0.5 - max(abs(box.x), abs(box.y));
                float edgeFade = _Fade.z > 1e-4 ? saturate(edgeDist / (_Fade.z * 0.5)) : 1.0;
                mask *= nearFade * farFade * edgeFade;

                if (_Fade.w > 1e-4)
                {
                    // Flat normal from screen derivatives, turned toward the camera.
                    float3 n = normalize(cross(ddy(positionWS), ddx(positionWS)));
                    float3 toCamera = -UNITY_MATRIX_V[2].xyz;
                    n = dot(n, toCamera) < 0.0 ? -n : n;
                    float facing = saturate(dot(n, -_ProjDir.xyz));
                    mask *= lerp(1.0, saturate(facing * 3.0), _Fade.w);
                }

                // ---- Flicker ----
                if (_Flicker.x > 1e-4)
                {
                    float t = _Time.y * _Flicker.y + _Flicker.z;
                    float n = sin(t * 6.1) * 0.5 + sin(t * 2.3 + 1.7) * 0.35 + sin(t * 13.7 + 0.4) * 0.15;
                    mask *= 1.0 - _Flicker.x * (0.5 - 0.5 * n);
                }

                // ---- Strength, then the sun's own shadows ----
                mask *= _Params.x;
                #if defined(_RESPECT_SUN_SHADOWS)
                    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
                    mask *= MainLightRealtimeShadow(shadowCoord);
                #endif

                // ---- Posterise + ordered dither ----
                float bands = _Look.y;
                if (bands >= 1.0)
                {
                    float threshold = lerp(0.5, Bayer4(gridPixel), saturate(_Look.z));
                    // Bands count light steps above 0: mask 0..strength -> 0, 1/b, 2/b ... 1 of the range.
                    float range = max(abs(_Params.x), 1e-4);
                    float v = saturate(mask / range);
                    v = floor(v * bands + threshold) / bands;
                    mask = v * range;
                }
                clip(mask - 1e-4);

                // ---- Output ----
                float mode = _Params.y;
                if (mode < 0.5)
                {
                    // Shadow: multiply toward the shadow colour.
                    half3 factor = lerp(half3(1, 1, 1), _Color.rgb * tint, (half)saturate(mask));
                    return half4(factor * 0.5h, 0.5h);
                }
                if (mode < 1.5)
                {
                    // Light, multiply: brighten what is there (keeps the surface's own colour and texture).
                    half3 factor = 1.0h + _Color.rgb * tint * (half)mask;
                    return half4(factor * 0.5h, 0.5h);
                }
                // Light, additive: glow.
                return half4(_Color.rgb * tint * (half)mask, 0.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
