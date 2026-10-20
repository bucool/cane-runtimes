Shader "Cane/Skeleton" {
    Properties {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Pma ("Premultiplied texture", Float) = 0
        [ToggleUI] _Effects ("Material effects", Float) = 0
        _Hue ("Hue (degrees)", Range(-180,180)) = 0
        _Saturation ("Saturation", Range(0,2)) = 1
        _Brightness ("Brightness", Range(0,4)) = 1
        _SecondaryTex ("Secondary albedo", 2D) = "white" {}
        _SecondaryColor ("Secondary tint (alpha = strength)", Color) = (1,1,1,0)
        _Grayscale ("Grayscale", Range(0,1)) = 0
        _FillColor ("Fill color (alpha = strength)", Color) = (1,1,1,0)
        _OutlineColor ("Outline color", Color) = (0,0,0,1)
        _OutlineWidth ("Outline width", Range(0,8)) = 0
        [Toggle] _OutlineScreenSpace ("Outline width in screen pixels", Float) = 0
        [Toggle] _OutlineOnly ("Outline only", Float) = 0
        _OutlineUvRect ("Outline atlas bounds (min/max UV)", Vector) = (0,0,1,1)
        _Cutoff ("Shadow cutoff", Range(0,1)) = .1
        [HideInInspector] _CaneSlotAlpha ("Slot opacity", Float) = 1
        [HideInInspector] _CaneClipCount ("Clip vertices", Integer) = -1
        [HideInInspector] _Canvas ("Canvas", Float) = 0
        [HideInInspector] _ZTest ("Depth comparison", Float) = 4
        [HideInInspector] _SrcBlend ("Source blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination blend", Float) = 10
        [HideInInspector] _DstBlendAlpha ("Destination alpha blend", Float) = 10
        [HideInInspector] _StencilComp ("Stencil comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil write mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil read mask", Float) = 255
        [HideInInspector] _ColorMask ("Color mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("UI alpha clip", Float) = 0
    }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off Lighting Off ZWrite Off ZTest [_ZTest]
        Blend [_SrcBlend] [_DstBlend], One [_DstBlendAlpha]
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        ColorMask [_ColorMask]
        Pass {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma target 4.5 CANE_SLOT_OBJECT
            #pragma multi_compile_local _ CANE_SLOT_OBJECT
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #pragma multi_compile_local _ CANE_SAMPLE_CONTROL
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "Color.hlsl"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize, _Sampling, _SecondaryTex_ST;
            #include "Sampling.hlsl"
            #include "Effects.hlsl"
            #ifdef CANE_SLOT_OBJECT
            #include "SlotObject.hlsl"
            #endif
            struct Input { float4 vertex : POSITION; float4 light : COLOR; float2 uv : TEXCOORD0; float3 dark : TEXCOORD1; float4 coreLight : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Output { float4 vertex : SV_POSITION; float4 light : COLOR; float2 uv : TEXCOORD0; float3 dark : TEXCOORD1; float4 local : TEXCOORD2; float4 global : TEXCOORD3;
                #ifdef CANE_SLOT_OBJECT
                float3 world : TEXCOORD4;
                #endif
                UNITY_VERTEX_OUTPUT_STEREO };
            float _Pma, _Canvas;
            int _UIVertexColorAlwaysGammaSpace;
            float4 _ClipRect;
            Output vert(Input input) {
                Output output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.vertex = UnityObjectToClipPos(input.vertex); output.local = input.vertex;
                output.uv = input.uv; output.light = _Canvas != 0 ? input.coreLight : input.light; output.dark = input.dark;
                output.global = _Canvas != 0 ? input.light : 1;
                #ifdef CANE_SLOT_OBJECT
                output.world = mul(unity_ObjectToWorld, input.vertex).xyz;
                #endif
                #ifndef UNITY_COLORSPACE_GAMMA
                output.light.rgb = CaneToLinear(output.light.rgb); output.dark = CaneToLinear(output.dark);
                if (_UIVertexColorAlwaysGammaSpace) output.global.rgb = CaneToLinear(output.global.rgb);
                #endif
                return output;
            }
            float4 frag(Output input) : SV_Target {
                float4 sample = CaneSample(input.uv);
                float3 rgb = _Pma == 0 ? sample.rgb : sample.a > .000001 ? sample.rgb / sample.a : 0;
                float4 color = float4(saturate(input.dark + (input.light.rgb - input.dark) * rgb), sample.a);
                [branch] if (_Effects != 0) color = CaneEffects(color, input.uv, input.uv * _SecondaryTex_ST.xy + _SecondaryTex_ST.zw);
                color.a = saturate(color.a * input.light.a); color.rgb *= color.a;
                color *= float4(input.global.rgb, 1) * input.global.a;
                #ifdef UNITY_UI_CLIP_RECT
                color *= UnityGet2DClipping(input.local.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - .001);
                #endif
                #ifdef CANE_SLOT_OBJECT
                color = CaneApplySlot(input.world, color);
                #endif
                return color;
            }
            ENDHLSL
        }
        Pass {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual Blend Off ColorMask 0
            Stencil { Comp Always Pass Keep }
            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma target 3.5
            #pragma target 4.5 CANE_SLOT_OBJECT
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_local _ CANE_SLOT_OBJECT
            #pragma multi_compile_local _ CANE_SAMPLE_CONTROL
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize, _Sampling;
            #include "Sampling.hlsl"
            #ifdef CANE_SLOT_OBJECT
            #include "SlotObject.hlsl"
            #endif
            float _Cutoff;
            struct ShadowOutput { V2F_SHADOW_CASTER; float2 uv : TEXCOORD1; float alpha : TEXCOORD2;
                #ifdef CANE_SLOT_OBJECT
                float3 world : TEXCOORD3;
                #endif
            };
            ShadowOutput shadowVert(appdata_full v) {
                ShadowOutput output; v.normal = float3(0,0,-1);
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(output);
                output.uv = v.texcoord.xy; output.alpha = v.color.a;
                #ifdef CANE_SLOT_OBJECT
                output.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                #endif
                return output;
            }
            float4 shadowFrag(ShadowOutput input) : SV_Target {
                float alpha = saturate(CaneSample(input.uv).a * input.alpha);
                #ifdef CANE_SLOT_OBJECT
                alpha = CaneApplySlot(input.world, float4(0,0,0,alpha)).a;
                #endif
                clip(alpha - _Cutoff); SHADOW_CASTER_FRAGMENT(input);
            }
            ENDHLSL
        }
    }
}
