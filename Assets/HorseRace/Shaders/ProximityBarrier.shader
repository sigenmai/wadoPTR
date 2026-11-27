// コースの見えない柵（Built-in Render Pipeline / VRChat ワールド用）
// - 遠くからは完全に透明。見ている人（カメラ）から _ShowFar m 以内に入ると薄く現れ、_ShowNear m 以内でいちばん濃い
//   （各ピクセルのワールド位置とカメラの距離で決めるので、近づいたところだけ丸く浮かぶ。中継・ドローンのカメラも同じ）
// - 西部劇らしい砂金色の網目（斜めに編んだ縄。結び目が明るく、まわりがぼんやり光る）がゆっくり流れる
// - 上端・下端はぼかす。半透明・ZWrite Off・Cull Off（どちらの面からも見える）・ライティングなし・霧対応
// - シングルパス・ステレオ（インスタンシング方式を含む）と GPU インスタンシングに対応
// UV は BarrierBuilder が作る：u = 柵の線に沿った m、v = 下端からの高さ m
Shader "HorseRace/ProximityBarrier"
{
    Properties
    {
        _Color ("Rope Color", Color) = (0.93, 0.76, 0.42, 1)
        _GlowColor ("Knot / Glow Color", Color) = (1.0, 0.88, 0.58, 1)
        _MaxAlpha ("Max Opacity", Range(0,1)) = 0.6

        [Header(Proximity)]
        _ShowNear ("Fully Visible Within (m)", Float) = 6
        _ShowFar ("Invisible Beyond (m)", Float) = 16

        [Header(Pattern)]
        _Cell ("Lattice Cell (m)", Float) = 1.6
        _LineWidth ("Rope Half Width (cells)", Range(0.005,0.2)) = 0.045
        _Glow ("Glow Strength", Range(0,1)) = 0.35
        _Scroll ("Scroll Speed (cells/s)", Float) = 0.08
        _Height ("Strip Height (m)", Float) = 8.5
        _EdgeFade ("Top/Bottom Fade (m)", Float) = 1.5
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Lighting Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _Color, _GlowColor;
            float _MaxAlpha, _ShowNear, _ShowFar, _Cell, _LineWidth, _Glow, _Scroll, _Height, _EdgeFade;

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
                float3 wpos : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // 帯の 1 区間（長さ 5m × 高さ 8.5m）の三角形の頂点どうしは 10m 以内。頂点が _ShowFar + 12m より遠ければ、
            // その三角形はどこも _ShowFar より遠い（= 透明）なので、画面の外へ潰して塗りの手間を省く。
            #define CULL_SLACK 12.0

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                if (distance(o.wpos, _WorldSpaceCameraPos) > _ShowFar + CULL_SLACK)
                    o.pos = float4(0, 0, -2, 1);   // ニア面の手前（D3D・OpenGL どちらでもクリップされる）
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            // 0..0.5：x に一番近い整数までの距離
            float DistToInt(float x) { return abs(frac(x + 0.5) - 0.5); }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                // ---- 近さ（カメラから各ピクセルまで） ----
                float dist = distance(i.wpos, _WorldSpaceCameraPos);
                float prox = 1.0 - smoothstep(_ShowNear, max(_ShowFar, _ShowNear + 0.01), dist);

                // ---- 斜めに編んだ縄の網目 ----
                float t = _Time.y * _Scroll;
                float2 g = i.uv / max(_Cell, 0.05);
                float a = g.x + g.y + t;          // 右上がり
                float b = g.x - g.y - t * 0.6;    // 右下がり（少し遅く逆向きに流す）
                float da = DistToInt(a), db = DistToInt(b);
                float wa = fwidth(a) * 1.2, wb = fwidth(b) * 1.2;
                float lineA = 1.0 - smoothstep(_LineWidth, _LineWidth + wa, da);
                float lineB = 1.0 - smoothstep(_LineWidth, _LineWidth + wb, db);
                // 縄のより（線に沿って明暗）
                float twistA = 0.75 + 0.25 * sin((g.x - g.y) * 18.85);
                float twistB = 0.75 + 0.25 * sin((g.x + g.y) * 18.85);
                float rope = max(lineA * twistA, lineB * twistB);
                // 結び目（交点）と、縄のまわりのぼんやりした光
                float knot = lineA * lineB;
                float glow = exp(-da * da * 60.0) + exp(-db * db * 60.0);
                float pattern = saturate(rope + knot * 0.6 + glow * _Glow * 0.5);

                // ---- 上端・下端をぼかす ----
                float fadeW = max(_EdgeFade, 0.01);
                float edge = smoothstep(0.0, fadeW, i.uv.y) * (1.0 - smoothstep(_Height - fadeW * 2.0, _Height, i.uv.y));

                // ごく近く（触れるくらい）では少し明るく
                float touch = 1.0 - smoothstep(0.0, _ShowNear, dist);
                float alpha = saturate(pattern * prox * edge * _MaxAlpha * (0.75 + 0.25 * touch));
                fixed3 col = lerp(_Color.rgb, _GlowColor.rgb, saturate(knot + glow * 0.3 + touch * 0.2));

                clip(alpha - 0.002);   // 透明なところは塗らない（微分は上で計算済み）
                fixed4 c = fixed4(col, alpha);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
