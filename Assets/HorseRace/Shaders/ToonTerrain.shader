// 荒野横断ホースレース用の地形シェーダー（Built-in Render Pipeline / Terrain の materialTemplate）
// - HorseRace/Toon と同じ 2 段のトゥーンの陰影・影色（地形の影と飾りの影を同じ色にする：art-r1-01）
// - TerrainLayer 4 枚まで（_Control の RGBA）。0 土・1 岩・2 砂は平面の UV、3 は「地層の縞」：
//   ワールドの高さ（y / _StrataScale）を V にして横の縞を急な斜面に描く（峡谷の壁がメサの岩の模様と同じ縞になる）
// - 道の踏み固めた土（course-r1-09）：_TrailTex（Terrain 全体の UV、R = 重み）で _TrailColor に置き換える（土の斑を少しだけ残す）。
//   ステージごとのマテリアル（TerrainBuilder.ApplyToonMaterial）。_TrailTex が無い（黒）なら何もしない
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
        _TrailTex ("Trail mask (R)", 2D) = "black" {}
        _TrailColor ("Trail Color", Color) = (0.55,0.40,0.28,1)
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
                float3 c1 = tex2D(_Splat1, TRANSFORM_TEX(i.uv, _Splat1)).rgb;
                float3 c2 = tex2D(_Splat2, TRANSFORM_TEX(i.uv, _Splat2)).rgb;
                // 地層：横（x+z）は _StrataTile m、縦（高さ）は _StrataScale m ごとの縞
                float2 suv = float2((i.wpos.x + i.wpos.z) / max(_StrataTile, 1.0), i.wpos.y / max(_StrataScale, 0.5));
                float3 c3 = tex2D(_Splat3, suv).rgb;
                float wsum = max(ctl.r + ctl.g + ctl.b + ctl.a, 1e-3);
                float3 albedo = (c0 * ctl.r + c1 * ctl.g + c2 * ctl.b + c3 * ctl.a) / wsum;
                // 道：踏み固めた土（土の斑の明るさを 30% だけ残す）
                float trail = tex2D(_TrailTex, i.uv).r;
                float mott = saturate(dot(c0, float3(0.333, 0.333, 0.333)) * 1.5);
                float3 trailCol = _TrailColor.rgb * (0.92 + 0.16 * mott);
                albedo = lerp(albedo, trailCol, saturate(trail));

                float3 n = normalize(i.nrm);
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
