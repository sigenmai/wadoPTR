using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// ステージごとの空気（art-r2-10）：荒野 = 赤錆の昼、砂漠 = 砂金の午後、境目 = 紫の夕景。
    /// RaceManager の同期状態（state / stage）を毎フレーム見て、ロビーかステージが変わったときだけ（ローカル）
    /// 霧の色と距離・環境光（Trilight 3 色）・太陽（向きと色と強さ）を入れ替え、トゥーンの影の色を
    /// グローバルの _Udon_ShadowColor（HorseToon / ToonTerrain が読む。a = 1 で有効）に入れる。
    /// 値は Build World が LightingPlan.ForStage(0〜3) から配列（添字 3 = ロビー）に書き込む。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class StageAtmosphere : UdonSharpBehaviour
    {
        public const int LOBBY = 3;

        public RaceManager raceManager;
        public Light sun;
        public Vector3[] sunEuler;
        public Color[] sunColor;
        public float[] sunIntensity;
        public Color[] fogColor;
        public float[] fogStart;
        public float[] fogEnd;
        public Color[] ambientSky;
        public Color[] ambientEquator;
        public Color[] ambientGround;
        public Color[] shadowColor;

        private int applied = -1;
        private int shadowId;
        private bool idReady;

        private void Start()
        {
            Apply(LOBBY);
        }

        private void Update()
        {
            int want = LOBBY;
            if (raceManager != null && raceManager.state != RaceMath.STATE_LOBBY)
            {
                int s = raceManager.stage;
                if (s >= 0 && s < LOBBY) want = s;
            }
            if (want != applied) Apply(want);
        }

        /// <summary>添字 i（0〜2 = ステージ、3 = ロビー）の光にする。配列が足りなければ何もしない。</summary>
        public void Apply(int i)
        {
            applied = i;
            if (fogColor == null || i < 0 || i >= fogColor.Length) return;
            RenderSettings.fogColor = fogColor[i];
            if (fogStart != null && i < fogStart.Length) RenderSettings.fogStartDistance = fogStart[i];
            if (fogEnd != null && i < fogEnd.Length) RenderSettings.fogEndDistance = fogEnd[i];
            if (ambientSky != null && i < ambientSky.Length) RenderSettings.ambientSkyColor = ambientSky[i];
            if (ambientEquator != null && i < ambientEquator.Length) RenderSettings.ambientEquatorColor = ambientEquator[i];
            if (ambientGround != null && i < ambientGround.Length) RenderSettings.ambientGroundColor = ambientGround[i];
            if (sun != null)
            {
                if (sunEuler != null && i < sunEuler.Length) sun.transform.rotation = Quaternion.Euler(sunEuler[i].x, sunEuler[i].y, sunEuler[i].z);
                if (sunColor != null && i < sunColor.Length) sun.color = sunColor[i];
                if (sunIntensity != null && i < sunIntensity.Length) sun.intensity = sunIntensity[i];
            }
            if (shadowColor != null && i < shadowColor.Length)
            {
                if (!idReady) { shadowId = VRCShader.PropertyToID("_Udon_ShadowColor"); idReady = true; }
                Color c = shadowColor[i];
                VRCShader.SetGlobalColor(shadowId, new Color(c.r, c.g, c.b, 1f));
            }
        }
    }
}
