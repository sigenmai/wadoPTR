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

        public override void Interact()
        {
            if (director != null) director.SetOverride(followRank);
        }

        public void SetSelected(bool selected)
        {
            if (selectedMark != null) selectedMark.SetActive(selected);
        }
    }
}
