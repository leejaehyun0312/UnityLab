Shader "WorldView/GlassWorld"
{
    Properties
    {
        _WorldCube("World Cube", Cube) = "" {}

        [HDR]_GlassColor("Glass Tint", Color) = (0.08, 0.16, 0.20, 1)
        [HDR]_EdgeColor("Edge Color", Color) = (0.30, 0.60, 0.75, 1)

        _GlassOpacity("Glass Opacity", Range(0, 0.5)) = 0.12
        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 4
        _FresnelStrength("Fresnel Strength", Range(0, 0.6)) = 0.18
        _SurfaceScale("Surface Scale", Range(1, 30)) = 8
        _HighlightStrength("Highlight Strength", Range(0, 0.3)) = 0.05
        _DirtStrength("Dirt Strength", Range(0, 0.05)) = 0.01
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Transparent"
            "RenderType"="Transparent"
        }

        Pass
        {
            Name "World"
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex VertWorld
            #pragma fragment FragWorld
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURECUBE(_WorldCube);
            SAMPLER(sampler_WorldCube);

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings VertWorld(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 FragWorld(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.positionWS - GetCameraPositionWS());
                return SAMPLE_TEXTURECUBE(_WorldCube, sampler_WorldCube, direction);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Glass"
            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex VertGlass
            #pragma fragment FragGlass
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _GlassColor;
                float4 _EdgeColor;
                float _GlassOpacity;
                float _FresnelPower;
                float _FresnelStrength;
                float _SurfaceScale;
                float _HighlightStrength;
                float _DirtStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 34.45);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);

                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));

                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            Varyings VertGlass(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }

            half4 FragGlass(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDir = normalize(GetCameraPositionWS() - input.positionWS);
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), _FresnelPower);

                float surfaceNoise = Noise(input.uv * _SurfaceScale + 17.3);
                float highlight = smoothstep(0.65, 0.88, surfaceNoise) * _HighlightStrength;
                float dirt = smoothstep(0.91, 0.98, surfaceNoise) * _DirtStrength;

                float alpha = saturate(_GlassOpacity + fresnel * _FresnelStrength + highlight * 0.35 + dirt);
                float3 color = lerp(_GlassColor.rgb, _EdgeColor.rgb, fresnel);
                color += highlight + dirt * 0.15;

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
