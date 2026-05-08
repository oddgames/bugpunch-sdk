Shader "Hidden/Bugpunch/Depth"
{
    // URP — kept for completeness; URP ignores Camera.SetReplacementShader.
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float viewZ : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionHCS = p.positionCS;
                o.viewZ = -p.positionVS.z;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float d = saturate(1.0 - (i.viewZ - 0.3) / 79.7);
                return half4(d, d, d, 1);
            }
            ENDHLSL
        }
    }

    // ─── Built-in: one SubShader per RenderType.

    SubShader { Tags { "RenderType"="Opaque" }
        Pass { Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct v2f { float4 pos : SV_POSITION; float viewZ : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.viewZ = -UnityObjectToViewPos(v.vertex).z; return o; }
            fixed4 frag(v2f i) : SV_Target { float d = saturate(1.0 - (i.viewZ - 0.3) / 79.7); return fixed4(d, d, d, 1); }
            ENDCG
        }
    }

    SubShader { Tags { "RenderType"="Transparent" }
        Pass { Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct v2f { float4 pos : SV_POSITION; float viewZ : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.viewZ = -UnityObjectToViewPos(v.vertex).z; return o; }
            fixed4 frag(v2f i) : SV_Target { float d = saturate(1.0 - (i.viewZ - 0.3) / 79.7); return fixed4(d, d, d, 1); }
            ENDCG
        }
    }

    SubShader { Tags { "RenderType"="TransparentCutout" }
        Pass { Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct v2f { float4 pos : SV_POSITION; float viewZ : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.viewZ = -UnityObjectToViewPos(v.vertex).z; return o; }
            fixed4 frag(v2f i) : SV_Target { float d = saturate(1.0 - (i.viewZ - 0.3) / 79.7); return fixed4(d, d, d, 1); }
            ENDCG
        }
    }

    SubShader { Tags { "RenderType"="Overlay" }
        Pass { Cull Off ZTest Always ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct v2f { float4 pos : SV_POSITION; float viewZ : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.viewZ = -UnityObjectToViewPos(v.vertex).z; return o; }
            fixed4 frag(v2f i) : SV_Target { float d = saturate(1.0 - (i.viewZ - 0.3) / 79.7); return fixed4(d, d, d, 1); }
            ENDCG
        }
    }

    Fallback Off
}
