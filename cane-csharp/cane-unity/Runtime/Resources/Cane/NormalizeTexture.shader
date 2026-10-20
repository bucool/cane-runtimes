Shader "Hidden/Cane/NormalizeTexture" {
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader {
        Cull Off ZWrite Off ZTest Always
        Pass {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "Color.hlsl"
            Texture2D<float4> _MainTex;
            float4 _MainTex_TexelSize, _Storage;
            float4 frag(v2f_img input) : SV_Target {
                int2 pixel = min(int2(input.uv * _MainTex_TexelSize.zw), int2(_MainTex_TexelSize.zw) - 1);
                float4 color = _MainTex.Load(int3(pixel, 0));
                if (_Storage.y != 0) color.rgb = CaneToSrgb(color.rgb);
                // V2 associates RGB with alpha in the declared storage space.
                if (_Storage.x == _Storage.w) {
                    if (_Storage.z != 0) color.rgb = color.a > 0 ? color.rgb / color.a : 0;
                    color.rgb = _Storage.w != 0 ? CaneToLinear(color.rgb) : CaneToSrgb(color.rgb);
                    if (_Storage.z != 0) color.rgb *= color.a;
                }
                return color;
            }
            ENDHLSL
        }
    }
}
