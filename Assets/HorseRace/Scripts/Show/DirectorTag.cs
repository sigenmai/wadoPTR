using TMPro;
using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 掲示板の下に吊るした木札。Interact で中継の追う相手をこのクライアントだけ切り替える。
    /// followRank = 1..8 で「○位を追う」、0 で「おまかせ」。Interaction Text に札の文言（「1位を追う」等）を入れる。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class DirectorTag : UdonSharpBehaviour
    {
        public SpectatorDirector director;
        [Tooltip("0 = おまかせ、1..8 = その順位を追う")]
        public int followRank;
        [Tooltip("任意。選ばれているとき表示する印（焼き印・赤い紐など）")]
        public GameObject selectedMark;
        [Tooltip("札の文字（ui-r2-14：「2位 名前」に書き換える）")]
        public TextMeshPro label;
        [Tooltip("レース外の文字（「2位」「おまかせ」）")]
        public string baseLabel = "";

        private bool selected;
        private bool tagVisible = true;
        private string shownText = "";

        /// <summary>札を出す／隠す（エントリー数より大きい順位の札は隠す。ui-r2-14）。隠すと選択の印も隠す。</summary>
        public void SetVisible(bool v)
        {
            if (v == tagVisible) return;
            tagVisible = v;
            gameObject.SetActive(v);
        }

        /// <summary>札の文字（空なら baseLabel）。</summary>
        public void SetLabel(string text)
        {
            if (label == null) return;
            string t = text != null && text.Length > 0 ? text : baseLabel;
            if (t == shownText) return;
            shownText = t;
            label.text = t;
        }

        public override void Interact()
        {
            if (director != null) director.SetOverride(followRank);
        }

        public void SetSelected(bool sel)
        {
            selected = sel;
            if (selectedMark != null) selectedMark.SetActive(sel);
        }
    }
}
