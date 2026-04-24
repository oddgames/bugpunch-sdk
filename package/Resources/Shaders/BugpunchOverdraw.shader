Shader "Hidden/Bugpunch/Overdraw"
{
    // URP
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }
            ZTest Always
            ZWrite Off
            Cull Off
            Blend One One
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(0.10, 0.035, 0.015, 0);
            }
            ENDHLSL
        }
    }

    // Built-in
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "IgnoreProjector"="True" }
        ZTest Always
        ZWrite Off
        Cull Off
        Blend One One
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct v2f { float4 pos : SV_POSITION; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return fixed4(0.10, 0.035, 0.015, 0);
            }
            ENDCG
        }
    }
    Fallback Off
}
