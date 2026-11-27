using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 厩舎前の柵につながれた馬（毛色ごと＋骨の馬）。近づいて Interact でその毛色を選ぶ（乗り物は馬になる）。
    /// 選んだ馬には自分にだけ目印（chosenMarker：鞍・リボンなど）が付く。visual があれば Start でその毛色を着せる。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MountPicker : UdonSharpBehaviour
    {
        public RaceManager raceManager;
        [Tooltip("0 鹿毛 / 1 栗毛 / 2 青毛 / 3 芦毛 / 4 月毛 / 5 河原毛 / 6 骨の馬")]
        public int coat;
        [Tooltip("つながれた馬の見た目（任意）")]
        public MountVisual visual;
        [Tooltip("選んだときだけ自分に見える目印（任意）")]
        public GameObject chosenMarker;

        private void Start()
        {
            if (visual != null) visual.Apply(MountMath.MOUNT_HORSE, coat);
        }

        public override void Interact()
        {
            if (raceManager == null) return;
            raceManager.SelectCoat(coat);
        }

        public void SetChosen(bool chosen)
        {
            if (chosenMarker != null) chosenMarker.SetActive(chosen);
        }
    }
}
