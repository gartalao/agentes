using UnityEngine;

namespace OndaVerde
{
    public static class RoadProfile
    {
        static Vector2[] cm;
        static float[] cs;
        static DipDoc[] dips;
        static float expressHalf = 15f;
        static float d0Off = 11f;
        static bool ready;

        public static void Init(NetDoc net)
        {
            ready = false;
            if (net == null || net.axisCm == null || net.axisCm.Length < 4) return;
            int n = net.axisCm.Length / 2;
            cm = new Vector2[n];
            cs = new float[n];
            for (int i = 0; i < n; i++)
            {
                cm[i] = new Vector2(net.axisCm[2 * i], net.axisCm[2 * i + 1]);
                cs[i] = i == 0 ? 0f : cs[i - 1] + Vector2.Distance(cm[i], cm[i - 1]);
            }
            dips = net.dips ?? new DipDoc[0];
            if (net.expressHalf > 1f) expressHalf = net.expressHalf;
            if (net.d0Off > 0.5f) d0Off = net.d0Off;
            ready = true;
        }

        static void Project(float x, float z, out float s, out float perp)
        {
            var p = new Vector2(x, z);
            float bestD2 = float.MaxValue; s = 0f; perp = 0f;
            for (int i = 0; i < cm.Length - 1; i++)
            {
                Vector2 a = cm[i], b = cm[i + 1];
                Vector2 d = b - a;
                float seg2 = Mathf.Max(d.sqrMagnitude, 1e-9f);
                float t = Mathf.Clamp01(Vector2.Dot(p - a, d) / seg2);
                Vector2 q = a + d * t;
                float d2 = (p - q).sqrMagnitude;
                if (d2 < bestD2)
                {
                    float seg = Mathf.Sqrt(seg2);
                    bestD2 = d2;
                    s = cs[i] + t * seg;
                    perp = ((p.x - q.x) * (-d.y) + (p.y - q.y) * d.x) / seg;
                }
            }
        }

        public static float RoadY(float x, float z)
        {
            if (!ready) return 0f;
            Project(x, z, out float bestS, out float bestPerp);
            if (Mathf.Abs(bestPerp) > expressHalf) return 0f;
            float y = 0f;
            foreach (var dp in dips)
            {
                float ds = Mathf.Abs(bestS - dp.sC);
                if (ds <= dp.halfDeck) y = Mathf.Min(y, -dp.depth);
                else if (ds < dp.halfDeck + dp.ramp) y = Mathf.Min(y, -dp.depth * (1f - (ds - dp.halfDeck) / dp.ramp));
            }
            return y;
        }

        public static float LidY(float x, float z)
        {
            if (!ready) return 0f;
            Project(x, z, out float s, out float off);
            float ao = Mathf.Abs(off);
            float f = 0f;
            foreach (var dp in dips)
            {
                float d0 = dp.halfDeck + d0Off;
                float ds = Mathf.Abs(s - dp.sC);

                float r = expressHalf + 0.3f;
                if (ds > d0 && ds < dp.halfDeck + dp.ramp && ao < r) return 0f;
                float dN = Mathf.Sqrt((s - dp.sC - d0) * (s - dp.sC - d0) + off * off);
                float dS = Mathf.Sqrt((s - dp.sC + d0) * (s - dp.sC + d0) + off * off);
                if (dN < r || dS < r) return 0f;
                if (ds > d0 + 7f) continue;
                float fo = ao <= expressHalf - 1.3f ? 1f : Mathf.Max(0f, 1f - (ao - (expressHalf - 1.3f)) / 4f);
                float fs = ds <= d0 + 1f ? 1f : Mathf.Max(0f, 1f - (ds - d0 - 1f) / 6f);
                f = Mathf.Max(f, fo * fs);
            }
            return 0.12f * f;
        }
    }
}
