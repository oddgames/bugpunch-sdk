Shader "Hidden/Bugpunch/UV"
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

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 cell = floor(i.uv * 10.0);
                float checker = fmod(cell.x + cell.y, 2.0);
                float3 uvTint = float3(frac(i.uv.x), frac(i.uv.y), 0.0);
                float3 grid = lerp(float3(0.15,0.15,0.15), float3(0.85,0.85,0.85), checker);
                float3 col = grid * 0.5 + uvTint * 0.7;
                return half4(col, 1);
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

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 cell = floor(i.uv * 10.0);
                float checker = fmod(cell.x + cell.y, 2.0);
                float3 uvTint = float3(frac(i.uv.x), frac(i.uv.y), 0.0);
                float3 grid = lerp(float3(0.15,0.15,0.15), float3(0.85,0.85,0.85), checker);
                float3 col = grid * 0.5 + uvTint * 0.7;
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
