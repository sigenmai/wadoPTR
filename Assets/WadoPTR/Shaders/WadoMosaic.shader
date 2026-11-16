// 修整用モザイク。原体の各部位に、少し膨らませた同じメッシュで重ねて描く。
//
// 仕組み: 背景(すでに描かれた原体を含む)を GrabPass で取り、ブロックの中心だけを拾って塗る。
// ブロックの格子は「画面」ではなく「ワールド空間のカメラ正対面」に固定している。
// 画面基準にすると VR で左右の目の格子がずれて見づらくなるため。大きさは _Cell (メートル)。
Shader "WadoPTR/Mosaic"
{
    Properties
    {
        _Cell ("Block Size (m)", Float) = 0.034
        _Inflate ("Inflate (m)", Float) = 0.012
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+5" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        GrabPass { "_WadoMosaicGrab" }
        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            UNITY_DECLARE_SCREENSPACE_TEXTURE(_WadoMosaicGrab);
            float _Cell;
            float _Inflate;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
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
                float3 wn = UnityObjectToWorldNormal(v.normal);
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz + wn * _Inflate;
                o.wpos = wp;
                o.pos = mul(UNITY_MATRIX_VP, float4(wp, 1.0));
                return o;
            }

            fixed3 grabAt(float3 wp)
            {
                float4 cp = mul(UNITY_MATRIX_VP, float4(wp, 1.0));
                float4 gp = ComputeGrabScreenPos(cp);
                return UNITY_SAMPLE_SCREENSPACE_TEXTURE(_WadoMosaicGrab, gp.xy / gp.w).rgb;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // 視線に正対する 2 軸。左右の目で向きは同じなので、格子は両目で一致する
                float3 right = normalize(UNITY_MATRIX_V[0].xyz);
                float3 up = normalize(UNITY_MATRIX_V[1].xyz);
                float2 uv = float2(dot(i.wpos, right), dot(i.wpos, up));
                float2 q = (floor(uv / _Cell) + 0.5) * _Cell;
                float3 c = i.wpos + right * (q.x - uv.x) + up * (q.y - uv.y);
                float o = _Cell * 0.25;
                fixed3 col = grabAt(c) * 0.4;
                col += grabAt(c + right * o + up * o) * 0.15;
                col += grabAt(c - right * o + up * o) * 0.15;
                col += grabAt(c + right * o - up * o) * 0.15;
                col += grabAt(c - right * o - up * o) * 0.15;
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
