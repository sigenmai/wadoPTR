using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// 配置の手直し（Task 11e）。
    ///   ・Build World は飾りに安定した id（名前の末尾 " {id}"）を付け、計算どおりの姿勢を Generated/LayoutBaseline.json に書いてから、
    ///     Layout/LayoutOverrides.json の手直し（移動・回転・拡大・削除）を当てる。
    ///   ・メニュー「HorseRace/Save Layout」：シーンの今の飾りと基準（LayoutBaseline.json）を比べ、違いを LayoutOverrides.json に書く（シーンは作り直さない）。
    ///   ・メニュー「HorseRace/Reset Layout」：確認のあと LayoutOverrides.json を消す（次の Build World は自動の配置だけ）。
    ///   ・シーン直下の "HorseRace_Custom" の子は Build World が一切触らない（自分で置いた物はここへ）。
    /// 基準は「計画をもう一度計算する」代わりに、Build World が作った直後（手直しを当てる前）の姿勢を保存したもの。手配書のように
    /// レイキャストで決まる物も含めて同じ方法で比べられる。計画の入力（ステージ定義・素材）が変わると id や基準が変わるので、
    /// そのときは不明な id が Console に出る。
    /// </summary>
    public static class LayoutTools
    {
        public const string BASELINE = GenUtil.GENERATED + "/LayoutBaseline.json";
        public const string CUSTOM_ROOT = "HorseRace_Custom";

        /// <summary>シーン直下の HorseRace_Custom（無ければ空で作る）。Build World はこの中を触らない。</summary>
        public static GameObject EnsureCustomRoot()
        {
            foreach (GameObject g in SceneManager.GetActiveScene().GetRootGameObjects())
                if (g.name == CUSTOM_ROOT) return g;
            var go = new GameObject(CUSTOM_ROOT);
            Debug.Log("[HorseRace] シーン直下に " + CUSTOM_ROOT + " を作りました。自分で置く物はこの子にすると Build World で消えません");
            return go;
        }

        static LayoutItem ItemOf(string id, Transform t)
            => new LayoutItem { id = id, position = t.position, euler = t.rotation.eulerAngles, scale = t.localScale };

        /// <summary>Build World 中：id を付けた飾りの「計算どおりの姿勢」を基準として保存する（手直しを当てる前に呼ぶ）。</summary>
        public static void WriteBaseline()
        {
            var items = new List<LayoutOverride>();
            foreach (var kv in GenUtil.Tagged)
            {
                if (kv.Value == null) continue;
                Transform t = kv.Value.transform;
                items.Add(new LayoutOverride { id = kv.Key, position = t.position, euler = t.rotation.eulerAngles, scale = t.localScale });
            }
            GenUtil.EnsureFolder(GenUtil.GENERATED);
            File.WriteAllText(BASELINE, LayoutOverrides.ToJson(items));
            AssetDatabase.ImportAsset(BASELINE);
        }

        static List<LayoutOverride> ReadOverrides(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                return LayoutOverrides.FromJson(File.ReadAllText(path));
            }
            catch (System.Exception e)
            {
                Debug.LogError("[HorseRace] " + path + " を読めませんでした（" + e.Message + "）。手直しは当てません");
                return null;
            }
        }

        /// <summary>Build World 中：LayoutOverrides.json の手直しを id を付けた飾りに当てる。数と不明な id を Console に出す。</summary>
        public static void ApplyOverrides()
        {
            var overrides = ReadOverrides(LayoutOverrides.FILE);
            if (overrides == null || overrides.Count == 0) return;
            int moved = 0, deleted = 0;
            var unknown = new List<string>();
            foreach (var o in overrides)
            {
                GameObject go;
                if (!GenUtil.Tagged.TryGetValue(o.id, out go) || go == null) { unknown.Add(o.id); continue; }
                Transform t = go.transform;
                Transform walls = null;   // id に "/" が入るので Transform.Find は使わない
                if (t.parent != null)
                    for (int c = 0; c < t.parent.childCount; c++)
                        if (t.parent.GetChild(c).name == go.name + "_InvisibleWalls") { walls = t.parent.GetChild(c); break; }
                if (o.deleted)
                {
                    if (walls != null) Object.DestroyImmediate(walls.gameObject);
                    Object.DestroyImmediate(go);
                    GenUtil.Tagged.Remove(o.id);
                    deleted++;
                    continue;
                }
                Vector3 oldPos = t.position;
                Quaternion oldRot = t.rotation;
                Quaternion newRot = Quaternion.Euler(o.euler);
                t.SetPositionAndRotation(o.position, newRot);
                t.localScale = o.scale;
                if (walls != null)
                {
                    // 背景専用の見えない壁も一緒に動かす（拡大の変化は壁には当てない）
                    Quaternion d = newRot * Quaternion.Inverse(oldRot);
                    walls.SetPositionAndRotation(o.position + d * (walls.position - oldPos), d * walls.rotation);
                }
                moved++;
            }
            Debug.Log("[HorseRace] 配置の手直しを当てました：移動 " + moved + " 個・削除 " + deleted + " 個（" + LayoutOverrides.FILE + "）");
            if (unknown.Count > 0)
            {
                int show = Mathf.Min(unknown.Count, 20);
                Debug.LogWarning("[HorseRace] 手直しのうち " + unknown.Count + " 件は今の配置に無い id です（ステージ定義や素材が変わった可能性。Save Layout で書き直すと消えます）:\n  "
                    + string.Join("\n  ", unknown.GetRange(0, show).ToArray()) + (unknown.Count > show ? "\n  …" : ""));
            }
        }

        /// <summary>シーンの今の飾りと基準の違い。基準が無い・壊れている・ルートが無いときは false と理由。</summary>
        static bool SceneDiff(out List<LayoutOverride> diff, out int tagged, out List<string> broken, out string problem)
        {
            diff = null; tagged = 0; broken = new List<string>(); problem = null;
            GameObject root = null;
            foreach (GameObject g in SceneManager.GetActiveScene().GetRootGameObjects())
                if (g.name == GenUtil.ROOT_NAME) root = g;
            if (root == null || !File.Exists(BASELINE)) { problem = "先に「HorseRace/Build World」を実行してください（配置の基準がありません）。"; return false; }
            List<LayoutOverride> baseline;
            try { baseline = LayoutOverrides.FromJson(File.ReadAllText(BASELINE)); }
            catch (System.Exception e)
            {
                problem = "配置の基準ファイル（" + BASELINE + "）が壊れています（" + e.Message + "）。\n「HorseRace/Build World」をやり直すと作り直されます（その前に手で直した分は保存できません）。";
                return false;
            }
            var expected = new List<LayoutItem>();
            foreach (var b in baseline) expected.Add(new LayoutItem { id = b.id, position = b.position, euler = b.euler, scale = b.scale });
            var actual = new List<LayoutItem>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                string id = LayoutOverrides.ParseId(t.name);
                if (id != null) { actual.Add(ItemOf(id, t)); tagged++; }
                else if (LayoutOverrides.LooksLikeBrokenId(t.name)) broken.Add(t.name);
            }
            diff = LayoutOverrides.Diff(expected, actual);
            return true;
        }

        static void Count(List<LayoutOverride> diff, out int moved, out int deleted)
        {
            moved = 0; deleted = 0;
            foreach (var d in diff) if (d.deleted) deleted++; else moved++;
        }

        static void WriteOverrides(List<LayoutOverride> diff)
        {
            GenUtil.EnsureFolder(Path.GetDirectoryName(LayoutOverrides.FILE).Replace('\\', '/'));
            File.WriteAllText(LayoutOverrides.FILE, LayoutOverrides.ToJson(diff));
            AssetDatabase.ImportAsset(LayoutOverrides.FILE);
        }

        /// <summary>
        /// Build World の前：シーンに保存していない配置の変更があれば聞く。true = 続ける（保存した or 破棄した or 変更なし）、false = やめる。
        /// </summary>
        public static bool GuardUnsavedEdits()
        {
            List<LayoutOverride> diff; int tagged; List<string> broken; string problem;
            if (!SceneDiff(out diff, out tagged, out broken, out problem)) return true;   // 基準やルートが無い＝比べるものがない
            var saved = ReadOverrides(LayoutOverrides.FILE) ?? new List<LayoutOverride>();
            if (LayoutOverrides.SameOverrides(diff, saved)) return true;
            int moved, deleted;
            Count(diff, out moved, out deleted);
            int choice = EditorUtility.DisplayDialogComplex("HorseRace/Build World",
                "保存していない配置の変更があります（移動 " + moved + "・削除 " + deleted + "）。\nBuild World はシーンを作り直すので、保存しないと消えます。",
                "保存して続ける", "キャンセル", "破棄して続ける");
            if (choice == 1) return false;
            if (choice == 0)
            {
                WriteOverrides(diff);
                Debug.Log("[HorseRace] Build World の前に配置の変更を保存しました：移動 " + moved + " 個・削除 " + deleted + " 個");
            }
            else Debug.Log("[HorseRace] 保存していない配置の変更を破棄しました（移動 " + moved + "・削除 " + deleted + "）");
            return true;
        }

        [MenuItem("HorseRace/Save Layout")]
        public static void SaveLayout()
        {
            List<LayoutOverride> diff; int tagged; List<string> broken; string problem;
            if (!SceneDiff(out diff, out tagged, out broken, out problem))
            {
                EditorUtility.DisplayDialog("HorseRace/Save Layout", problem, "OK");
                return;
            }
            int moved, deleted;
            Count(diff, out moved, out deleted);
            var deletedIds = new List<string>();
            foreach (var d in diff) if (d.deleted) deletedIds.Add(d.id);
            string list = "";
            if (deletedIds.Count > 0)
            {
                int show = Mathf.Min(10, deletedIds.Count);
                list = "\n\n削除になる飾り（" + deletedIds.Count + " 個" + (deletedIds.Count > show ? "、最初の 10 個" : "") + "）:\n  " + string.Join("\n  ", deletedIds.GetRange(0, show).ToArray());
            }
            string brokenNote = "";
            if (broken.Count > 0)
                brokenNote = "\n\n名前の末尾の id（{…}）が変わっている物が " + broken.Count + " 個あります（例: " + broken[0] + "）。"
                    + "名前を変えた・複製した物は手直しの対象外で、元の物は「削除」として保存されます。複製した物は HorseRace_Custom へ移してください。";
            bool many = deleted > 20 || (tagged + deleted > 0 && deleted > 0.1f * (tagged + deleted));
            if (many && !EditorUtility.DisplayDialog("HorseRace/Save Layout",
                    "削除が多いです（" + deleted + " 個）。名前を変えたり親を変えたりしていませんか？" + list + brokenNote + "\n\nこのまま保存しますか？", "保存する", "やめる"))
                return;
            WriteOverrides(diff);
            string msg = "移動 " + moved + " 個・削除 " + deleted + " 個を保存しました。\n（" + LayoutOverrides.FILE + "）\n次の Build World でもこの配置になります。" + (many ? "" : list) + brokenNote;
            Debug.Log("[HorseRace] " + msg);
            EditorUtility.DisplayDialog("HorseRace/Save Layout", msg, "OK");
        }

        [MenuItem("HorseRace/Reset Layout")]
        public static void ResetLayout()
        {
            if (!File.Exists(LayoutOverrides.FILE))
            {
                EditorUtility.DisplayDialog("HorseRace/Reset Layout", "手直しのファイルはありません（自動の配置のままです）。", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("HorseRace/Reset Layout",
                "手で直した配置（" + LayoutOverrides.FILE + "）を消します。\n次の Build World から自動の配置に戻ります。よろしいですか？", "消す", "やめる"))
                return;
            AssetDatabase.DeleteAsset(LayoutOverrides.FILE);
            Debug.Log("[HorseRace] 配置の手直しを消しました。HorseRace/Build World で自動の配置に戻ります");
        }
    }
}
