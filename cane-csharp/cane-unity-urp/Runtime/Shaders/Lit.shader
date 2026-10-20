Shader "Cane/URP/Lit" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        _Pma ("Premultiplied texture", Float) = 0
        [ToggleUI] _Effects ("Material effects", Float) = 0
        _Hue ("Hue (degrees)", Range(-180,180)) = 0
        _Saturation ("Saturation", Range(0,2)) = 1
        _Brightness ("Brightness", Range(0,4)) = 1
        _SecondaryTex ("Secondary albedo", 2D) = "white" {}
        _SecondaryColor ("Secondary tint (alpha = strength)", Color) = (1,1,1,0)
        _LightRamp ("Diffuse light ramp", 2D) = "white" {}
        _RampStrength ("Ramp strength", Range(0,1)) = 0
        [HDR] _RimColor ("Rim color (alpha = strength)", Color) = (0,0,0,0)
        _RimPower ("Rim power", Range(.1,16)) = 3
        _Grayscale ("Grayscale", Range(0,1)) = 0
        _FillColor ("Fill color (alpha = strength)", Color) = (1,1,1,0)
        _OutlineColor ("Outline color", Color) = (0,0,0,1)
        _OutlineWidth ("Outline width", Range(0,8)) = 0
        [Toggle] _OutlineScreenSpace ("Outline width in screen pixels", Float) = 0
        [Toggle] _OutlineOnly ("Outline only", Float) = 0
        _OutlineUvRect ("Outline atlas bounds (min/max UV)", Vector) = (0,0,1,1)
        [HideInInspector] _Sampling ("Sampling", Vector) = (0,0,-1,0)
        _Cutoff ("Shadow/depth cutoff", Range(0,1)) = .1
        [Toggle] _ZWrite ("Depth write", Float) = 0
        [HideInInspector] _CaneSlotAlpha ("Slot opacity", Float) = 1
        [HideInInspector] _CaneClipCount ("Clip vertices", Integer) = -1
        [HideInInspector] _SrcBlend ("Source blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination blend", Float) = 10
        [HideInInspector] _DstBlendAlpha ("Destination alpha blend", Float) = 10
        [HideInInspector] _StencilComp ("Stencil comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil write mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil read mask", Float) = 255
        [Normal] _BumpMap ("Normal map", 2D) = "bump" {}
        _BumpScale ("Normal strength", Range(0,2)) = 1
        _MetallicGlossMap ("Metallic (R), smoothness (A)", 2D) = "white" {}
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = .5
        _EmissionMap ("Emission", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission color", Color) = (0,0,0,1)
    }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite [_ZWrite]
        Blend [_SrcBlend] [_DstBlend], One [_DstBlendAlpha]
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        HLSLINCLUDE
        #pragma target 3.5
        #pragma target 4.5 CANE_SLOT_OBJECT
        #pragma multi_compile_local _ CANE_SLOT_OBJECT
        #pragma multi_compile_local _ CANE_SAMPLE_CONTROL
        #pragma multi_compile_instancing
        #include "CaneInput.hlsl"
        ENDHLSL
        Pass {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma vertex LitVertex
            #pragma fragment LitFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            half3 CaneRampLight(Light light, float3 normal, uint layers, bool vertexLight = false) {
                #ifdef _LIGHT_LAYERS
                if (!IsMatchingLightLayer(light.layerMask, layers)) return 0;
                #endif
                float cosine = saturate(dot(normal, light.direction));
                float3 delta = CaneLightRamp(cosine) - (vertexLight ? 0 : cosine);
                return delta * light.color * (light.distanceAttenuation * light.shadowAttenuation);
            }
            half3 CaneRampedDiffuse(InputData inputData, SurfaceData surface) {
                half4 shadowMask = CalculateShadowMask(inputData);
                AmbientOcclusionFactor ao = CreateAmbientOcclusionFactor(inputData, surface);
                uint layers = GetMeshRenderingLayer();
                half3 delta = CaneRampLight(GetMainLight(inputData, shadowMask, ao), inputData.normalWS, layers);
                #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
                bool vertexLight = false;
                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                vertexLight = true; delta -= inputData.vertexLighting;
                #endif
                #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++) {
                    CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                    delta += CaneRampLight(GetAdditionalLight(lightIndex, inputData, shadowMask, ao), inputData.normalWS, layers);
                }
                #endif
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    delta += CaneRampLight(GetAdditionalLight(lightIndex, inputData, shadowMask, ao), inputData.normalWS, layers, vertexLight);
                LIGHT_LOOP_END
                #endif
                BRDFData brdf; InitializeBRDFData(surface, brdf);
                return delta * brdf.diffuse * _RampStrength;
            }
            CaneVaryings LitVertex(CaneAttributes input) {
                CaneVaryings output = CaneVertex(input);
                output.sh = SampleSHVertex(output.normal);
                output.vertexLight = VertexLighting(output.world, output.normal);
                return output;
            }
            half4 LitFragment(CaneVaryings input) : SV_Target {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float4 color = CaneSurface(input);
                InputData lighting = (InputData)0;
                lighting.positionWS = input.world; lighting.positionCS = input.positionCS;
                lighting.normalWS = CaneNormal(input);
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.world);
                lighting.shadowCoord = TransformWorldToShadowCoord(input.world);
                lighting.bakedGI = SampleSHPixel(input.sh, lighting.normalWS);
                lighting.vertexLighting = input.vertexLight;
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = unity_ProbesOcclusion;
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = color.rgb; surface.alpha = 1; surface.occlusion = 1;
                half4 metal = tex2D(_MetallicGlossMap, input.uv);
                surface.metallic = metal.r * _Metallic; surface.smoothness = metal.a * _Glossiness;
                surface.emission = tex2D(_EmissionMap, input.uv).rgb * _EmissionColor.rgb;
                half4 result = UniversalFragmentPBR(lighting, surface);
                [branch] if (_Effects != 0) {
                    if (_RampStrength > 0) result.rgb += CaneRampedDiffuse(lighting, surface);
                    if (_RimColor.a > 0) result.rgb += CaneRim(lighting.normalWS, lighting.viewDirectionWS);
                }
                result.rgb = MixFog(result.rgb, InitializeInputDataFog(float4(input.world, 1), input.fog)) * color.a;
                result.a = color.a; return result;
            }
            ENDHLSL
        }
        UsePass "Cane/URP/Unlit/ShadowCaster"
        UsePass "Cane/URP/Unlit/DepthOnly"
        Pass {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On Blend Off
            Stencil { Comp Always Pass Keep }
            HLSLPROGRAM
            #pragma vertex CaneVertex
            #pragma fragment DepthNormals
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 DepthNormals(CaneVaryings input) : SV_Target {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                clip(CaneSurface(input, false).a - _Cutoff);
                float3 normal = CaneNormal(input);
                #ifdef _GBUFFER_NORMALS_OCT
                return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(normal) * .5 + .5)), 0);
                #else
                return half4(normal, 0);
                #endif
            }
            ENDHLSL
        }
    }
}
