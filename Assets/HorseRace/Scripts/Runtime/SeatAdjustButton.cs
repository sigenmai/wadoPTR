using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 鞍の調整札のボタン 1 つ（見えない箱の当たり判定＋文字）。Interact（デスクトップのクリック・VR のレーザー）で 1 刻み。
    /// VR の指先で触れたときの押し続けは SeatFit が直接見る。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SeatAdjustButton : UdonSharpBehaviour
    {
        public SeatFit seatFit;
        [Tooltip("SeatFit.BTN_*（0 高さ＋ / 1 高さ− / 2 前 / 3 後 / 4 前傾 / 5 後傾 / 6 元に戻す）")]
        public int button;

        public override void Interact()
        {
            if (seatFit != null) seatFit.Press(button);
        }
    }
}
