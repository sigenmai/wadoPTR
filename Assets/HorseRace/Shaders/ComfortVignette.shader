// VR の酔い止めの視界の縁（ui-r2-18、Built-in Render Pipeline / VRChat ワールド用）
// - 頭の位置に置いた内向きの球（砂塵のかすみと同じメッシュ）に掛ける。新しい形は作らない
// - 各ピクセルの「カメラから見た向き」と「カメラの正面」の内積で、正面は透明、縁ほど暗い：
//     alpha = smoothstep(_InnerCos, _OuterCos, dot(viewDir, camForward)) × _Strength
//   （カメラの正面は目ごとのビュー行列から取るので、シングルパス・ステレオでも左右の目でずれない）
// - _Strength は ComfortVignette（Udon）が乗り物の速さと曲がる速さから毎フレーム入れる（0 = 出ない）
// - 半透明・ZWrite Off・ZTest Always（近くの物にも負けない）・ライティングなし・霧なし、いちばん最後に描く
Shader "HorseRace/ComfortVignette"
{
    Properties
    {
        _Color ("Edge Color", Color) = (0.05, 0.035, 0.02, 1)
        _Strength ("Strength", Range(0,1)) = 0
        _InnerCos ("Clear Inside (cos)", Range(0,1)) = 0.82
        _OuterCos ("Dark Outside (cos)", Range(0,1)) = 0.45
    }

    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            Lighting Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Strength, _InnerCos, _OuterCos;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wpos : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 viewDir = normalize(i.wpos - _WorldSpaceCameraPos);
                // ビュー行列の 3 行目 = カメラの後ろ向き（Unity のビュー空間は -Z が前）
                float3 camFwd = -normalize(UNITY_MATRIX_V[2].xyz);
                float d = dot(viewDir, camFwd);
                float a = smoothstep(_InnerCos, _OuterCos, d) * _Strength;
                return fixed4(_Color.rgb, a * _Color.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
