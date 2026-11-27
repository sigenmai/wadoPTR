using UnityEditor;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// Build World が使うレイヤー。名前が TagManager に無ければ空いているユーザーレイヤー 23〜26 に作る
    /// （VRChat はユーザーレイヤー 22〜31 を自由に使える。0〜21 は Unity / VRChat の予約）。
    ///   Ground … 地形・練習場の坂（乗り物が立てる面）。MountController / AbilityCaster / 岩壁 / 鉄球 / 遺物の groundMask
    ///   Mount  … 乗り物の体の Collider（AbilityCaster の targetMask、投げ縄の mountMask）
    ///   Rider  … 騎手の上半身・カカシの当たり判定（targetMask のみ。物理の衝突は全レイヤーと無効＝クエリ専用）
    ///   Block  … 岩壁・岩・崖・建物・見えない壁（MountController の obstacleMask、AbilityCaster の blockMask は Ground|Block）
    /// </summary>
    public static class WorldLayers
    {
        public static int Ground = 23, Mount = 24, Rider = 25, Block = 26;
        public const int VRC_PLAYER = 9, VRC_PLAYER_LOCAL = 10;
        /// <summary>Unity 組み込みの Ignore Raycast。大きなトリガー（CP・砂地・練習場・遺物）はここに置く
        /// （Default のトリガーは VRChat のインタラクトのレイを遮り、中の杭や木札に触れなくなるため）。</summary>
        public const int TRIGGERS = 2;

        public static int GroundMask => 1 << Ground;
        public static int MountMask => 1 << Mount;
        public static int TargetMask => (1 << Mount) | (1 << Rider);
        public static int BlockMask => (1 << Ground) | (1 << Block);
        public static int ObstacleMask => 1 << Block;

        /// <summary>レイヤーを用意して番号を決め、衝突マトリクスを設定する。失敗したら false。</summary>
        public static bool Ensure()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) { Debug.LogError("[HorseRace] TagManager.asset を開けません"); return false; }
            var so = new SerializedObject(assets[0]);
            SerializedProperty layers = so.FindProperty("layers");
            if (layers == null) { Debug.LogError("[HorseRace] TagManager に layers がありません"); return false; }
            int[] preferred = { 23, 24, 25, 26 };
            string[] names = { "Ground", "Mount", "Rider", "Block" };
            int[] result = new int[4];
            for (int k = 0; k < names.Length; k++)
            {
                int found = -1;
                for (int i = 0; i < layers.arraySize; i++)
                    if (layers.GetArrayElementAtIndex(i).stringValue == names[k]) { found = i; break; }
                if (found < 0)
                {
                    int slot = preferred[k];
                    string cur = layers.GetArrayElementAtIndex(slot).stringValue;
                    if (!string.IsNullOrEmpty(cur))
                    {
                        // 予定の番号が別名で使われている：22〜31 の空きを探す
                        slot = -1;
                        for (int i = 22; i < 32; i++)
                            if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) { slot = i; break; }
                    }
                    if (slot < 0) { Debug.LogError("[HorseRace] 空きのユーザーレイヤーがありません（" + names[k] + "）"); return false; }
                    layers.GetArrayElementAtIndex(slot).stringValue = names[k];
                    found = slot;
                }
                result[k] = found;
            }
            so.ApplyModifiedProperties();
            Ground = result[0]; Mount = result[1]; Rider = result[2]; Block = result[3];

            // Rider は当たり判定のクエリ専用（CP・砂地・遺物のトリガーを二重に踏まない）
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(Rider, i, true);
            // 乗っている本人のカプセルが乗り物の体に押されない
            Physics.IgnoreLayerCollision(Mount, VRC_PLAYER_LOCAL, true);
            Physics.IgnoreLayerCollision(Mount, VRC_PLAYER, true);
            return true;
        }
    }
}
