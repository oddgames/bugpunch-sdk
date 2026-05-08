Shader "Hidden/Bugpunch/Normals"
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

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float3 worldNormal : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.worldNormal = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(normalize(i.worldNormal) * 0.5 + 0.5, 1);
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
            struct v2f { float4 pos : SV_POSITION; float3 worldNormal : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.worldNormal = UnityObjectToWorldNormal(v.normal); return o; }
            fixed4 frag(v2f i) : SV_Target { return fixed4(normalize(i.worldNormal) * 0.5 + 0.5, 1); }
            ENDCG
        }
    }

    SubShader { Tags { "RenderType"="Transparent" }
        Pass { Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct v2f { float4 pos : SV_POSITION; float3 worldNormal : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.worldNormal = UnityObjectToWorldNormal(v.normal); return o; }
            fixed4 frag(v2f i) : SV_Target { return fixed4(normalize(i.worldNormal) * 0.5 + 0.5, 1); }
            ENDCG
        }
    }

    SubShader { Tags { "RenderType"="TransparentCutout" }
        Pass { Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct v2f { float4 pos : SV_POSITION; float3 worldNormal : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.worldNormal = UnityObjectToWorldNormal(v.normal); return o; }
            fixed4 frag(v2f i) : SV_Target { return fixed4(normalize(i.worldNormal) * 0.5 + 0.5, 1); }
            ENDCG
        }
    }

    SubShader { Tags { "RenderType"="Overlay" }
        Pass { Cull Off ZTest Always ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct v2f { float4 pos : SV_POSITION; float3 worldNormal : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.worldNormal = UnityObjectToWorldNormal(v.normal); return o; }
            fixed4 frag(v2f i) : SV_Target { return fixed4(normalize(i.worldNormal) * 0.5 + 0.5, 1); }
            ENDCG
        }
    }

    Fallback Off
}
