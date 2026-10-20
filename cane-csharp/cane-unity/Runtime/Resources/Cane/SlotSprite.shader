Shader "Cane/SlotSprite" {
    Properties {
        [PerRendererData] _MainTex ("Sprite texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("Renderer color", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("External alpha enabled", Float) = 0
        [HideInInspector] _CaneSlotAlpha ("Slot opacity", Float) = 1
        [HideInInspector] _CaneClipCount ("Clip vertices", Integer) = -1
    }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Cull Off ZWrite Off Blend One OneMinusSrcAlpha
        Pass {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"
            #include "SlotObject.hlsl"
            struct Output { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float3 world : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            Output vert(appdata_t input) {
                Output output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                v2f sprite = SpriteVert(input);
                output.vertex = sprite.vertex; output.color = sprite.color; output.uv = sprite.texcoord;
                output.world = mul(unity_ObjectToWorld, UnityFlipSprite(input.vertex, _Flip)).xyz;
                return output;
            }
            float4 frag(Output input) : SV_Target {
                float4 color = SampleSpriteTexture(input.uv) * input.color;
                color.rgb *= color.a;
                return CaneApplySlot(input.world, color);
            }
            ENDHLSL
        }
    }
}
