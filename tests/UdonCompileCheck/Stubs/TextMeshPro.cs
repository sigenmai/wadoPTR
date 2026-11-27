// Hand-written stubs mirroring the TextMeshPro (com.unity.textmeshpro 3.0) and uGUI types used by Assets/HorseRace/Scripts.
// Real hierarchy: TMP_Text : MaskableGraphic : Graphic : UIBehaviour : MonoBehaviour. Type-check only.
using UnityEngine;

namespace UnityEngine.EventSystems
{
    public abstract class UIBehaviour : MonoBehaviour { }
}

namespace UnityEngine.UI
{
    public abstract class Graphic : UnityEngine.EventSystems.UIBehaviour
    {
        public virtual Color color { get; set; }
    }

    public abstract class MaskableGraphic : Graphic { }
}

namespace TMPro
{
    public abstract class TMP_Text : UnityEngine.UI.MaskableGraphic
    {
        public virtual string text { get; set; }
        public float fontSize { get; set; }
    }

    public class TextMeshPro : TMP_Text { }

    public class TextMeshProUGUI : TMP_Text { }
}
