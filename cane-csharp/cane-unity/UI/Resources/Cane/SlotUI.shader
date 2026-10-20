Shader "Cane/SlotUI" {
    Properties {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _StencilComp ("Stencil comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil write mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil read mask", Float) = 255
        [HideInInspector] _ColorMask ("Color mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("UI alpha clip", Float) = 0
        [HideInInspector] _CaneSlotAlpha ("Slot opacity", Float) = 1
        [HideInInspector] _CaneClipCount ("Clip vertices", Integer) = -1
    }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        ColorMask [_ColorMask]
        Pass {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "../../../Runtime/Resources/Cane/SlotObject.hlsl"
            struct Input { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Output { float4 vertex : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float3 world : TEXCOORD1; float4 rect : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex;
            float4 _Color, _MainTex_ST, _TextureSampleAdd, _ClipRect;
            float _UIMaskSoftnessX, _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;
            Output vert(Input input) {
                Output output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.world = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                #ifndef UNITY_COLORSPACE_GAMMA
                if (_UIVertexColorAlwaysGammaSpace) output.color.rgb = UIGammaToLinear(output.color.rgb);
                #endif
                output.color *= _Color;
                float2 pixelSize = abs(output.vertex.w / mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                output.rect = float4(input.vertex.xy, pixelSize);
                return output;
            }
            float4 frag(Output input) : SV_Target {
                float4 color = (tex2D(_MainTex, input.uv) + _TextureSampleAdd) * input.color;
                #ifdef UNITY_UI_CLIP_RECT
                float2 edge = min(input.rect.xy - _ClipRect.xy, _ClipRect.zw - input.rect.xy);
                float2 mask = saturate(edge / (.5 * float2(_UIMaskSoftnessX, _UIMaskSoftnessY) + 2 * input.rect.zw));
                color.a *= mask.x * mask.y;
                #endif
                color.a = CaneApplySlot(input.world, float4(0,0,0,color.a)).a;
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - .001);
                #endif
                color.rgb *= color.a;
                return color;
            }
            ENDHLSL
        }
    }
}
