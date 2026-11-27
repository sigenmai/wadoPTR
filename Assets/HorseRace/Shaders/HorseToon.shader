// 荒野横断ホースレース用トゥーンシェーダー（Built-in Render Pipeline / VRChat ワールド用）
// - リアル寄りのテクスチャを「平らに」して手描き風に寄せる（_Flatten）
// - 2〜3段の陰影（セル）、影色（影の段にも太陽の半分を足して真っ黒にしない）、リムライト、アウトライン
// - GPU インスタンシング対応（全パス multi_compile_instancing、Build World がマテリアルの enableInstancing を ON）
// - 色相・彩度・明るさで毛色を変える（暗いたてがみ・蹄はあまり変わらない）
Shader "HorseRace/Toon"
{
    Properties
    {
        _MainTex ("Base Color", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)

        [Header(Coat Color)]
        _HueShift ("Hue Shift (0-1)", Range(0,1)) = 0
        _Saturation ("Saturation", Range(0,2)) = 1
        _Brightness ("Brightness", Range(0,3)) = 1
        _CoatMaskLow ("Coat mask: dark threshold", Range(0,1)) = 0.08
        _CoatMaskHigh ("Coat mask: full at", Range(0,1)) = 0.22

        [Header(Toon)]
        _Flatten ("Flatten Texture Detail", Range(0,1)) = 0.7
        _FlattenMip ("Flatten Blur (mip bias)", Range(0,8)) = 4
        _Posterize ("Posterize Steps (0=off)", Range(0,16)) = 6
        _ShadowColor ("Shadow Color", Color) = (0.70,0.52,0.54,1)
        _ShadowLightFloor ("Sun in shadow (0-1)", Range(0,1)) = 0.5
        [IntRange] _ShadeSteps ("Shade Steps (2 or 3)", Range(2,3)) = 2
        _ShadeThreshold ("Shade Threshold", Range(-1,1)) = 0.05
        _ShadeSoftness ("Shade Softness", Range(0.001,0.3)) = 0.03

        [Header(Rim)]
        _RimColor ("Rim Color", Color) = (1,0.9,0.75,1)
        _RimPower ("Rim Power", Range(0.5,8)) = 3.5
        _RimStrength ("Rim Strength", Range(0,1)) = 0.35

        [Header(Outline)]
        _OutlineColor ("Outline Color", Color) = (0.12,0.07,0.05,1)
        _OutlineWidth ("Outline Width (m)", Range(0,0.03)) = 0.008
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex; float4 _MainTex_ST;
    fixed4 _Tint;
    float _HueShift, _Saturation, _Brightness, _CoatMaskLow, _CoatMaskHigh;
    float _Flatten, _FlattenMip, _Posterize;

    float3 RGBtoHSV(float3 c)
    {
        float4 K = float4(0.0, -1.0/3.0, 2.0/3.0, -1.0);
        float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
        float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
        float d = q.x - min(q.w, q.y);
        float e = 1.0e-10;
        return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
    }
    float3 HSVtoRGB(float3 c)
    {
        float4 K = float4(1.0, 2.0/3.0, 1.0/3.0, 3.0);
        float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
        return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
    }

    // テクスチャ色：細部を平らにし、毛色を変え、段階化する
    float3 ToonAlbedo(float2 uv)
    {
        float3 sharp = tex2D(_MainTex, uv).rgb;
        float3 blur = tex2Dbias(_MainTex, float4(uv, 0, _FlattenMip)).rgb;
        float3 col = lerp(sharp, blur, _Flatten);

        float3 hsv = RGBtoHSV(col);
        // 暗い部分（たてがみ・蹄）は毛色変更の影響を弱める
        float coat = smoothstep(_CoatMaskLow, _CoatMaskHigh, hsv.z);
        float3 shifted = float3(frac(hsv.x + _HueShift), saturate(hsv.y * _Saturation), hsv.z * _Brightness);
        float3 dark = float3(hsv.x, hsv.y, hsv.z * lerp(1.0, _Brightness, 0.35));
        hsv = lerp(dark, shifted, coat);
        if (_Posterize >= 1.0) hsv.z = (floor(hsv.z * _Posterize) + 0.5) / _Posterize;
        return saturate(HSVtoRGB(hsv)) * _Tint.rgb;
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        // ---- メイン（平行光源・環境光・影受け）
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            fixed4 _ShadowColor, _RimColor;
            // art-r2-10：ステージごとの影色（StageAtmosphere が VRCShader.SetGlobalColor で入れる。a < 0.5 = 未設定 → マテリアルの _ShadowColor）
            float4 _Udon_ShadowColor;
            float _ShadeSteps, _ShadeThreshold, _ShadeSoftness, _RimPower, _RimStrength, _ShadowLightFloor;

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

            // appdata_base は UNITY_VERTEX_INPUT_INSTANCE_ID を含む（GPU インスタンシング）
            v2f vert(appdata_base v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.nrm = UnityObjectToWorldNormal(v.normal);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                TRANSFER_SHADOW(o);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 albedo = ToonAlbedo(i.uv);
                float3 n = normalize(i.nrm);
                float3 l = normalize(_WorldSpaceLightPos0.xyz);
                float3 v = normalize(_WorldSpaceCameraPos - i.wpos);

                float ndl = dot(n, l) * 0.5 + 0.5;               // half-lambert 0..1
                float atten = SHADOW_ATTENUATION(i);
                float lit = ndl * lerp(0.35, 1.0, atten);
                // セル化：2段（影/光）または 3段（影/中間/光）
                float t = lit - (0.5 + _ShadeThreshold * 0.5);
                float s = max(_ShadeSoftness, 1e-3);
                float shade = (_ShadeSteps < 2.5)
                    ? smoothstep(-s, s, t)
                    : 0.5 * smoothstep(-s, s, t + 0.15) + 0.5 * smoothstep(-s, s, t - 0.15);

                float3 lightCol = _LightColor0.rgb;
                float3 ambient = ShadeSH9(float4(n, 1));
                // 影の段にも太陽の半分を足す（影は「暗く」ではなく「色を変える」：LightingPlan.SHADOW_LIGHT_FLOOR）
                float3 shadowTint = _Udon_ShadowColor.a > 0.5 ? _Udon_ShadowColor.rgb : _ShadowColor.rgb;
                float3 shadowed = albedo * shadowTint * (ambient + lightCol * _ShadowLightFloor);
                float3 litCol = albedo * (ambient * 0.5 + lightCol);
                float3 col = lerp(shadowed, litCol, shade);

                float rim = pow(1.0 - saturate(dot(n, v)), _RimPower) * saturate(dot(n, l) + 0.3);
                col += _RimColor.rgb * step(0.5, rim) * _RimStrength;

                UNITY_APPLY_FOG(i.fogCoord, col);
                return fixed4(col, 1);
            }
            ENDCG
        }

        // ---- アウトライン（背面法線押し出し）
        Pass
        {
            Name "OUTLINE"
            Cull Front
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            fixed4 _OutlineColor;
            float _OutlineWidth;
            struct v2f { float4 pos : SV_POSITION; UNITY_FOG_COORDS(0) UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata_base v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 wn = UnityObjectToWorldNormal(v.normal);
                // 遠くでも線が細くなりすぎないよう距離で少し太らせる（上限あり）
                float dist = distance(wpos, _WorldSpaceCameraPos);
                wpos += wn * _OutlineWidth * clamp(dist * 0.15, 1.0, 4.0);
                o.pos = mul(UNITY_MATRIX_VP, float4(wpos, 1));
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = _OutlineColor;
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }

        // ---- 影を落とす
        Pass
        {
            Tags { "LightMode"="ShadowCaster" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing
            struct v2f { V2F_SHADOW_CASTER; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata_base v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }
            float4 frag(v2f i) : SV_Target { SHADOW_CASTER_FRAGMENT(i) }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
