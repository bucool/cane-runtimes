Shader "Cane/URP/2D Lit" {
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
        [Normal] _BumpMap ("Normal map", 2D) = "bump" {}
        _BumpScale ("Normal strength", Range(0,2)) = 1
        _MaskTex ("Light blend mask", 2D) = "white" {}
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
            Name "Lit2D"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex CaneVertex
            #pragma fragment Lit2D
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/SurfaceData2D.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/InputData2D.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"
            half4 Lit2D(CaneVaryings input) : SV_Target {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float4 color = CaneSurface(input);
                SurfaceData2D surface;
                InitializeSurfaceData(color.rgb, color.a, tex2D(_MaskTex, input.uv), surface);
                InputData2D lighting;
                InitializeInputData(input.uv, GetNormalizedScreenSpaceUV(input.positionCS), lighting);
                half4 result = CombinedShapeLightShared(surface, lighting);
                result.rgb *= result.a; return result;
            }
            ENDHLSL
        }
        Pass {
            Name "NormalsRendering"
            Tags { "LightMode"="NormalsRendering" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex CaneVertex
            #pragma fragment Normals2D
            half4 Normals2D(CaneVaryings input) : SV_Target {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(CaneNormal(input) * .5 + .5, CaneSurface(input).a);
            }
            ENDHLSL
        }
    }
}
