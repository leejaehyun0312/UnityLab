Shader "ReactiveSnow/SnowSurfaceState"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.93, 0.95, 0.98, 1)
        _FootprintColor ("Footprint Color", Color) = (0.76, 0.80, 0.85, 1)
        _FootprintStrength ("Footprint Strength", Range(0, 1)) = 0.45
        _VisualDepth ("Footprint Depth", Range(0, 0.05)) = 0.015
        _NormalStrength ("Depth Normal", Range(0, 20)) = 8
        _ParallaxStrength ("Parallax Strength", Range(0, 2)) = 1
        _CompressionDarken ("Compression Darken", Range(0, 0.5)) = 0.06
        _DepthDarken ("Depth Darken", Range(0, 0.5)) = 0.12
        _MacroScale ("Macro Scale", Range(0.1, 10)) = 1.2
        _MacroStrength ("Macro Strength", Range(0, 0.2)) = 0.025
        _MicroScale ("Micro Scale", Range(1, 100)) = 25
        _MicroStrength ("Micro Strength", Range(0, 0.2)) = 0.012
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D_ARRAY(_SnowState);
            SAMPLER(sampler_SnowState);
            Texture2D<float> _PageSliceMap;

            float4 _SurfaceMin;
            float4 _SurfaceMax;
            float4 _LocalPageSize;
            int _PageCountX;
            int _PageCountZ;
            float _SnowStateResolution;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _FootprintColor;
                float _FootprintStrength;
                float _VisualDepth;
                float _NormalStrength;
                float _ParallaxStrength;
                float _CompressionDarken;
                float _DepthDarken;
                float _MacroScale;
                float _MacroStrength;
                float _MicroScale;
                float _MicroStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float2 localXZ : TEXCOORD2;
            };

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = Hash(i);
                float b = Hash(i + float2(1, 0));
                float c = Hash(i + float2(0, 1));
                float d = Hash(i + float2(1, 1));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float4 SampleSnowState(float2 localXZ)
            {
                float2 pageSize = _LocalPageSize.xy;
                int2 page = (int2)floor((localXZ - _SurfaceMin.xy) / pageSize);
                page = clamp(page, int2(0, 0), int2(_PageCountX - 1, _PageCountZ - 1));

                int slice = (int)round(_PageSliceMap.Load(int3(page, 0)));
                if (slice < 0) return 0;

                float2 pageMin = _SurfaceMin.xy + page * pageSize;
                float2 pageMax = min(pageMin + pageSize, _SurfaceMax.xy);
                float2 uv = saturate((localXZ - pageMin) / max(pageMax - pageMin, 0.0001));

                return SAMPLE_TEXTURE2D_ARRAY_LOD(_SnowState, sampler_SnowState, uv, slice, 0);
            }

            float SampleDepth(float2 localXZ)
            {
                return SampleSnowState(localXZ).r * _VisualDepth;
            }

            float2 ApplyParallax(float2 localXZ, float3 positionOS)
            {
                float depression = SampleSnowState(localXZ).r;
                if (depression <= 0.001) return localXZ;

                float3 cameraOS = mul(UNITY_MATRIX_I_M, float4(GetCameraPositionWS(), 1)).xyz;
                float3 viewDir = normalize(cameraOS - positionOS);
                float vertical = max(abs(viewDir.y), 0.15);

                float2 offset = -(viewDir.xz / vertical) * depression * _VisualDepth * _ParallaxStrength;
                return localXZ + offset;
            }

            float3 GetSnowNormal(float2 localXZ)
            {
                float2 stepSize = _LocalPageSize.xy / max(_SnowStateResolution, 1.0);

                float left = SampleDepth(localXZ - float2(stepSize.x, 0));
                float right = SampleDepth(localXZ + float2(stepSize.x, 0));
                float down = SampleDepth(localXZ - float2(0, stepSize.y));
                float up = SampleDepth(localXZ + float2(0, stepSize.y));

                float dx = (right - left) * _NormalStrength;
                float dz = (up - down) * _NormalStrength;

                return normalize(float3(dx, 1, dz));
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                output.localXZ = input.positionOS.xz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 sampleXZ = ApplyParallax(input.localXZ, input.positionOS);
                float4 state = SampleSnowState(sampleXZ);

                float depression = state.r;
                float compression = state.g;
                float footprint = saturate(max(depression, compression));

                float macroNoise = Noise(input.positionWS.xz * _MacroScale);
                float microNoise = Noise(input.positionWS.xz * _MicroScale);
                float surfaceVariation = (macroNoise - 0.5) * _MacroStrength + (microNoise - 0.5) * _MicroStrength;

                float3 color = lerp(_BaseColor.rgb, _FootprintColor.rgb, footprint * _FootprintStrength);
                color += surfaceVariation;
                color *= 1.0 - compression * _CompressionDarken;
                color *= 1.0 - depression * _DepthDarken;

                float3 normalWS = normalize(TransformObjectToWorldNormal(GetSnowNormal(sampleXZ)));
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));

                float NdotL = saturate(dot(normalWS, mainLight.direction));
                float diffuse = 0.55 + NdotL * 0.45;

                return half4(color * diffuse, 1);
            }
            ENDHLSL
        }
    }
}