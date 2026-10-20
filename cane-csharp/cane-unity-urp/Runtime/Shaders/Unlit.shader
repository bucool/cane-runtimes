Shader "Cane/URP/Unlit" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        _Pma ("Premultiplied texture", Float) = 0
        [ToggleUI] _Effects ("Material effects", Float) = 0
        _Hue ("Hue (degrees)", Range(-180,180)) = 0
        _Saturation ("Saturation", Range(0,2)) = 1
        _Brightness ("Brightness", Range(0,4)) = 1
        _SecondaryTex ("Secondary albedo", 2D) = "white" {}
        _SecondaryColor ("Secondary tint (alpha = strength)", Color) = (1,1,1,0)
        [HideInInspector] _RampStrength ("Ramp strength", Float) = 0
        [HideInInspector] _RimColor ("Rim color", Color) = (0,0,0,0)
        [HideInInspector] _RimPower ("Rim power", Float) = 3
        _Grayscale ("Grayscale", Range(0,1)) = 0
        _FillColor ("Fill color (alpha = strength)", Color) = (1,1,1,0)
        _OutlineColor ("Outline color", Color) = (0,0,0,1)
        _OutlineWidth ("Outline width", Range(0,8)) = 0
        [Toggle] _OutlineScreenSpace ("Outline width in screen pixels", Float) = 0
        [Toggle] _OutlineOnly ("Outline only", Float) = 0
        _OutlineUvRect ("Outline atlas bounds (min/max UV)", Vector) = (0,0,1,1)
        [HideInInspector] _Sampling ("Sampling", Vector) = (0,0,-1,0)
        // Keep one material layout for the shared lighting/shadow/depth passes.
        [HideInInspector] _BumpScale ("Normal strength", Float) = 1
        [HideInInspector] _Metallic ("Metallic", Float) = 0
        [HideInInspector] _Glossiness ("Smoothness", Float) = .5
        [HideInInspector] _EmissionColor ("Emission color", Color) = (0,0,0,1)
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
            Name "Unlit"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex CaneVertex
            #pragma fragment CaneUnlit
            ENDHLSL
        }
        Pass {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual Blend Off ColorMask 0
            Stencil { Comp Always Pass Keep }
            HLSLPROGRAM
            #pragma vertex CaneShadow
            #pragma fragment CaneDepth
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection, _LightPosition;
            CaneVaryings CaneShadow(CaneAttributes input) {
                CaneVaryings output = CaneVertex(input);
                #ifdef _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 direction = normalize(_LightPosition - output.world);
                #else
                float3 direction = _LightDirection;
                #endif
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(output.world, output.normal, direction)));
                return output;
            }
            ENDHLSL
        }
        Pass {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On Blend Off ColorMask R
            Stencil { Comp Always Pass Keep }
            HLSLPROGRAM
            #pragma vertex CaneVertex
            #pragma fragment CaneDepth
            ENDHLSL
        }
    }
}
