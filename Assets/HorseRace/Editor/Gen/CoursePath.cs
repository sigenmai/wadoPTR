using System.Collections.Generic;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>Editor-only course polyline utilities (centripetal Catmull-Rom).</summary>
    public static class CoursePath
    {
        const int SUBDIV = 64;

        /// <summary>Equal-arc-length samples through the control points, including both endpoints.</summary>
        public static Vector3[] Sample(Vector3[] controlPoints, float spacing)
        {
            if (controlPoints == null || controlPoints.Length == 0) return new Vector3[0];
            if (controlPoints.Length == 1 || spacing <= 0f) return new[] { controlPoints[0] };

            var dense = new List<Vector3>();
            int n = controlPoints.Length;
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 p1 = controlPoints[i], p2 = controlPoints[i + 1];
                Vector3 p0 = i > 0 ? controlPoints[i - 1] : p1 * 2f - p2;
                Vector3 p3 = i + 2 < n ? controlPoints[i + 2] : p2 * 2f - p1;
                for (int s = 0; s < SUBDIV; s++) dense.Add(CatmullRom(p0, p1, p2, p3, s / (float)SUBDIV));
            }
            dense.Add(controlPoints[n - 1]);

            var result = new List<Vector3> { dense[0] };
            float next = spacing, acc = 0f;
            for (int i = 1; i < dense.Count; i++)
            {
                Vector3 a = dense[i - 1], b = dense[i];
                float seg = Vector3.Distance(a, b);
                while (seg > 0f && acc + seg >= next)
                {
                    float t = (next - acc) / seg;
                    result.Add(Vector3.Lerp(a, b, t));
                    next += spacing;
                }
                acc += seg;
            }
            Vector3 last = controlPoints[n - 1];
            if (Vector3.Distance(result[result.Count - 1], last) > 1e-4f) result.Add(last);
            else result[result.Count - 1] = last;
            return result.ToArray();
        }

        public static float Length(Vector3[] points)
        {
            float l = 0f;
            if (points == null) return l;
            for (int i = 1; i < points.Length; i++) l += Vector3.Distance(points[i - 1], points[i]);
            return l;
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            const float eps = 1e-4f;
            float t0 = 0f;
            float t1 = t0 + Mathf.Max(Mathf.Sqrt(Vector3.Distance(p0, p1)), eps);
            float t2 = t1 + Mathf.Max(Mathf.Sqrt(Vector3.Distance(p1, p2)), eps);
            float t3 = t2 + Mathf.Max(Mathf.Sqrt(Vector3.Distance(p2, p3)), eps);
            float t = Mathf.Lerp(t1, t2, u);
            Vector3 a1 = p0 * ((t1 - t) / (t1 - t0)) + p1 * ((t - t0) / (t1 - t0));
            Vector3 a2 = p1 * ((t2 - t) / (t2 - t1)) + p2 * ((t - t1) / (t2 - t1));
            Vector3 a3 = p2 * ((t3 - t) / (t3 - t2)) + p3 * ((t - t2) / (t3 - t2));
            Vector3 b1 = a1 * ((t2 - t) / (t2 - t0)) + a2 * ((t - t0) / (t2 - t0));
            Vector3 b2 = a2 * ((t3 - t) / (t3 - t1)) + a3 * ((t - t1) / (t3 - t1));
            return b1 * ((t2 - t) / (t2 - t1)) + b2 * ((t - t1) / (t2 - t1));
        }
    }
}
