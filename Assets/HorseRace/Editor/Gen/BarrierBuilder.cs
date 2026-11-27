using System.Collections.Generic;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// コースの見えない柵の実体化（どこに張るかは CourseBarrier が決める。純粋・単体テスト済み）。SetPieceBuilder の後に呼ぶ
    /// （溝・近道を掘った後の地形の高さを使う）。
    ///   当たり：1 区間 = BoxCollider 1 つ（厚さ 0.5m、両端の地面の高い方から 8m 上まで、低い方の 1m 下から）。Block レイヤー
    ///           （乗り物の obstacleMask・徒歩のプレイヤーを止める）。
    ///   見た目（course-r2-06）：作ったメッシュの帯（以前の ProximityBarrier の帯）は使わない（モデルを作らない決まり）。
    ///           代わりに柵の線に沿って地形の土手（HeightmapGen.RaiseBerm、1.2m）を盛り、既存の柵・杭・岩を並べる（DressingPlan.BarrierLine）。
    ///           ぶつかると止まる物は、どのステージでも同じ形と色（木の柵と杭、急カーブの外は岩）。
    ///   手直し（LayoutOverrides）の対象ではない（id を付けない）。Build World のたびに計画から作り直す。
    /// </summary>
    public static class BarrierBuilder
    {
        public const string ROOT = "CourseBarrier";

        public static void Build(StageBuild sb)
        {
            CourseBarrier.Plan plan = CourseBarrier.For(sb.def, sb.courseLocal, sb.goalArc, sb.shortcut);
            // 柵の足元の土手（地形だけ）→ Terrain に書き戻す
            HeightmapGen.RaiseBerm(sb.heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, plan.Segments());
            sb.terrain.terrainData.SetHeights(0, 0, sb.heights);

            var root = GenUtil.Child(sb.root, ROOT, Vector3.zero).transform;
            root.position = Vector3.zero;
            var walls = GenUtil.Child(root, "Walls", Vector3.zero).transform;
            walls.position = Vector3.zero;

            int seg = 0;
            for (int r = 0; r < plan.runs.Count; r++)
            {
                Vector3[] p = plan.runs[r];
                bool closed = plan.closed[r];
                int n = closed ? p.Length : p.Length - 1;
                var g = new Vector3[p.Length];
                for (int i = 0; i < p.Length; i++) g[i] = sb.Ground(p[i].x, p[i].z);
                for (int k = 0; k < n; k++) Wall(walls, "Wall_" + (seg++), g[k], g[(k + 1) % p.Length]);
            }
            DressingBuilder.SetStatic(root.gameObject);

            // 柵の線の目印（既存の柵・杭・岩）
            List<Placement> marks = DressingPlan.BarrierLine(sb.def, sb.courseLocal, sb.heights, sb.goalArc, plan);
            var markRoot = GenUtil.Child(root, "Marks", Vector3.zero).transform;
            markRoot.position = Vector3.zero;
            GenUtil.StageVariant = sb.def.index;
            DressingBuilder.Instantiate(marks, markRoot, sb.def.Origin, "S" + (sb.def.index + 1) + "B");
            GenUtil.StageVariant = -1;
            Debug.Log("[HorseRace] ステージ" + (sb.def.index + 1) + " の見えない柵: " + seg + " 区間（土手 " + HeightmapGen.BERM_M + "m、目印 " + marks.Count + " 個）");
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
    }
}
