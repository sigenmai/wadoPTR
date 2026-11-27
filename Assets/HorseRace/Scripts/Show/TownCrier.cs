using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// 新聞売り。ステージ結果（STAGE_RESULT）に入ったら呼び声を鳴らし、掲示板（NoticeBoardDisplay mode 1）に着順の電報を貼る。
    /// RaceManager の同期状態を毎フレーム見て、stageSeq ごとに 1 回だけ（ローカル）。
    /// 途中参加で結果表示の途中に入った人には電報だけ貼り、呼び声は鳴らさない。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TownCrier : UdonSharpBehaviour
    {
        /// <summary>状態に入ってからこの秒数以内なら呼び声を鳴らす（遅れて入った人には鳴らさない）。</summary>
        public const float FRESH_SECONDS = 3f;

        public RaceManager raceManager;
        [Tooltip("呼び声の AudioSource（全員に聞こえるよう 2D にするか、たまり場を覆う距離に）")]
        public AudioSource callAudio;
        [Tooltip("任意。設定すると PlayOneShot、未設定なら callAudio.Play()")]
        public AudioClip callClip;
        [Tooltip("電報を貼る掲示板（mode 1）")]
        public NoticeBoardDisplay[] boards;
        [Tooltip("任意。新聞売りの人形の手（紙を掲げる）")]
        public Transform paperArm;
        public Vector3 paperArmRaisedEuler = new Vector3(-100f, 0f, 0f);
        public float paperRaiseSeconds = 4f;

        private int postedSeq = -1;
        private float raiseUntil;
        private Quaternion armRest;
        private bool armCaptured;

        private void Update()
        {
            RaceManager rm = raceManager;
            if (rm == null) return;
            if (rm.state == RaceMath.STATE_STAGE_RESULT && rm.stageSeq != postedSeq)
            {
                postedSeq = rm.stageSeq;
                Post(rm);
            }
            AnimateArm();
        }

        private void Post(RaceManager rm)
        {
            string text = StageTelegram(rm);
            if (boards != null)
            {
                for (int i = 0; i < boards.Length; i++)
                    if (boards[i] != null) boards[i].PostTelegram(text, rm.stageSeq);
            }
            double elapsed = Networking.GetServerTimeInSeconds() - rm.stateStartServer;
            if (elapsed > FRESH_SECONDS) return;
            if (callAudio != null)
            {
                if (callClip != null) callAudio.PlayOneShot(callClip);
                else callAudio.Play();
            }
            raiseUntil = Time.time + paperRaiseSeconds;
        }

        private void AnimateArm()
        {
            if (paperArm == null) return;
            if (!armCaptured)
            {
                armRest = paperArm.localRotation;
                armCaptured = true;
            }
            Quaternion goal = Time.time < raiseUntil ? armRest * Quaternion.Euler(paperArmRaisedEuler) : armRest;
            paperArm.localRotation = Quaternion.Slerp(paperArm.localRotation, goal, 1f - Mathf.Exp(-8f * Time.deltaTime));
        }

        /// <summary>今のステージの着順を電報の文面にする（RaceManager の slotStagePlace / slotTotalPoints から）。</summary>
        public static string StageTelegram(RaceManager rm)
        {
            string s = "電報\n" + RaceManager.StageLabel(rm.stage) + " 着順\n";
            int lines = 0;
            for (int place = 1; place <= RaceManager.MAX_SLOTS; place++)
            {
                for (int i = 0; i < RaceManager.MAX_SLOTS; i++)
                {
                    if (rm.slotPlayerId[i] < 0 || rm.slotStagePlace[i] != place) continue;
                    s += place.ToString() + "着 " + RaceManager.PlayerName(rm.slotPlayerId[i])
                        + "　+" + RaceMath.PointsForPlace(place - 1).ToString() + "点（計" + rm.slotTotalPoints[i].ToString() + "点）\n";
                    lines++;
                }
            }
            if (lines == 0) s += "（出走者なし）\n";
            s += "― 町の新聞売り";
            return s;
        }
    }
}
