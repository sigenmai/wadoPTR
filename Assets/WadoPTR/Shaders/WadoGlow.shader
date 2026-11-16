// ネオンのにじみ。看板や管の手前に置いた板に、中心から外へ薄れる光を加算で描く。
// ポストプロセス(Bloom)に頼らずに、暗い路地のネオンらしさを出すためのもの。
Shader "WadoPTR/Glow"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.15, 0.45, 1)
        _Strength ("Strength", Float) = 0.55
        _Core ("Core Size", Range(0, 1)) = 0.45
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+30" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off
            Fog { Mode Off }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Strength;
            float _Core;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 板の縁で 0、芯(_Core)の内側で 1 になる四角いにじみ
                float2 d = abs(i.uv - 0.5) * 2.0;
                float2 f = saturate((1.0 - d) / max(1.0 - _Core, 0.001));
                float a = f.x * f.y;
                a = a * a * (3.0 - 2.0 * a);
                return fixed4(_Color.rgb * (a * _Strength), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
