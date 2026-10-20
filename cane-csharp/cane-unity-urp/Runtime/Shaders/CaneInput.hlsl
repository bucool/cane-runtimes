#ifndef CANE_URP_INPUT
#define CANE_URP_INPUT
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.cane.unity/Runtime/Resources/Cane/Color.hlsl"
CBUFFER_START(UnityPerMaterial)
float4 _MainTex_TexelSize, _Sampling, _EmissionColor;
float4 _FillColor, _OutlineColor, _OutlineUvRect;
float4 _SecondaryTex_ST, _SecondaryColor, _RimColor;
float _Pma, _Cutoff, _BumpScale, _Metallic, _Glossiness;
float _Grayscale, _OutlineWidth, _OutlineScreenSpace, _OutlineOnly;
float _Effects, _Hue, _Saturation, _Brightness, _RampStrength, _RimPower;
float _CaneSlotAlpha;
int _CaneClipCount;
CBUFFER_END
sampler2D _MainTex, _BumpMap, _MetallicGlossMap, _EmissionMap, _MaskTex;
#include "Packages/com.cane.unity/Runtime/Resources/Cane/Sampling.hlsl"
#define CANE_EFFECTS_MATERIAL_PROPERTIES
#include "Packages/com.cane.unity/Runtime/Resources/Cane/Effects.hlsl"
#ifdef CANE_SLOT_OBJECT
#define CANE_SLOT_MATERIAL_PROPERTIES
#include "Packages/com.cane.unity/Runtime/Resources/Cane/SlotObject.hlsl"
#endif

struct CaneAttributes {
    float3 position : POSITION;
    float3 normal : NORMAL;
    float4 tangent : TANGENT;
    float2 uv : TEXCOORD0;
    float4 light : COLOR;
    float3 dark : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct CaneVaryings {
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 light : COLOR;
    float3 dark : TEXCOORD1;
    float3 world : TEXCOORD2;
    float3 normal : TEXCOORD3;
    float4 tangent : TEXCOORD4;
    float fog : TEXCOORD5;
    half3 sh : TEXCOORD6;
    half3 vertexLight : TEXCOORD7;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};
CaneVaryings CaneVertex(CaneAttributes input) {
    CaneVaryings output = (CaneVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    VertexPositionInputs position = GetVertexPositionInputs(input.position);
    output.positionCS = position.positionCS; output.world = position.positionWS;
    float3 normal = dot(input.normal, input.normal) > 0 ? input.normal : float3(0,0,-1);
    float4 tangent = input.tangent.w != 0 ? input.tangent : float4(1,0,0,-1);
    output.normal = TransformObjectToWorldNormal(normal);
    output.tangent = float4(TransformObjectToWorldDir(tangent.xyz), tangent.w * GetOddNegativeScale());
    output.uv = input.uv; output.light = input.light; output.dark = input.dark;
    output.fog = ComputeFogFactor(position.positionCS.z);
    #ifndef UNITY_COLORSPACE_GAMMA
    output.light.rgb = CaneToLinear(output.light.rgb); output.dark = CaneToLinear(output.dark);
    #endif
    return output;
}
float4 CaneSurface(CaneVaryings input, bool effects = true) {
    float4 texel = CaneSample(input.uv);
    float3 rgb = _Pma == 0 ? texel.rgb : texel.a > .000001 ? texel.rgb / texel.a : 0;
    float4 color = float4(saturate(input.dark + (input.light.rgb - input.dark) * rgb), texel.a);
    [branch] if (effects && _Effects != 0) color = CaneEffects(color, input.uv, input.uv * _SecondaryTex_ST.xy + _SecondaryTex_ST.zw);
    color.a = saturate(color.a * input.light.a);
    #ifdef CANE_SLOT_OBJECT
    color.a = CaneApplySlot(input.world, float4(0,0,0,color.a)).a;
    #endif
    return color;
}
float3 CaneNormal(CaneVaryings input) {
    float3 tangentNormal = UnpackNormalScale(tex2D(_BumpMap, input.uv), _BumpScale);
    float3 bitangent = cross(input.normal, input.tangent.xyz) * input.tangent.w;
    return normalize(mul(tangentNormal, float3x3(input.tangent.xyz, bitangent, input.normal)));
}
float4 CaneUnlit(CaneVaryings input) : SV_Target {
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float4 color = CaneSurface(input); color.rgb *= color.a; return color;
}
float4 CaneDepth(CaneVaryings input) : SV_Target {
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    clip(CaneSurface(input, false).a - _Cutoff); return 0;
}
#endif
