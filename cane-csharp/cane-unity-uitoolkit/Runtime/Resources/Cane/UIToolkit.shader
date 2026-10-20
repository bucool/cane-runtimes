Shader "Cane/UI Toolkit" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        _Pma ("Premultiplied texture", Float) = 0
        _Light ("Light tint", Vector) = (1,1,1,1)
        _Dark ("Dark tint", Vector) = (0,0,0,0)
        [HideInInspector] _SrcBlend ("Source blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination blend", Float) = 10
        [HideInInspector] _DstBlendAlpha ("Destination alpha blend", Float) = 10
    }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "isCustomUITKShader"="true" }
        Cull Off ZWrite Off
        Blend [_SrcBlend] [_DstBlend], One [_DstBlendAlpha]
        Pass {
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex uie_std_vert
            #pragma fragment frag
            #pragma multi_compile _ _UIE_FORCE_GAMMA
            #pragma multi_compile _ _UIE_TEXTURE_SLOT_COUNT_4 _UIE_TEXTURE_SLOT_COUNT_2 _UIE_TEXTURE_SLOT_COUNT_1
            #pragma multi_compile_local _ CANE_SAMPLE_CONTROL
            #include "Internal/UnityUIE.cginc"
            #include "Packages/com.cane.unity/Runtime/Resources/Cane/Color.hlsl"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize, _Sampling, _Light, _Dark;
            float _Pma;
            #include "Packages/com.cane.unity/Runtime/Resources/Cane/Sampling.hlsl"
            float4 frag(v2f input) : SV_Target {
                float coverage = uie_fragment_clip(input.uvClip.zw);
                clip(coverage - .003);
                float4 sample = CaneSample(input.uvClip.xy);
                float3 rgb = _Pma == 0 ? sample.rgb : sample.a > .000001 ? sample.rgb / sample.a : 0;
                #if _UIE_FORCE_GAMMA && !defined(UNITY_COLORSPACE_GAMMA)
                rgb = CaneToSrgb(rgb);
                #endif
                float3 light = _Light.rgb, dark = _Dark.rgb;
                #if !UIE_COLORSPACE_GAMMA
                light = CaneToLinear(light); dark = CaneToLinear(dark);
                #endif
                float alpha = saturate(sample.a * _Light.a) * input.color.a * coverage;
                // Core supplies float tint; UI vertex tint is white and only its inherited opacity is needed.
                return float4(saturate(dark + (light - dark) * rgb) * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
