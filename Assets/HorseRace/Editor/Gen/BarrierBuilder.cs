using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// コースの見えない柵の実体化（どこに張るかは CourseBarrier が決める。純粋・単体テスト済み）。SetPieceBuilder の後に呼ぶ
    /// （溝・近道を掘った後の地形の高さを使う）。
    ///   当たり：1 区間 = BoxCollider 1 つ（厚さ 0.5m、両端の地面の高い方から 8m 上まで、低い方の 1m 下から）。Block レイヤー
    ///           （乗り物の obstacleMask・徒歩のプレイヤーを止める）。
    ///   見た目：CHUNK_M（200m）ごとに 1 つのメッシュの帯（u = 線に沿った m、v = 下端からの高さ m）。シェーダー
    ///           HorseRace/ProximityBarrier が「見ている人からの距離」で薄くするので、遠くからは見えず、近づくと砂金色の網目が浮かぶ。
    ///   手直し（LayoutOverrides）の対象ではない（id を付けない）。Build World のたびに計画から作り直す。
    /// </summary>
    public static class BarrierBuilder
    {
        public const string ROOT = "CourseBarrier";
        public const string SHADER = "HorseRace/ProximityBarrier";
        public const string MAT_PATH = GenUtil.GEN_MATERIALS + "/ProximityBarrier.mat";
        const float VIS_SINK = 0.5f;    // 見た目の帯は地面の 0.5m 下から（地面の凸凹で下に隙間が見えない）

        public static void Build(StageBuild sb)
        {
            CourseBarrier.Plan plan = CourseBarrier.For(sb.def, sb.courseLocal, sb.goalArc, sb.shortcut);
            var root = GenUtil.Child(sb.root, ROOT, Vector3.zero).transform;
            root.position = Vector3.zero;
            var walls = GenUtil.Child(root, "Walls", Vector3.zero).transform;
            walls.position = Vector3.zero;
            var visual = GenUtil.Child(root, "Visual", Vector3.zero).transform;
            visual.position = Vector3.zero;
            Material mat = Material();

            int seg = 0, chunk = 0;
            for (int r = 0; r < plan.runs.Count; r++)
            {
                Vector3[] p = plan.runs[r];
                bool closed = plan.closed[r];
                int n = closed ? p.Length : p.Length - 1;
                // 区間の両端の地面（ワールド）
                var g = new Vector3[p.Length];
                for (int i = 0; i < p.Length; i++) g[i] = sb.Ground(p[i].x, p[i].z);

                for (int k = 0; k < n; k++) Wall(walls, "Wall_" + (seg++), g[k], g[(k + 1) % p.Length]);

                if (mat == null) continue;
                // 見た目：CHUNK_M ごとに切る（隣のチャンクと端の点を共有して切れ目を出さない）
                var pts = new List<Vector3>();
                var us = new List<float>();
                float u = 0f;
                pts.Add(g[0]); us.Add(0f);
                for (int k = 0; k < n; k++)
                {
                    Vector3 b = g[(k + 1) % p.Length];
                    u += WorldLayout.DistXZ(pts[pts.Count - 1], b);
                    pts.Add(b); us.Add(u);
                    if (us[us.Count - 1] - us[0] >= CourseBarrier.CHUNK_M && k < n - 1)
                    {
                        Strip(visual, "Strip_" + (chunk++), pts, us, mat);
                        pts = new List<Vector3> { b };
                        us = new List<float> { u };
                    }
                }
                if (pts.Count >= 2) Strip(visual, "Strip_" + (chunk++), pts, us, mat);
            }
            DressingBuilder.SetStatic(root.gameObject);
            Debug.Log("[HorseRace] ステージ" + (sb.def.index + 1) + " の見えない柵: " + seg + " 区間（中心から " + CourseBarrier.OFFSET_M
                + "m、見た目のメッシュ " + chunk + " 個）");
        }

        /// <summary>a→b（ワールド、y = 地面）の壁 1 枚。</summary>
        static void Wall(Transform parent, string name, Vector3 a, Vector3 b)
        {
            Vector3 d = b - a; d.y = 0f;
            float len = d.magnitude;
            if (len < 0.05f) return;
            float bottom = Mathf.Min(a.y, b.y) - CourseBarrier.WALL_SINK_M;
            float top = Mathf.Max(a.y, b.y) + CourseBarrier.WALL_HEIGHT_M;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3((a.x + b.x) * 0.5f, (bottom + top) * 0.5f, (a.z + b.z) * 0.5f);
            go.transform.rotation = Quaternion.LookRotation(d / len, Vector3.up);
            var box = go.AddComponent<BoxCollider>();
            // ローカル Z = 柵の線の向き。継ぎ目に隙間ができないよう少し長く
            box.size = new Vector3(CourseBarrier.WALL_THICK_M, top - bottom, len + 0.3f);
            go.layer = WorldLayers.Block;
        }

        /// <summary>地面に沿った帯のメッシュ（点ごとに下と上の 2 頂点）。</summary>
        static void Strip(Transform parent, string name, List<Vector3> pts, List<float> us, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Vector3 o = pts[0];
            go.transform.position = o;
            int n = pts.Count;
            var v = new Vector3[n * 2];
            var nrm = new Vector3[n * 2];
            var uv = new Vector2[n * 2];
            float h = CourseBarrier.WALL_HEIGHT_M + VIS_SINK;
            for (int i = 0; i < n; i++)
            {
                Vector3 dir = pts[Mathf.Min(i + 1, n - 1)] - pts[Mathf.Max(i - 1, 0)];
                dir.y = 0f;
                Vector3 side = dir.sqrMagnitude > 1e-6f ? WorldLayout.RightOf(dir.normalized) : Vector3.right;
                Vector3 bottom = pts[i] - Vector3.up * VIS_SINK - o;
                v[i * 2] = bottom;
                v[i * 2 + 1] = bottom + Vector3.up * h;
                nrm[i * 2] = nrm[i * 2 + 1] = side;
                uv[i * 2] = new Vector2(us[i], 0f);
                uv[i * 2 + 1] = new Vector2(us[i], h);
            }
            var tri = new int[(n - 1) * 6];
            for (int i = 0; i < n - 1; i++)
            {
                int a = i * 2, t = i * 6;
                tri[t] = a; tri[t + 1] = a + 1; tri[t + 2] = a + 3;
                tri[t + 3] = a; tri[t + 4] = a + 3; tri[t + 5] = a + 2;
            }
            var mesh = new Mesh();
            mesh.name = "CourseBarrier_" + name;
            mesh.vertices = v;
            mesh.normals = nrm;
            mesh.uv = uv;
            mesh.triangles = tri;
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>柵のマテリアル（Generated/Materials/ProximityBarrier.mat）。シェーダーが無ければ null（当たりだけ作る）。</summary>
        public static Material Material()
        {
            var sh = Shader.Find(SHADER);
            if (sh == null)
            {
                GenUtil.Warn("シェーダー " + SHADER + " が見つかりません（コースの柵は当たりだけ作ります）");
                return null;
            }
            GenUtil.EnsureFolder(GenUtil.GEN_MATERIALS);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MAT_PATH);
            if (mat == null)
            {
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, MAT_PATH);
            }
            mat.shader = sh;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
