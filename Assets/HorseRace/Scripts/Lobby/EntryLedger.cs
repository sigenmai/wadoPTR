using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// 受付カウンターの台帳（机に平らに置いた手配書の紙 2 枚 = 見開き）。Interact ＝ サイン ＝ 手配書・柵の馬で選んだ内容でマスターへエントリーを依頼する。
    ///  - 左のページ（pageText、インクの文字・自動で縮む）：エントリー者の一覧（1 人 1 行、名前は 10 文字まで）・次のステージからの人・状況。
    ///  - 右のページ（choiceText、自分だけ）：「あなたの選択：馬（栗毛）・投げ縄 ― 台帳に触れてサイン」。
    ///    サインが通ったら赤いインクで「署名済み ✓」、定員なら「定員いっぱい」。サインした瞬間にペンの音と文字が 1.15 → 1 に跳ねる。
    /// ページの更新は RaceManager が同期を受け取るたび・自分の選択が変わるたびに Refresh() で行う。
    /// ステージ中のサインは「次のステージから」の欄に載り、ステージ結果に入ると枠へ移る（RaceManager.pendingPlayerId）。
    /// 取り消し（system-r2-05）：署名済みで選択を変えていなければ、もう一度触れると「取り消す」（RaceManager.WithdrawLedger）。
    /// 選択を変えてから触れるとサインし直し（乗り物・能力の変更）。出走しなかった人は一覧に「（不在）」。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class EntryLedger : UdonSharpBehaviour
    {
        public RaceManager raceManager;
        [Tooltip("左のページ（エントリー一覧、TextMeshPro（3D））")]
        public TextMeshPro pageText;
        [Tooltip("右のページ（自分の選択・署名済み、ローカル）")]
        public TextMeshPro choiceText;
        [Tooltip("ペンの音（任意）")]
        public AudioSource signSound;

        private float punchAt = -10f;
        private Vector3 choiceScale = Vector3.one;
        private bool scaleCaptured;
        private bool punching;

        private void Start()
        {
            Refresh();
        }

        public override void Interact()
        {
            if (raceManager == null) return;
            if (signSound != null) signSound.Play();
            if (WillWithdraw(raceManager)) raceManager.WithdrawLedger();
            else raceManager.SignLedger();
            punchAt = Time.time;
            punching = true;
        }

        /// <summary>今触れると取り消しになるか：署名済み（枠か預かり）で、選択がサインした内容と同じ（変えていない）。</summary>
        public static bool WillWithdraw(RaceManager rm)
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return false;
            if (rm.IsPending(local.playerId)) return true;
            int s = rm.FindSlot(local.playerId);
            if (s < 0) return false;
            if (rm.state != RaceMath.STATE_LOBBY && rm.state != RaceMath.STATE_STAGE_RESULT) return false;
            int coat = rm.localMount == MountMath.MOUNT_CAMEL ? 0 : rm.localCoat;
            return rm.slotMount[s] == rm.localMount && rm.slotCoat[s] == coat && rm.slotAbility[s] == rm.localAbility;
        }

        private void Update()
        {
            if (!punching || choiceText == null) return;
            Transform t = choiceText.transform;
            if (!scaleCaptured) { choiceScale = t.localScale; scaleCaptured = true; }
            float e = Time.time - punchAt;
            if (e >= 0.4f) { punching = false; t.localScale = choiceScale; return; }
            t.localScale = choiceScale * HudMath.PunchScale(e, 1.15f);
        }

        public void Refresh()
        {
            if (raceManager == null) return;
            RaceManager rm = raceManager;
            int st = rm.state;
            int count = 0;
            if (pageText != null)
            {
                string s = "<b><size=140%>エントリー台帳</size></b>\n";
                for (int i = 0; i < RaceManager.MAX_SLOTS; i++)
                {
                    int pid = rm.slotPlayerId[i];
                    if (pid < 0) continue;
                    count++;
                    string pts = st != RaceMath.STATE_LOBBY ? rm.slotTotalPoints[i].ToString() + "点" : "";
                    if (rm.IsNoStart(i)) pts = "（不在）";   // system-r2-05：次の合間で外れる
                    // 列：番号 0% / 名前 7% / 乗り物・能力 50%（80%）/ 点 86%
                    s += "<pos=0%>" + count.ToString() + ".<pos=7%>" + RaceManager.ShortName(pid) + "<pos=50%><size=80%>"
                        + RaceManager.MountKindLabel(rm.slotMount[i]) + "・" + RaceManager.AbilityLabel(rm.slotAbility[i]) + "</size><pos=86%>" + pts + "\n";
                }
                if (count == 0) s += "（まだ誰もサインしていない）\n";
                string pend = "";
                for (int q = 0; q < RaceManager.MAX_SLOTS; q++)
                {
                    int pid = rm.pendingPlayerId[q];
                    if (pid < 0) continue;
                    if (pend.Length > 0) pend += "、";
                    pend += RaceManager.ShortName(pid);
                }
                if (pend.Length > 0) s += "次のステージから：" + pend + "\n";
                if (st == RaceMath.STATE_LOBBY) s += "<i>手配書と柵の馬で選んでサイン。号砲で出走。</i>";
                else if (st == RaceMath.STATE_STAGE_RESULT && rm.stage + 1 < RaceManager.STAGE_COUNT) s += "<i>ステージの合間：今サインすれば次から走れる。</i>";
                else if (st == RaceMath.STATE_STAGE_RESULT) s += "<i>最終ステージの結果発表中。次のレースはロビーで。</i>";
                else if (st == RaceMath.STATE_FINAL_RESULT) s += "<i>総合結果の発表中。</i>";
                else if (rm.stage + 1 < RaceManager.STAGE_COUNT) s += "<i>" + RaceManager.StageLabel(rm.stage) + " 出走中。今サインすれば次から走れる。</i>";
                else s += "<i>" + RaceManager.StageLabel(rm.stage) + "（最終）出走中。次のレースはロビーで。</i>";
                pageText.text = s;
            }
            else
            {
                for (int i = 0; i < RaceManager.MAX_SLOTS; i++) if (rm.slotPlayerId[i] >= 0) count++;
            }
            if (choiceText != null) choiceText.text = ChoiceText(rm, count);
            string prompt = WillWithdraw(rm) ? "台帳：取り消す" : "台帳にサイン";
            if (InteractionText != prompt) InteractionText = prompt;
        }

        private string ChoiceText(RaceManager rm, int count)
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            int pid = local != null ? local.playerId : -1;
            int coat = rm.localMount == MountMath.MOUNT_CAMEL ? 0 : rm.localCoat;
            string choice = "あなたの選択：\n" + RaceManager.MountLabel(rm.localMount, coat) + "・" + RaceManager.AbilityLabel(rm.localAbility);
            string undo = WillWithdraw(rm) ? "\n<size=80%>― もう一度触れると取り消す</size>" : "";
            if (rm.FindSlot(pid) >= 0)
                return "<color=#B81F14><size=170%><b>署名済み ✓</b></size></color>\n" + choice + undo;
            if (rm.IsPending(pid))
                return "<color=#B81F14><size=150%><b>署名済み ✓</b></size></color>\n次のステージから\n" + choice + undo;
            if (rm.SlotCount() > 0 && count >= rm.SlotCount())
                return "<color=#B81F14><size=150%><b>定員いっぱい</b></size></color>\n" + choice;
            return choice + "\n<size=80%>― 台帳に触れてサイン</size>";
        }
    }
}
