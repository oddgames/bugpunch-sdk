Shader "Hidden/Bugpunch/Wireframe"
{
    // Unlit depth-shaded material.
    // Used as a line material drawn by SceneCameraService on a MeshTopology.Lines
    // edge mesh, so it works on every pipeline and every platform — no GL.wireframe.

    // -------- URP subshader (picked when the active RP is Universal) --------
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+1" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            ZTest LEqual
            Cull Off

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
                float t = saturate((i.viewZ - 2.0) / 78.0);
                float b = lerp(1.0, 0.35, t);
                return half4(b, b, b, 1);
            }
            ENDHLSL
        }
    }

    // -------- Built-in RP fallback --------
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+1" "IgnoreProjector"="True" }

        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct v2f { float4 pos : SV_POSITION; float viewZ : TEXCOORD0; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.viewZ = -UnityObjectToViewPos(v.vertex).z;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = saturate((i.viewZ - 2.0) / 78.0);
                float b = lerp(1.0, 0.35, t);
                return fixed4(b, b, b, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
