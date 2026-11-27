// Extensions of the TextMeshPro 3.0 stubs used by Assets/HorseRace/Editor. Type-check only.
using UnityEngine;

namespace TMPro
{
    public enum TextAlignmentOptions { TopLeft = 257, Top = 258, Left = 513, Center = 514, Right = 516, Midline = 4610 }

    /// <summary>Real: TMPro.TMP_FontAsset : TMP_Asset (ScriptableObject). CreateFontAsset(Font) creates a dynamic font asset (TMP 3.0).</summary>
    public class TMP_FontAsset : ScriptableObject
    {
        public Material material;
        public Texture2D[] atlasTextures;
        public Texture2D atlasTexture => null;
        public static TMP_FontAsset CreateFontAsset(Font font) => null;
    }

    public abstract partial class TMP_Text
    {
        public TextAlignmentOptions alignment { get; set; }
        public bool enableWordWrapping { get; set; }
        public TMP_FontAsset font { get; set; }
        public RectTransform rectTransform => null;
    }
}
