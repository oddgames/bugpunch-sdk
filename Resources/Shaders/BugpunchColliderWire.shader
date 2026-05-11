Shader "Hidden/Bugpunch/ColliderWire"
{
    // Per-instance tinted line shader for the collider wireframe overlay.
    // ZTest Always so colliders show through walls (matches Unity gizmo
    // / Physics Debugger behaviour at runtime). _TintColor is set per-draw
    // via MaterialPropertyBlock from SceneCameraService.

    Properties
    {
        _TintColor ("Tint Color", Color) = (0, 1, 0, 1)
    }

    // -------- URP subshader --------
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "ColliderWire"
            Tags { "LightMode"="UniversalForward" }
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _TintColor;

            struct v2f { float4 pos : SV_POSITION; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target { return _TintColor; }
            ENDCG
        }
    }

    // -------- Built-in RP fallback --------
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" "IgnoreProjector"="True" }

        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _TintColor;

            struct v2f { float4 pos : SV_POSITION; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target { return _TintColor; }
            ENDCG
        }
    }
    Fallback Off
}
