Shader "ReactiveSnow/SnowSurfaceState"
{
    Properties
    {
        [Header(Snow)]
        _SnowColor("Snow Color", Color) = (1, 1, 1, 1)
        _SnowHeight("Base Height", Range(0, 1)) = 0.15
        _MinimumHeight("Minimum Height", Range(0, 1)) = 0.02
        _HeightVariation("Noise Height", Range(0, 2)) = 1.2
        _HeightContrast("Height Diversity", Range(0.25, 3)) = 1.6
        _MacroScale("Macro Scale", Range(0.005, 0.5)) = 0.03
        _MediumScale("Medium Scale", Range(0.02, 1)) = 0.085
        _DetailScale("Detail Scale", Range(0.1, 4)) = 0.45
        _DetailHeight("Detail Height", Range(0, 0.05)) = 0.003
        _AmplitudeScale("Amplitude Scale", Range(0.005, 0.25)) = 0.012
        _AmplitudeVariation("Amplitude Variation", Range(0, 1)) = 0.65
        _WarpScale("Warp Scale", Range(0.005, 0.25)) = 0.018
        _WarpStrength("Warp Strength", Range(0, 12)) = 5
        _NormalStrength("Normal Strength", Range(0, 1)) = 0.9
        _ShadowColor("Shadow Color", Color) = (0.72, 0.78, 0.86, 1)
        [Header(Sparkle)]
        [NoScaleOffset] _SparkleNoise("Sparkle Noise", 2D) = "black" {}
        _SparkleScale("Sparkle Scale", Range(0.5, 20)) = 3
        _SparkleThreshold("Sparkle Threshold", Range(0.5, 0.999)) = 0.65
        _SparkleStrength("Sparkle Strength", Range(0, 4)) = 0.9
        _SparkleNear("Sparkle Full Distance", Range(0, 20)) = 1.5
        _SparkleFar("Sparkle Fade Distance", Range(1, 40)) = 8
        [Header(Trail)]
        _TrailColor("Trail Color", Color) = (0.72, 0.76, 0.82, 1)
        _FootprintSmoothness("Trail Smoothness", Range(0, 1)) = 0.5
        _DebugFootprint("Debug Trail", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_SparkleNoise);
            SAMPLER(sampler_SparkleNoise);
            TEXTURE2D_ARRAY(_SnowState);
            SAMPLER(sampler_SnowState);
            Texture2D<float> _PageSliceMap;
            float4 _SurfaceMin;
            float4 _SurfaceMax;
            float4 _LocalPageSize;
            int _PageCountX;
            int _PageCountZ;
            float _SnowStateResolution;
            float4 _SnowInteractorPosition;

            CBUFFER_START(UnityPerMaterial)
                float4 _SnowColor;
                float4 _ShadowColor;
                float4 _TrailColor;
                float _SnowHeight;
                float _MinimumHeight;
                float _HeightVariation;
                float _HeightContrast;
                float _MacroScale;
                float _MediumScale;
                float _DetailScale;
                float _DetailHeight;
                float _AmplitudeScale;
                float _AmplitudeVariation;
                float _WarpScale;
                float _WarpStrength;
                float _NormalStrength;
                float _SparkleScale;
                float _SparkleThreshold;
                float _SparkleStrength;
                float _SparkleNear;
                float _SparkleFar;
                float _FootprintSmoothness;
                float _DebugFootprint;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 baseNormalWS : TEXCOORD1;
                float2 localXZ : TEXCOORD2;
                float2 worldXZ : TEXCOORD3;
                float surfaceNoise : TEXCOORD4;
            };

            float2 RotateUV(float2 value, float2 direction)
            {
                return float2(value.x * direction.x - value.y * direction.y, value.x * direction.y + value.y * direction.x);
            }

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 345.45));
                value += dot(value, value + 34.345);
                return frac(value.x * value.y);
            }

            float ValueNoise(float2 position)
            {
                float2 cell = floor(position);
                float2 local = frac(position);
                local = local * local * local * (local * (local * 6.0 - 15.0) + 10.0);
                float bottom = lerp(Hash21(cell), Hash21(cell + float2(1, 0)), local.x);
                float top = lerp(Hash21(cell + float2(0, 1)), Hash21(cell + 1.0), local.x);
                return lerp(bottom, top, local.y);
            }

            float Fbm3(float2 position)
            {
                float value = ValueNoise(position) * 0.5714;
                position = RotateUV(position, float2(0.7986, 0.6018)) * 2.03 + float2(17.13, 9.71);
                value += ValueNoise(position) * 0.2857;
                position = RotateUV(position, float2(0.5446, -0.8387)) * 2.01 + float2(5.37, 21.19);
                value += ValueNoise(position) * 0.1429;
                return value;
            }

            float4 EvaluateSnowShape(float2 worldXZ)
            {
                float2 warp = float2(ValueNoise(worldXZ * _WarpScale + float2(3.17, 7.31)), ValueNoise(worldXZ * _WarpScale + float2(11.61, 2.29)));
                float2 warpedXZ = worldXZ + (warp * 2.0 - 1.0) * _WarpStrength;
                float macro = Fbm3(warpedXZ * _MacroScale);
                float medium = Fbm3(RotateUV(warpedXZ, float2(0.7986, 0.6018)) * _MediumScale + float2(13.37, 7.19));
                float detail = ValueNoise(RotateUV(warpedXZ, float2(0.5446, -0.8387)) * _DetailScale + float2(31.71, 19.43));
                float amplitudeNoise = Fbm3(worldXZ * _AmplitudeScale + float2(47.17, 23.73));
                float heightNoise = macro * 0.62 + medium * 0.25 + amplitudeNoise * 0.13;
                heightNoise = saturate((heightNoise - 0.5) * _HeightContrast + 0.5);
                heightNoise = heightNoise * heightNoise * (3.0 - 2.0 * heightNoise);
                float regionalVariation = lerp(1.0 - _AmplitudeVariation * 0.5, 1.0, amplitudeNoise);
                float accumulatedHeight = heightNoise * regionalVariation * _HeightVariation;
                float height = max(_MinimumHeight, _SnowHeight + accumulatedHeight + (detail * 2.0 - 1.0) * _DetailHeight);
                return float4(height, macro, medium, detail);
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

            float SampleDepression(float2 localXZ) { return saturate(SampleSnowState(localXZ).r); }

            float SampleSmoothDepression(float2 localXZ)
            {
                float2 stepSize = _LocalPageSize.xy / max(_SnowStateResolution, 1.0) * lerp(1.0, 2.0, _FootprintSmoothness);
                float value = SampleDepression(localXZ) * 0.25;
                value += SampleDepression(localXZ + float2( stepSize.x, 0)) * 0.125;
                value += SampleDepression(localXZ + float2(-stepSize.x, 0)) * 0.125;
                value += SampleDepression(localXZ + float2(0,  stepSize.y)) * 0.125;
                value += SampleDepression(localXZ + float2(0, -stepSize.y)) * 0.125;
                value += SampleDepression(localXZ + float2( stepSize.x,  stepSize.y)) * 0.0625;
                value += SampleDepression(localXZ + float2(-stepSize.x,  stepSize.y)) * 0.0625;
                value += SampleDepression(localXZ + float2( stepSize.x, -stepSize.y)) * 0.0625;
                value += SampleDepression(localXZ + float2(-stepSize.x, -stepSize.y)) * 0.0625;
                return value;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 basePositionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float depression = SampleSmoothDepression(input.positionOS.xz);
                float4 snowShape = EvaluateSnowShape(basePositionWS.xz);
                float displacement = (1.0 - depression) * snowShape.x;
                float3 positionWS = basePositionWS + normalWS * displacement;
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.baseNormalWS = normalWS;
                output.localXZ = input.positionOS.xz;
                output.worldXZ = basePositionWS.xz;
                output.surfaceNoise = dot(snowShape.yzw, float3(0.55, 0.3, 0.15));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float depression = SampleSmoothDepression(input.localXZ);
                if (_DebugFootprint > 0.5) return half4(depression.xxx, 1);

                float3 baseNormalWS = normalize(input.baseNormalWS);
                float3 geometricNormalWS = normalize(cross(ddy(input.positionWS), ddx(input.positionWS)));
                if (dot(geometricNormalWS, baseNormalWS) < 0.0) geometricNormalWS *= -1.0;
                float3 normalWS = normalize(lerp(baseNormalWS, geometricNormalWS, _NormalStrength));
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float direct = saturate(dot(normalWS, mainLight.direction)) * mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                float3 lightColor = lerp(_ShadowColor.rgb, mainLight.color, direct);

                #if defined(_ADDITIONAL_LIGHTS)
                uint lightCount = GetAdditionalLightsCount();
                for (uint i = 0u; i < lightCount; i++)
                {
                    Light light = GetAdditionalLight(i, input.positionWS);
                    float amount = saturate(dot(normalWS, light.direction)) * light.shadowAttenuation * light.distanceAttenuation;
                    lightColor += light.color * amount;
                }
                #endif

                float surfaceTone = lerp(0.82, 1.0, input.surfaceNoise);
                float3 snow = _SnowColor.rgb * surfaceTone * lightColor;
                float3 color = lerp(snow, _TrailColor.rgb * lightColor, depression);
                float sparkleNoise = SAMPLE_TEXTURE2D_LOD(_SparkleNoise, sampler_SparkleNoise, input.worldXZ * _SparkleScale, 0).r;
                float playerDistance = distance(input.positionWS.xz, _SnowInteractorPosition.xz);
                float sparkleRange = 1.0 - smoothstep(_SparkleNear, max(_SparkleFar, _SparkleNear + 0.01), playerDistance);
                float sparkle = step(_SparkleThreshold, sparkleNoise) * _SparkleStrength * sparkleRange * (1.0 - depression);
                return half4(color + sparkle, 1);
            }
            ENDHLSL
        }
    }
}
