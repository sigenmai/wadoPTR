// 荒野横断ホースレース用の地形シェーダー（Built-in Render Pipeline / Terrain の materialTemplate）
// - HorseRace/Toon と同じ 2 段のトゥーンの陰影・影色（地形の影と飾りの影を同じ色にする：art-r1-01）
// - TerrainLayer 4 枚まで（_Control の RGBA）。0 土・1 岩・2 砂は平面の UV、3 は「地層の縞」：
//   ワールドの高さ（y / _StrataScale）を V にして横の縞を急な斜面に描く（峡谷の壁がメサの岩の模様と同じ縞になる）
// - 道の踏み固めた土（course-r1-09）：_TrailTex（Terrain 全体の UV、R = 重み）で _TrailColor に置き換える（土の斑を少しだけ残す）。
//   G = 0.5 + 符号付きの横の距離 / 32m（art-r5-01）：±1.8m に幅 0.6m の轍を道の色 ×0.86 で描く。
// - art-r4-02：道の外はワールド座標の値ノイズの粒（3.5m）とまだら（45m）、道の上は蹄の擦れ（1.2m）。art-r4-04：急な面（n.y < _StrataSlope）は地層の縞を濃く
//   ステージごとのマテリアル（TerrainBuilder.ApplyToonMaterial）。_TrailTex が無い（黒）なら何もしない
// - art-r5-05：_TintTex（RGB = ワールドの土の色、A = 重み）で土のレイヤーの色を置き換える（ステージの色が外へにじむ。無ければ黒 = 何もしない）
// - 霧・影受け。GPU インスタンシングの地形描画（drawInstanced）は使わない（TerrainBuilder が OFF にする）
Shader "HorseRace/ToonTerrain"
{
    Properties
    {
        [HideInInspector] _Control ("Control (RGBA)", 2D) = "red" {}
        [HideInInspector] _Splat3 ("Layer 3 (A)", 2D) = "grey" {}
        [HideInInspector] _Splat2 ("Layer 2 (B)", 2D) = "grey" {}
        [HideInInspector] _Splat1 ("Layer 1 (G)", 2D) = "grey" {}
        [HideInInspector] _Splat0 ("Layer 0 (R)", 2D) = "grey" {}
        [HideInInspector] _MainTex ("BaseMap (RGB)", 2D) = "white" {}
        [HideInInspector] _Color ("Main Color", Color) = (1,1,1,1)

        _ShadowColor ("Shadow Color", Color) = (0.70,0.52,0.54,1)
        _ShadowLightFloor ("Sun in shadow (0-1)", Range(0,1)) = 0.5
        _ShadeThreshold ("Shade Threshold", Range(-1,1)) = 0.05
        _ShadeSoftness ("Shade Softness", Range(0.001,0.3)) = 0.05
        _StrataScale ("Strata band height (m)", Float) = 8
        _StrataTile ("Strata horizontal tile (m)", Float) = 45
        _TrailTex ("Trail mask (R) + signed centre distance (G, 0.5 = centre)", 2D) = "black" {}
        _TrailColor ("Trail Color", Color) = (0.55,0.40,0.28,1)
        _RutOffset ("Rut offset (G units)", Float) = 0.1125
        _RutWidth ("Rut width (G units)", Float) = 0.0375
        _RutDarken ("Rut darken (0-1)", Range(0,1)) = 0.14
        _LineBoost ("Start/finish line brightness (x trail color)", Float) = 1.6
        _GrainScale ("Grain scale (m)", Float) = 3.5
        _GrainAmp ("Grain amount", Range(0,0.3)) = 0.13
        _MottleScale ("Mottle scale (m)", Float) = 45
        _MottleAmp ("Mottle amount", Range(0,0.3)) = 0.05
        _ScuffScale ("Hoof scuff scale (m)", Float) = 1.2
        _ScuffAmp ("Hoof scuff amount", Range(0,0.3)) = 0.11
        _StrataSlope ("Strata on faces with normal.y below (0 = off)", Range(0,1)) = 0
        _TintTex ("Ground dirt colour (RGB) + weight (A)", 2D) = "black" {}
        _DirtBase ("Dirt layer base colour", Color) = (0.62,0.36,0.22,1)
    }

    SubShader
    {
        Tags { "Queue"="Geometry-100" "RenderType"="Opaque" "TerrainCompatible"="True" }

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            sampler2D _Control; float4 _Control_ST;
            sampler2D _TrailTex; float4 _TrailColor;
            sampler2D _TintTex; float4 _DirtBase;
            float _RutOffset, _RutWidth, _RutDarken, _LineBoost;
            float _GrainScale, _GrainAmp, _MottleScale, _MottleAmp, _ScuffScale, _ScuffAmp, _StrataSlope;

            // art-r4-02：ワールド座標の値ノイズ（0〜1）。平らな塗りの面に粒とまだらを足す（テクスチャの繰り返しを見せない）
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i), b = hash21(i + float2(1, 0)), c = hash21(i + float2(0, 1)), d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }
            // 2 オクターブ（scale と scale / 2）、−1〜1
            float noise2(float2 p, float scale)
            {
                float2 q = p / max(scale, 0.01);
                return (vnoise(q) * 0.65 + vnoise(q * 2.03 + 17.0) * 0.35) * 2.0 - 1.0;
            }
            sampler2D _Splat0, _Splat1, _Splat2, _Splat3;
            float4 _Splat0_ST, _Splat1_ST, _Splat2_ST, _Splat3_ST;
            fixed4 _ShadowColor;
            // art-r2-10：ステージごとの影色（StageAtmosphere が VRCShader.SetGlobalColor で入れる。a < 0.5 = 未設定 → マテリアルの _ShadowColor）
            float4 _Udon_ShadowColor;
            float _ShadowLightFloor, _ShadeThreshold, _ShadeSoftness, _StrataScale, _StrataTile;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 nrm : TEXCOORD1;
                float3 wpos : TEXCOORD2;
                SHADOW_COORDS(3)
                UNITY_FOG_COORDS(4)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata_full v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                o.nrm = UnityObjectToWorldNormal(v.normal);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                TRANSFER_SHADOW(o);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 ctl = tex2D(_Control, TRANSFORM_TEX(i.uv, _Control));
                float3 c0 = tex2D(_Splat0, TRANSFORM_TEX(i.uv, _Splat0)).rgb;
                // art-r5-05：土の色をワールドの土の色（_TintTex、ステージの縁から外へにじむ）へ。レイヤーの模様の明暗（c0 / _DirtBase）は残す
                float4 gt = tex2D(_TintTex, i.uv);
                c0 = lerp(c0, c0 / max(_DirtBase.rgb, 0.05) * gt.rgb, saturate(gt.a));
                float3 c1 = tex2D(_Splat1, TRANSFORM_TEX(i.uv, _Splat1)).rgb;
                float3 c2 = tex2D(_Splat2, TRANSFORM_TEX(i.uv, _Splat2)).rgb;
                // 地層：横（x+z）は _StrataTile m、縦（高さ）は _StrataScale m ごとの縞
                float2 suv = float2((i.wpos.x + i.wpos.z) / max(_StrataTile, 1.0), i.wpos.y / max(_StrataScale, 0.5));
                float3 c3 = tex2D(_Splat3, suv).rgb;
                float wsum = max(ctl.r + ctl.g + ctl.b + ctl.a, 1e-3);
                float3 albedo = (c0 * ctl.r + c1 * ctl.g + c2 * ctl.b + c3 * ctl.a) / wsum;
                float3 nW = normalize(i.nrm);
                // art-r4-04：急な面（法線の y < _StrataSlope）は地層の縞を濃く（段の付いた岩の重なりに見せる）
                if (_StrataSlope > 0.0)
                    albedo = lerp(albedo, c3, saturate((_StrataSlope - nW.y) * 5.0) * 0.85);
                // 道：踏み固めた土（土の斑の明るさを 30% だけ残す）
                float3 trTex = tex2D(_TrailTex, i.uv).rgb;
                float trail = trTex.r;
                // art-r4-02：道の外の粒（_GrainScale）とまだら（_MottleScale）。平らな色の面を少しだけ揺らす
                // art-r5-13：粒（_GrainAmp 0.13）と細かい擦れ（_ScuffAmp 0.11）は道の重み < 0.5 の所だけ（騎手の目の高さでも地面の細部が読める）
                float offRoad = 1.0 - smoothstep(0.4, 0.6, trail);
                float grain = noise2(i.wpos.xz, _GrainScale) * _GrainAmp + noise2(i.wpos.xz + 311.0, _MottleScale) * _MottleAmp
                            + noise2(i.wpos.xz + 517.0, _ScuffScale) * _ScuffAmp;
                albedo *= 1.0 + grain * offRoad;
                float mott = saturate(dot(c0, float3(0.333, 0.333, 0.333)) * 1.5);
                float3 trailCol = _TrailColor.rgb * (0.92 + 0.16 * mott);
                // 轍（art-r3-06 → art-r5-01）：G = 0.5 + 符号付きの横の距離 / (2·16m)。符号付きなので補間しても中心で折れず、2 本のなめらかな線になる。
                // d = (G − 0.5)·2（16m 単位）。±_RutOffset の幅 _RutWidth の 2 本を道の色 × (1 − _RutDarken)
                float sd = (trTex.g - 0.5) * 2.0;
                float rut = 1.0 - smoothstep(_RutWidth * 0.35, _RutWidth * 0.65, abs(abs(sd) - _RutOffset));
                trailCol *= 1.0 - _RutDarken * rut;
                // art-r4-02 → art-r5-13：蹄の擦れは道の上では弱く（道はきれいに保つ）、道の外の粒は強く
                trailCol *= 1.0 + noise2(i.wpos.xz + 77.0, _ScuffScale) * _ScuffAmp * 0.3;
                albedo = lerp(albedo, trailCol, saturate(trail));
                // スタート・ゴールの線（course-r3-14）：B = 道を横切る帯の重み。道の色を明るく（_LineBoost 倍）
                albedo = lerp(albedo, saturate(_TrailColor.rgb * _LineBoost), saturate(trTex.b));

                float3 n = nW;
                float3 l = normalize(_WorldSpaceLightPos0.xyz);
                float ndl = dot(n, l) * 0.5 + 0.5;
                float atten = SHADOW_ATTENUATION(i);
                float lit = ndl * lerp(0.35, 1.0, atten);
                float t = lit - (0.5 + _ShadeThreshold * 0.5);
                float s = max(_ShadeSoftness, 1e-3);
                float shade = smoothstep(-s, s, t);

                float3 lightCol = _LightColor0.rgb;
                float3 ambient = ShadeSH9(float4(n, 1));
                float3 shadowTint = _Udon_ShadowColor.a > 0.5 ? _Udon_ShadowColor.rgb : _ShadowColor.rgb;
                float3 shadowed = albedo * shadowTint * (ambient + lightCol * _ShadowLightFloor);
                float3 litCol = albedo * (ambient * 0.5 + lightCol);
                float3 col = lerp(shadowed, litCol, shade);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
