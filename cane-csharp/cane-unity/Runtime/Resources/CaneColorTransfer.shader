Shader "Hidden/Cane/Color Transfer"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        Texture2D<float4> _CaneTransferSource;
        Texture2DArray<float4> _CaneTransferArray;
        Texture2DMS<float4> _CaneTransferMultisample;
        Texture2DMSArray<float4> _CaneTransferMultisampleArray;
        float _CaneTransferCoverage, _CaneTransferIsArray;
        int _CaneTransferSamples;
        struct TransferVertex { float4 position : SV_POSITION; };
        TransferVertex transfer_vertex(uint id : SV_VertexID)
        {
            TransferVertex result;
            result.position = float4(id == 2 ? 3.0 : -1.0, id == 1 ? 3.0 : -1.0, 0.0, 1.0);
            return result;
        }
        float transfer_encode(float v) { return v <= .0031308 ? v * 12.92 : 1.055 * pow(v, 1.0 / 2.4) - .055; }
        float transfer_decode(float v) { return v <= .04045 ? v / 12.92 : pow((v + .055) / 1.055, 2.4); }
        float4 convert_color(TransferVertex input, bool encode)
        {
            int2 pixel = int2(input.position.xy);
            float4 value = 0;
            if (_CaneTransferSamples > 1)
            {
                for (int sample_index = 0; sample_index < _CaneTransferSamples; ++sample_index)
                    value += _CaneTransferIsArray > .5 ? _CaneTransferMultisampleArray.Load(int3(pixel, 0), sample_index) : _CaneTransferMultisample.Load(pixel, sample_index);
                value /= _CaneTransferSamples;
            }
            else value = _CaneTransferIsArray > .5 ? _CaneTransferArray.Load(int4(pixel, 0, 0)) : _CaneTransferSource.Load(int3(pixel, 0));
            // Add can accumulate alpha above one. It must not dim opaque radiance.
            float coverage = _CaneTransferCoverage > .5 ? saturate(value.a) : 1.0;
            float3 straight = coverage > .000001 ? value.rgb / coverage : float3(0, 0, 0);
            float3 result = encode ? float3(transfer_encode(straight.r), transfer_encode(straight.g), transfer_encode(straight.b))
                : float3(transfer_decode(straight.r), transfer_decode(straight.g), transfer_decode(straight.b));
            return float4(result * coverage, value.a);
        }
        float4 encode_color(TransferVertex input) : SV_Target { return convert_color(input, true); }
        float4 decode_color(TransferVertex input) : SV_Target { return convert_color(input, false); }
        ENDHLSL
        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex transfer_vertex
            #pragma fragment encode_color
            ENDHLSL
        }
        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex transfer_vertex
            #pragma fragment decode_color
            ENDHLSL
        }
    }
    Fallback Off
}
