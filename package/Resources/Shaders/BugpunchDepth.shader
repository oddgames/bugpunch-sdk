Shader "Hidden/Bugpunch/Depth"
{
    // URP
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
            struct Varyings   { float4 positionHCS : SV_POSITION; float depth01 : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionHCS = p.positionCS;
                // Normalised depth across the far plane (matches _ProjectionParams.w in Built-in).
                o.depth01 = -p.positionVS.z / _ProjectionParams.y;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float d = saturate(1.0 - i.depth01);
                return half4(d, d, d, 1);
            }
            ENDHLSL
        }
    }

    // Built-in
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct v2f { float4 pos : SV_POSITION; float depth : TEXCOORD0; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.depth = -(UnityObjectToViewPos(v.vertex).z * _ProjectionParams.w);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float d = saturate(1.0 - i.depth);
                return fixed4(d, d, d, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
