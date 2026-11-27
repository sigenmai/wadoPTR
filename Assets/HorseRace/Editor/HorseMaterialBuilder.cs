using UnityEditor;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// メニュー「HorseRace/Create Horse Materials」で、トゥーンシェーダーを使った毛色マテリアル6種を作る。
    /// 元テクスチャは鹿毛（赤茶＋黒いたてがみ）。色相・彩度・明るさで他の毛色に寄せる。
    /// </summary>
    public static class HorseMaterialBuilder
    {
        const string ShaderName = "HorseRace/Toon";
        const string TexturePath = "Assets/HorseRace/Models/Textures/Horse_BaseColor.jpg";
        const string OutDir = "Assets/HorseRace/Materials/Horse";

        // 名前, 色相シフト, 彩度, 明るさ
        static readonly (string name, float hue, float sat, float bright)[] Coats =
        {
            ("Horse_Bay",      0.00f, 1.00f, 1.00f), // 鹿毛
            ("Horse_Chestnut", 0.03f, 1.10f, 1.30f), // 栗毛
            ("Horse_Black",    0.00f, 0.30f, 0.35f), // 青毛
            ("Horse_Gray",     0.00f, 0.05f, 2.40f), // 芦毛
            ("Horse_Palomino", 0.08f, 0.90f, 2.00f), // 月毛
            ("Horse_Dun",      0.06f, 0.50f, 1.70f), // 河原毛
        };

        [MenuItem("HorseRace/Create Horse Materials")]
        public static void Create()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) { Debug.LogError($"[HorseRace] シェーダー {ShaderName} が見つかりません"); return; }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (tex == null) { Debug.LogError($"[HorseRace] テクスチャ {TexturePath} が見つかりません"); return; }

            if (!AssetDatabase.IsValidFolder("Assets/HorseRace/Materials"))
                AssetDatabase.CreateFolder("Assets/HorseRace", "Materials");
            if (!AssetDatabase.IsValidFolder(OutDir))
                AssetDatabase.CreateFolder("Assets/HorseRace/Materials", "Horse");

            foreach (var c in Coats)
            {
                string path = $"{OutDir}/{c.name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.shader = shader;
                mat.SetTexture("_MainTex", tex);
                mat.SetFloat("_HueShift", c.hue);
                mat.SetFloat("_Saturation", c.sat);
                mat.SetFloat("_Brightness", c.bright);
                EditorUtility.SetDirty(mat);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[HorseRace] 毛色マテリアルを {Coats.Length} 個作成しました: {OutDir}");
        }
    }
}
