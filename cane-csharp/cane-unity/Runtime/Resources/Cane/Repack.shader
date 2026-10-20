Shader "Hidden/Cane/Repack"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            Texture2D<float4> _MainTex;
            float4 _Source, _Size;
            int Wrap(int value, int size, int mode)
            {
                if (mode == 0) return clamp(value, 0, size - 1);
                if (mode == 3) return clamp(value < 0 ? -value - 1 : value, 0, size - 1);
                int period = mode == 1 ? size : size * 2;
                value = (value % period + period) % period;
                return mode == 1 || value < size ? value : period - 1 - value;
            }
            float4 frag(v2f_img input) : SV_Target
            {
                int2 texel = (int2)floor(_Source.xy + input.uv * _Source.zw);
                texel = int2(Wrap(texel.x, (int)_Size.x, (int)_Size.z), Wrap(texel.y, (int)_Size.y, (int)_Size.w));
                return _MainTex.Load(int3(texel, 0));
            }
            ENDHLSL
        }
    }
}
