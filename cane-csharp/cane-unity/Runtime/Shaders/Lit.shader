Shader "Cane/Lit" {
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
        [HideInInspector] _CaneSlotAlpha ("Slot opacity", Float) = 1
        [HideInInspector] _CaneClipCount ("Clip vertices", Integer) = -1
        [Normal] _BumpMap ("Normal map", 2D) = "bump" {}
        _BumpScale ("Normal strength", Range(0,2)) = 1
        _MetallicGlossMap ("Metallic (R), smoothness (A)", 2D) = "white" {}
        [Gamma] _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = .5
        _EmissionMap ("Emission", 2D) = "white" {}
        [HDR][Gamma] _EmissionColor ("Emission color", Color) = (0,0,0,1)
        _Cutoff ("Shadow cutoff", Range(0,1)) = .1
        [Toggle] _ZWrite ("Depth write", Float) = 0
        [HideInInspector] _StencilComp ("Stencil comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil write mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil read mask", Float) = 255
    }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite [_ZWrite] ColorMask RGBA
        Blend One OneMinusSrcAlpha
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        CGPROGRAM
        #pragma target 3.5
        #pragma target 4.5 CANE_SLOT_OBJECT
        #pragma multi_compile_local _ CANE_SLOT_OBJECT
        #pragma multi_compile_local _ CANE_SAMPLE_CONTROL
        #pragma surface Surface Cane keepalpha fullforwardshadows addshadow vertex:Vertex exclude_path:deferred exclude_path:prepass
        #include "UnityCG.cginc"
        #include "UnityStandardUtils.cginc"
        #include "UnityPBSLighting.cginc"
        // Fog must cover the same fraction as the premultiplied surface (zero for additive lights).
        #undef UNITY_APPLY_FOG
        #define UNITY_APPLY_FOG(coord,col) UNITY_APPLY_FOG_COLOR(coord,col,unity_FogColor * col.a)
        #include "../Resources/Cane/Color.hlsl"
        sampler2D _MainTex;
        float4 _MainTex_TexelSize, _Sampling;
        #include "../Resources/Cane/Sampling.hlsl"
        #include "../Resources/Cane/Effects.hlsl"
        #ifdef CANE_SLOT_OBJECT
        #include "../Resources/Cane/SlotObject.hlsl"
        #endif
        sampler2D _BumpMap, _MetallicGlossMap, _EmissionMap;
        float _Pma, _BumpScale, _Metallic, _Glossiness, _Cutoff;
        float4 _EmissionColor;
        struct Input { float2 uv_MainTex; float2 uv_SecondaryTex; float4 light; float3 dark; float3 worldPos; };
        half4 LightingCane(SurfaceOutputStandard surface, float3 view, UnityGI lighting) {
            half alpha = surface.Alpha; surface.Alpha = 1;
            half4 color = LightingStandard(surface, view, lighting);
            [branch] if (_Effects != 0) {
                float3 normal = normalize(surface.Normal);
                if (_RampStrength > 0) {
                    half3 specular; half remaining;
                    half3 diffuse = DiffuseAndSpecularFromMetallic(surface.Albedo, surface.Metallic, specular, remaining);
                    UnityIndirect indirect = (UnityIndirect)0;
                    half3 original = UNITY_BRDF_PBS(diffuse, 0, remaining, surface.Smoothness, normal, view, lighting.light, indirect).rgb;
                    color.rgb += _RampStrength * (diffuse * lighting.light.color * CaneLightRamp(saturate(dot(normal, lighting.light.dir))) - original);
                }
                #ifndef UNITY_PASS_FORWARDADD
                if (_RimColor.a > 0) color.rgb += CaneRim(normal, view);
                #endif
            }
            #ifdef UNITY_PASS_FORWARDADD
            return half4(color.rgb * alpha, 0);
            #else
            return half4(color.rgb * alpha, alpha);
            #endif
        }
        void LightingCane_GI(SurfaceOutputStandard surface, UnityGIInput data, inout UnityGI lighting) {
            LightingStandard_GI(surface, data, lighting);
        }
        void Vertex(inout appdata_full vertex, out Input output) {
            UNITY_INITIALIZE_OUTPUT(Input, output);
            output.light = vertex.color; output.dark = vertex.texcoord1.xyz;
            #ifndef UNITY_COLORSPACE_GAMMA
            output.light.rgb = CaneToLinear(output.light.rgb); output.dark = CaneToLinear(output.dark);
            #endif
        }
        void Surface(Input input, inout SurfaceOutputStandard output) {
            float4 texel = CaneSample(input.uv_MainTex);
            float3 rgb = _Pma == 0 ? texel.rgb : texel.a > .000001 ? texel.rgb / texel.a : 0;
            float4 color = float4(saturate(input.dark + (input.light.rgb - input.dark) * rgb), texel.a);
            #ifndef UNITY_PASS_SHADOWCASTER
            [branch] if (_Effects != 0) color = CaneEffects(color, input.uv_MainTex, input.uv_SecondaryTex);
            #endif
            output.Albedo = color.rgb; output.Alpha = saturate(color.a * input.light.a);
            #ifdef CANE_SLOT_OBJECT
            output.Alpha = CaneApplySlot(input.worldPos, float4(0,0,0,output.Alpha)).a;
            #endif
            #ifdef UNITY_PASS_SHADOWCASTER
            clip(output.Alpha - _Cutoff);
            #endif
            output.Normal = UnpackScaleNormal(tex2D(_BumpMap, input.uv_MainTex), _BumpScale);
            float4 metal = tex2D(_MetallicGlossMap, input.uv_MainTex);
            output.Metallic = metal.r * _Metallic; output.Smoothness = metal.a * _Glossiness;
            output.Occlusion = 1; output.Emission = tex2D(_EmissionMap, input.uv_MainTex).rgb * _EmissionColor.rgb * output.Alpha;
        }
        ENDCG
    }
}
