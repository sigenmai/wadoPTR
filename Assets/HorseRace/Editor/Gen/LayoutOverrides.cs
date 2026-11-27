using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>手で直した配置 1 件（ワールド座標の位置・回転（オイラー角）・拡大、または削除）。</summary>
    public sealed class LayoutOverride
    {
        public string id;
        public bool deleted;
        public Vector3 position;
        public Vector3 euler;
        public Vector3 scale = new Vector3(1f, 1f, 1f);
    }

    /// <summary>配置 1 つの姿勢（id とワールドの位置・回転・拡大）。Build World の「計算どおりの配置」と、シーンの今の配置の両方に使う。</summary>
    public sealed class LayoutItem
    {
        public string id;
        public Vector3 position;
        public Vector3 euler;
        public Vector3 scale = new Vector3(1f, 1f, 1f);
    }

    /// <summary>
    /// 配置の手直し（UnityEditor に依存しない。tests/HorseRace.Tests で単体テストする）。
    /// 飾り（ステージ・遠景・ロビー・木の帯・柵・手配書・見どころの岩など）は安定した id を持つ：
    ///   "&lt;範囲&gt;/&lt;グループ&gt;/&lt;素材ファイル名&gt;|&lt;部品&gt;#&lt;計画の中の番号&gt;"（計画の入力が同じなら同じ id）。
    /// id は GameObject の名前の末尾に " {id}" として付ける（実行時のコンポーネントを足さないので VRChat のアップロードに影響しない）。
    /// 手直しは Assets/HorseRace/Layout/LayoutOverrides.json（id → 位置・回転・拡大 または 削除）。
    /// 遊びに関わるもの（CP・遺物・スタート・ゴール後の囲い・ゴールゲート・スターター・表彰台・ロビーの仕掛け・乗り物）には id を付けない＝手直しできない。
    /// </summary>
    public static class LayoutOverrides
    {
        public const string FILE = "Assets/HorseRace/Layout/LayoutOverrides.json";
        public const float POS_EPS = 0.01f, ANGLE_EPS = 0.1f, SCALE_EPS = 0.001f;

        // ------------------------------------------------------------------
        // id
        // ------------------------------------------------------------------

        /// <summary>計画の配置の id（scope = "S1" "Backdrop" "Lobby" "Belt" など）。</summary>
        public static string IdFor(string scope, Placement p, int index)
        {
            string file = p.asset;
            int slash = file.LastIndexOf('/');
            if (slash >= 0) file = file.Substring(slash + 1);
            if (file.EndsWith(".fbx")) file = file.Substring(0, file.Length - 4);
            return scope + "/" + (p.group ?? "Misc") + "/" + file + "|" + (p.part ?? "") + "#" + index;
        }

        public static List<string> IdsFor(string scope, List<Placement> plan)
        {
            var r = new List<string>(plan.Count);
            for (int i = 0; i < plan.Count; i++) r.Add(IdFor(scope, plan[i], i));
            return r;
        }

        /// <summary>名前の末尾に id を付ける（"表示名 {id}"）。</summary>
        public static string NameWithId(string display, string id) => display + " {" + id + "}";

        /// <summary>名前の末尾の " {id}" を取り出す。無ければ null。</summary>
        public static string ParseId(string name)
        {
            if (string.IsNullOrEmpty(name) || !name.EndsWith("}")) return null;
            int open = name.LastIndexOf(" {");
            if (open < 0) return null;
            return name.Substring(open + 2, name.Length - open - 3);
        }

        /// <summary>計画の配置 → 計算どおりの姿勢（origin = 計画の原点。回転は Euler(0, yaw, roll)、拡大は一様）。</summary>
        public static LayoutItem ItemFor(string id, Placement p, Vector3 origin)
            => new LayoutItem { id = id, position = origin + p.position, euler = new Vector3(0f, p.yawDeg, p.rollDeg), scale = new Vector3(p.scale, p.scale, p.scale) };

        // ------------------------------------------------------------------
        // 比べる・当てる
        // ------------------------------------------------------------------

        static float AngleDiff(float a, float b)
        {
            float d = (a - b) % 360f;
            if (d > 180f) d -= 360f;
            if (d < -180f) d += 360f;
            return Mathf.Abs(d);
        }

        public static bool SameTransform(LayoutItem a, Vector3 pos, Vector3 euler, Vector3 scale)
        {
            return WorldLayout.DistXZ(a.position, pos) <= POS_EPS && Mathf.Abs(a.position.y - pos.y) <= POS_EPS
                && AngleDiff(a.euler.x, euler.x) <= ANGLE_EPS && AngleDiff(a.euler.y, euler.y) <= ANGLE_EPS && AngleDiff(a.euler.z, euler.z) <= ANGLE_EPS
                && Mathf.Abs(a.scale.x - scale.x) <= SCALE_EPS && Mathf.Abs(a.scale.y - scale.y) <= SCALE_EPS && Mathf.Abs(a.scale.z - scale.z) <= SCALE_EPS;
        }

        /// <summary>
        /// 計算どおりの配置 expected と今の配置 actual（シーン）を比べて手直しを作る。actual に無い id は削除、
        /// 姿勢が違うものは今の姿勢で上書き。actual にだけある id は無視（Build World が作っていない物）。
        /// </summary>
        public static List<LayoutOverride> Diff(List<LayoutItem> expected, List<LayoutItem> actual)
        {
            var now = new Dictionary<string, LayoutItem>();
            foreach (var a in actual) if (a.id != null) now[a.id] = a;
            var r = new List<LayoutOverride>();
            foreach (var e in expected)
            {
                LayoutItem a;
                if (!now.TryGetValue(e.id, out a)) { r.Add(new LayoutOverride { id = e.id, deleted = true }); continue; }
                if (SameTransform(e, a.position, a.euler, a.scale)) continue;
                r.Add(new LayoutOverride { id = e.id, position = a.position, euler = a.euler, scale = a.scale });
            }
            return r;
        }

        /// <summary>
        /// 計算どおりの配置に手直しを当てた結果（削除は結果から除く）。plan に無い id の手直しは unknown に入れる。
        /// </summary>
        public static List<LayoutItem> Apply(List<LayoutItem> plan, List<LayoutOverride> overrides, out List<string> unknown, out int moved, out int deleted)
        {
            var byId = new Dictionary<string, LayoutOverride>();
            foreach (var o in overrides) if (o != null && o.id != null) byId[o.id] = o;
            var ids = new HashSet<string>();
            var r = new List<LayoutItem>();
            moved = 0; deleted = 0;
            foreach (var p in plan)
            {
                ids.Add(p.id);
                LayoutOverride o;
                if (!byId.TryGetValue(p.id, out o)) { r.Add(p); continue; }
                if (o.deleted) { deleted++; continue; }
                moved++;
                r.Add(new LayoutItem { id = p.id, position = o.position, euler = o.euler, scale = o.scale });
            }
            unknown = new List<string>();
            foreach (var o in overrides) if (o != null && o.id != null && !ids.Contains(o.id)) unknown.Add(o.id);
            return r;
        }

        // ------------------------------------------------------------------
        // JSON（UnityEngine.JsonUtility を使わない小さな読み書き。テストで往復を確かめる）
        // ------------------------------------------------------------------

        static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        static string Esc(string s)
        {
            var b = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') b.Append('\\').Append(c);
                else if (c == '\n') b.Append("\\n");
                else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
                else b.Append(c);
            }
            return b.ToString();
        }

        public static string ToJson(List<LayoutOverride> items)
        {
            var b = new StringBuilder();
            b.Append("{\n  \"version\": 1,\n  \"items\": [");
            for (int i = 0; i < items.Count; i++)
            {
                var o = items[i];
                b.Append(i == 0 ? "\n" : ",\n");
                b.Append("    {\"id\": \"").Append(Esc(o.id)).Append("\", \"deleted\": ").Append(o.deleted ? "true" : "false");
                if (!o.deleted)
                {
                    b.Append(", \"position\": [").Append(F(o.position.x)).Append(", ").Append(F(o.position.y)).Append(", ").Append(F(o.position.z)).Append(']');
                    b.Append(", \"euler\": [").Append(F(o.euler.x)).Append(", ").Append(F(o.euler.y)).Append(", ").Append(F(o.euler.z)).Append(']');
                    b.Append(", \"scale\": [").Append(F(o.scale.x)).Append(", ").Append(F(o.scale.y)).Append(", ").Append(F(o.scale.z)).Append(']');
                }
                b.Append('}');
            }
            b.Append("\n  ]\n}\n");
            return b.ToString();
        }

        /// <summary>ToJson の形を読む。形が違えば例外（呼び出し側で日本語のエラーにする）。</summary>
        public static List<LayoutOverride> FromJson(string json)
        {
            var r = new Reader(json);
            object root = r.Value();
            var obj = root as Dictionary<string, object>;
            if (obj == null) throw new System.FormatException("最上位がオブジェクトではありません");
            var list = new List<LayoutOverride>();
            object itemsObj;
            if (!obj.TryGetValue("items", out itemsObj)) return list;
            var items = itemsObj as List<object>;
            if (items == null) throw new System.FormatException("items が配列ではありません");
            foreach (object it in items)
            {
                var d = it as Dictionary<string, object>;
                if (d == null) throw new System.FormatException("items の要素がオブジェクトではありません");
                var o = new LayoutOverride();
                object v;
                if (d.TryGetValue("id", out v)) o.id = v as string;
                if (d.TryGetValue("deleted", out v) && v is bool) o.deleted = (bool)v;
                if (d.TryGetValue("position", out v)) o.position = Vec(v);
                if (d.TryGetValue("euler", out v)) o.euler = Vec(v);
                if (d.TryGetValue("scale", out v)) o.scale = Vec(v);
                if (o.id == null) throw new System.FormatException("id の無い項目があります");
                list.Add(o);
            }
            return list;
        }

        static Vector3 Vec(object v)
        {
            var a = v as List<object>;
            if (a == null || a.Count != 3) throw new System.FormatException("3 要素の配列ではありません");
            return new Vector3((float)(double)a[0], (float)(double)a[1], (float)(double)a[2]);
        }

        sealed class Reader
        {
            readonly string s;
            int i;
            public Reader(string s) { this.s = s ?? ""; }

            void Ws() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

            public object Value()
            {
                Ws();
                if (i >= s.Length) throw new System.FormatException("JSON が途中で終わっています");
                char c = s[i];
                if (c == '{') return Obj();
                if (c == '[') return Arr();
                if (c == '"') return Str();
                if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
                if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
                if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
                return Num();
            }

            Dictionary<string, object> Obj()
            {
                var d = new Dictionary<string, object>();
                i++;
                Ws();
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws();
                    string k = Str();
                    Ws();
                    if (i >= s.Length || s[i] != ':') throw new System.FormatException(": がありません");
                    i++;
                    d[k] = Value();
                    Ws();
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return d; }
                    throw new System.FormatException("} がありません");
                }
            }

            List<object> Arr()
            {
                var a = new List<object>();
                i++;
                Ws();
                if (i < s.Length && s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value());
                    Ws();
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return a; }
                    throw new System.FormatException("] がありません");
                }
            }

            string Str()
            {
                if (i >= s.Length || s[i] != '"') throw new System.FormatException("文字列がありません");
                i++;
                var b = new StringBuilder();
                while (i < s.Length && s[i] != '"')
                {
                    char c = s[i++];
                    if (c == '\\' && i < s.Length)
                    {
                        char e = s[i++];
                        if (e == 'n') b.Append('\n');
                        else if (e == 'u' && i + 4 <= s.Length) { b.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber)); i += 4; }
                        else b.Append(e);
                    }
                    else b.Append(c);
                }
                if (i >= s.Length) throw new System.FormatException("文字列が閉じていません");
                i++;
                return b.ToString();
            }

            double Num()
            {
                int st = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                if (st == i) throw new System.FormatException("数値がありません（位置 " + st + "）");
                return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
            }
        }
    }
}
