using System.Collections.Generic;
using UnityEngine;

namespace OndaVerde
{
    public enum SignalState { Red, Yellow, Green }
    public enum SimMode { Fixed, Responsive }

    public enum Escenario { SinCoord, OndaVerde, Adaptativo }

    public class Polyline
    {
        public Vector3[] pts;
        public float[] cum;
        public float length;

        public static Polyline FromFlat(float[] flat, int n, Vector3 origin, Quaternion rot)
        {
            var p = new Polyline();
            p.pts = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float lx = flat[i * 3], ly = flat[i * 3 + 1], lz = flat[i * 3 + 2];
                p.pts[i] = origin + rot * new Vector3(lx, lz, ly);
            }
            p.Bake();
            return p;
        }

        public void Bake()
        {
            cum = new float[pts.Length];
            cum[0] = 0f;
            float L = 0f;
            for (int i = 1; i < pts.Length; i++) { L += Vector3.Distance(pts[i - 1], pts[i]); cum[i] = L; }
            length = L;
        }

        public Vector3 At(float s)
        {
            s = Mathf.Clamp(s, 0f, length);
            for (int i = 1; i < cum.Length; i++)
            {
                if (s <= cum[i])
                {
                    float seg = Mathf.Max(1e-4f, cum[i] - cum[i - 1]);
                    return Vector3.Lerp(pts[i - 1], pts[i], (s - cum[i - 1]) / seg);
                }
            }
            return pts[pts.Length - 1];
        }

        public Vector3 Tangent(float s)
        {
            float a = Mathf.Clamp(s - 0.7f, 0f, length), b = Mathf.Clamp(s + 0.7f, 0f, length);
            Vector3 d = At(b) - At(a);
            if (d.sqrMagnitude < 1e-6f) d = pts[pts.Length - 1] - pts[0];
            return d.normalized;
        }
    }

    public class PhaseGroup
    {

        public const float CLOCK0 = 24f;

        public string id;
        public float greenStart, greenDur, yellow, allred, cycle, offset;

        float Mod(float t) { t %= cycle; if (t < 0) t += cycle; return t; }

        public SignalState State(float time)
        {
            float t = Mod(time - offset + CLOCK0);
            float g0 = greenStart;
            if (t >= g0 && t < g0 + greenDur) return SignalState.Green;
            if (t >= g0 + greenDur && t < g0 + greenDur + yellow) return SignalState.Yellow;
            return SignalState.Red;
        }

        public float GreenRemaining(float time)
        {
            float t = Mod(time - offset + CLOCK0);
            if (t < greenStart || t >= greenStart + greenDur) return 0f;
            return greenStart + greenDur - t;
        }
    }

    public class SimController
    {
        public string id;
        public float cycle, offset, sM;
        public Dictionary<string, PhaseGroup> groups = new Dictionary<string, PhaseGroup>();
        public int qMain, qCross;
        public float baseMainDur, baseCrossDur;
        public PhaseGroup Group(string g) { return (g != null && groups.TryGetValue(g, out var p)) ? p : null; }

        public void SetOffset(float o)
        {
            offset = o;
            foreach (var kv in groups) kv.Value.offset = o;
        }

        public void ResetVerde(float verdeGM)
        {
            var gm = Group("GM_through"); var cr = Group("CROSS");
            if (gm == null || cr == null) return;
            float sum = baseMainDur + baseCrossDur;
            gm.greenDur = verdeGM;
            cr.greenDur = Mathf.Max(6f, sum - verdeGM);
            cr.greenStart = gm.greenStart + gm.greenDur + gm.yellow + gm.allred;
        }
    }

    public class LinkStop { public float s; public PhaseGroup phase; }

    public class SimLink
    {
        public string id, kind, dir;
        public int laneIndex;
        public Polyline geom;
        public float speed;
        public List<LinkStop> stops = new List<LinkStop>();
        public List<SimConnector> downstream = new List<SimConnector>();
        public List<SimVehicle> vehicles = new List<SimVehicle>();
        public bool isCorridor, isSpawn, isSink;
        public float corridorBase;
        public float corridorSign = 1f;
        public float spawnRate, spawnAccum;
        public List<CrossGuard> crossGuards = new List<CrossGuard>();

        public float CorridorS(float s) { return corridorBase + corridorSign * s; }
    }

    public class ConflictPt { public SimLink lk; public float sL; public float sC; }

    public class CrossGuard { public SimConnector cn; public float sL, sC; }

    public class SimConnector
    {
        public string id, movement, control;
        public int dbgChosen, dbgBlockFull, dbgBlockExit, dbgBlockMouth, dbgBlockClear, dbgBlockGreen;
        public Polyline geom;
        public PhaseGroup phase;
        public SimLink from, to;
        public float branchS, mergeS, width;
        public List<SimVehicle> vehicles = new List<SimVehicle>();
        public List<SimConnector> conflicts = new List<SimConnector>();
        public List<ConflictPt> crossPts = new List<ConflictPt>();
    }

    public class SimVehicle
    {
        public int id, modelIndex;
        public float length, v, v0, spawnTime, dist, waited;
        public SimLink link;
        public SimConnector conn;
        public float s;
        public SimConnector chosen;
        public bool done;

        public const float A = 1.7f, B = 2.6f, T = 1.3f, S0 = 3.2f, DELTA = 4f;

        public float Accel(float gap, float leadV)
        {
            float free = A * (1f - Mathf.Pow(v / Mathf.Max(0.1f, v0), DELTA));
            if (gap >= 9000f) return free;
            gap = Mathf.Max(0.4f, gap);
            float sStar = S0 + Mathf.Max(0f, v * T + v * (v - leadV) / (2f * Mathf.Sqrt(A * B)));
            float r = sStar / gap;
            return Mathf.Min(free, A * (1f - r * r));
        }
    }

    public class SimWorld
    {
        public float time;
        public SimMode mode = SimMode.Fixed;
        public List<SimLink> links = new List<SimLink>();
        public List<SimConnector> connectors = new List<SimConnector>();
        public List<SimController> controllers = new List<SimController>();
        public System.Random rng;
        public int nextId = 1;
        public float V;
        public int spawnedTotal, finishedTotal;
        public float delaySumGM, delaySumCross;
        public int delayNGM, delayNCross;
        public int redViolations;
        float lastStamp = -1f;

        public Escenario escenario = Escenario.OndaVerde;
        public PlanControl plan;

        static readonly int[] FLEET = { 0, 1, 2, 5, 4, 2, 4, 5, 0, 6, 2, 4, 0, 7, 5, 6 };

        public SimWorld(SimMode m, int seed, float v) { mode = m; rng = new System.Random(seed); V = v; }

        public SimController Controller(string id) { return controllers.Find(c => c.id == id); }

        static float Mod(float t, float c) { t %= c; if (t < 0) t += c; return t; }

        public void AplicarEscenario(Escenario e)
        {
            escenario = e;
            string nom = e == Escenario.SinCoord ? "sin_coord"
                       : e == Escenario.OndaVerde ? "onda_verde" : "adaptativo";
            foreach (var c in controllers)
            {
                float off = (plan != null) ? plan.OffsetDe(nom, c.id) : (e == Escenario.SinCoord ? 0f : c.offset);
                c.SetOffset(off);
                float verde = (plan != null) ? plan.VerdeBase(nom) : c.baseMainDur;
                c.ResetVerde(verde);
            }
        }

        void AdaptiveUpdate()
        {
            if (plan == null) return;
            foreach (var c in controllers)
            {
                var gm = c.Group("GM_through");
                var cr = c.Group("CROSS");
                if (gm == null || cr == null) continue;
                int bc = plan.Bucket(c.qMain);
                int bt = plan.Bucket(c.qCross);
                float verde = plan.VerdeAdaptativo(c.id, bc, bt, c.baseMainDur);
                float sum = c.baseMainDur + c.baseCrossDur;
                gm.greenDur = Mathf.Clamp(verde, 8f, sum - 6f);
                cr.greenDur = sum - gm.greenDur;
                cr.greenStart = gm.greenStart + gm.greenDur + gm.yellow + gm.allred;
            }
        }

        public void Step(float dt)
        {
            time += dt;
            UpdateQueues();

            if (escenario == Escenario.Adaptativo)
            {
                float stamp = Mathf.Floor(time / 90f);
                if (stamp != lastStamp) { AdaptiveUpdate(); lastStamp = stamp; }
            }
            Spawn(dt);

            foreach (var lk in links) StepSeg(lk.vehicles, dt, lk, null);
            foreach (var cn in connectors) StepSeg(cn.vehicles, dt, null, cn);

            Transfers();
        }

        void UpdateQueues()
        {
            foreach (var c in controllers) { c.qMain = 0; c.qCross = 0; }
            foreach (var lk in links)
            {
                if (lk.stops.Count == 0) continue;
                var st = lk.stops[0];
                var ctrl = ControllerOfPhase(st.phase);
                if (ctrl == null) continue;
                foreach (var veh in lk.vehicles)
                {
                    if (veh.v > 1.5f) continue;
                    if (veh.s < st.s && veh.s > st.s - 40f)
                    {
                        if (st.phase.id == "CROSS") ctrl.qCross++; else ctrl.qMain++;
                    }
                }
            }
        }

        SimController ControllerOfPhase(PhaseGroup ph)
        {
            foreach (var c in controllers)
                foreach (var kv in c.groups)
                    if (kv.Value == ph) return c;
            return null;
        }

        void StepSeg(List<SimVehicle> vs, float dt, SimLink lk, SimConnector cn)
        {
            vs.Sort((a, b) => a.s.CompareTo(b.s));
            float segLen = lk != null ? lk.geom.length : cn.geom.length;
            for (int i = 0; i < vs.Count; i++)
            {
                var veh = vs[i];
                float gap = 9999f, leadV = 0f;
                if (i + 1 < vs.Count) { var ld = vs[i + 1]; gap = ld.s - veh.s - ld.length; leadV = ld.v; }
                if (lk != null)
                {

                    foreach (var dn in lk.downstream)
                        foreach (var v2 in dn.vehicles)
                        {
                            if (v2.s > 14f) continue;
                            float g2 = dn.branchS + v2.s - veh.s - v2.length;
                            if (g2 > -2f && g2 < gap) { gap = g2; leadV = v2.v; }
                        }
                }
                float a = veh.Accel(gap, leadV);
                bool mustStop = false; float stopLineS = 0f;

                if (lk != null)
                {
                    float ctrlS; SignalState ss;
                    if (NextControl(veh, lk, out ctrlS, out ss))
                    {
                        if (ss != SignalState.Green)
                        {
                            float sgap = ctrlS - veh.s - SimVehicle.S0;

                            float brake = (veh.v * veh.v) / (2f * SimVehicle.B);
                            bool stop = (ss == SignalState.Red) || (brake <= sgap);
                            if (stop)
                            {
                                a = Mathf.Min(a, veh.Accel(Mathf.Max(0.4f, sgap), 0f));
                                mustStop = true; stopLineS = ctrlS;
                            }
                        }
                        else if (veh.s < ctrlS && leadV < 0.5f && gap < 9999f)
                        {

                            float stopAt = veh.s + gap;
                            if (stopAt > ctrlS - 2f && stopAt < ctrlS + 60f)
                                a = Mathf.Min(a, veh.Accel(Mathf.Max(0.4f, ctrlS - veh.s - SimVehicle.S0), 0f));
                        }
                        if (veh.s < ctrlS && veh.chosen != null && veh.chosen.branchS > ctrlS)
                        {

                            int inBox = 0;
                            foreach (var o in lk.vehicles)
                                if (o != veh && o.s > ctrlS && o.chosen == veh.chosen) inBox++;
                            int maxBox = Mathf.Max(1, (int)((veh.chosen.branchS - ctrlS - 18f) / 9.5f));
                            if (inBox >= maxBox)
                                a = Mathf.Min(a, veh.Accel(Mathf.Max(0.4f, ctrlS - veh.s - SimVehicle.S0), 0f));
                        }
                    }

                    if (veh.chosen != null && veh.chosen.control == "give_way" && veh.s < veh.chosen.branchS
                        && (veh.chosen.vehicles.Count >= ConnCap(veh.chosen) || !CrossingClear(veh.chosen, veh.waited)))
                    {
                        a = Mathf.Min(a, veh.Accel(Mathf.Max(0.4f, veh.chosen.branchS - veh.s - SimVehicle.S0), 0f));

                        if (veh.v < 0.5f && veh.chosen.branchS - veh.s < 12f) veh.waited += dt;
                    }

                    foreach (var g in lk.crossGuards)
                    {
                        if (g.sL < veh.s + 1f || g.sL > veh.s + 60f) continue;
                        foreach (var v2 in g.cn.vehicles)
                            if (Mathf.Abs(v2.s - g.sC) < 9f)
                            {
                                a = Mathf.Min(a, veh.Accel(Mathf.Max(0.4f, g.sL - veh.s - SimVehicle.S0 - 1.5f), 0f));
                                break;
                            }
                    }
                }
                a = Mathf.Clamp(a, -8f, SimVehicle.A);
                veh.v = Mathf.Max(0f, veh.v + a * dt);
                float adv = veh.v * dt;

                if (mustStop)
                {
                    float limite = stopLineS - 0.5f;
                    if (veh.s <= limite && veh.s + adv > limite)
                    {
                        adv = Mathf.Max(0f, limite - veh.s);
                        veh.v = 0f;
                    }
                }
                veh.s += adv;
                veh.dist += adv;
                if (mustStop && veh.s > stopLineS + 0.1f) redViolations++;
            }

            for (int i = vs.Count - 2; i >= 0; i--)
            {
                var ld = vs[i + 1]; var vb = vs[i];
                float cap = ld.s - ld.length - 0.4f;
                if (vb.s > cap) { vb.s = Mathf.Max(0f, cap); vb.v = Mathf.Min(vb.v, ld.v); }
            }
        }

        bool NextControl(SimVehicle veh, SimLink lk, out float ctrlS, out SignalState ss)
        {
            ctrlS = 0f; ss = SignalState.Green;
            float best = 9999f; PhaseGroup ph = null;
            foreach (var st in lk.stops)
            {
                if (st.s > veh.s && st.s < best)
                {
                    if (veh.chosen != null && veh.chosen.branchS < st.s - 0.5f) continue;
                    best = st.s;

                    ph = (veh.chosen != null && veh.chosen.phase != null) ? veh.chosen.phase : st.phase;
                }
            }
            if (veh.chosen != null && veh.chosen.phase != null && veh.chosen.branchS > veh.s && veh.chosen.branchS < best)
            {
                best = veh.chosen.branchS; ph = veh.chosen.phase;
            }
            if (ph == null) return false;
            ctrlS = best; ss = ph.State(time);
            return true;
        }

        void Transfers()
        {
            foreach (var lk in links)
            {
                for (int i = lk.vehicles.Count - 1; i >= 0; i--)
                {
                    var veh = lk.vehicles[i];
                    float exitS = (veh.chosen != null) ? veh.chosen.branchS : lk.geom.length;
                    if (veh.s < exitS) continue;
                    if (veh.chosen != null)
                    {
                        var cn = veh.chosen;

                        bool full = cn.vehicles.Count >= ConnCap(cn);
                        bool exitTaken = cn.to != null && Occupied(cn.to, cn.mergeS, cn);

                        bool mouthBusy = false;
                        foreach (var o in cn.vehicles)
                            if (o.s < o.length + 2.0f) { mouthBusy = true; break; }
                        if (full) cn.dbgBlockFull++;
                        else if (exitTaken) cn.dbgBlockExit++;
                        else if (mouthBusy) cn.dbgBlockMouth++;
                        if (full || exitTaken || mouthBusy || (cn.control == "give_way" && !CrossingClear(cn, veh.waited)))
                        {
                            veh.waited += 0.05f;

                            float wall = exitS - 0.05f;
                            for (int q = lk.vehicles.Count - 1; q >= 0; q--)
                            {
                                var o = lk.vehicles[q];
                                if (o == veh || o.s >= exitS) continue;
                                if (o.s > wall - o.length - 0.5f)
                                    wall = Mathf.Min(wall, o.s - o.length - 0.4f);
                            }
                            veh.s = Mathf.Min(veh.s, Mathf.Max(0f, wall));
                            veh.v = 0f;
                            continue;
                        }
                        lk.vehicles.RemoveAt(i);
                        veh.link = null; veh.conn = cn; veh.chosen = null;
                        veh.waited = 0f;
                        veh.s = Mathf.Max(0f, veh.s - exitS);
                        cn.vehicles.Add(veh);
                    }
                    else
                    {
                        lk.vehicles.RemoveAt(i);
                        Finish(veh, lk);
                    }
                }
            }
            foreach (var cn in connectors)
            {
                for (int i = cn.vehicles.Count - 1; i >= 0; i--)
                {
                    var veh = cn.vehicles[i];
                    if (veh.s < cn.geom.length) continue;
                    var nl = cn.to;
                    if (nl == null) { cn.vehicles.RemoveAt(i); veh.done = true; finishedTotal++; continue; }
                    if (cn.control != "through" && veh.waited < 60f && (Occupied(nl, cn.mergeS, cn) || LandingBlocked(nl, cn, veh)))
                    {

                        float aheadLen = 0f;
                        foreach (var o in cn.vehicles)
                            if (o != veh && (o.s > veh.s + 0.01f || (Mathf.Abs(o.s - veh.s) <= 0.01f && o.id < veh.id)))
                                aheadLen += o.length + 1.4f;
                        float holdS = cn.geom.length - 4.5f - aheadLen;
                        veh.s = Mathf.Max(0f, Mathf.Min(veh.s, holdS));
                        veh.v = 0f; veh.waited += 0.05f;
                        continue;
                    }
                    veh.waited = 0f;
                    cn.vehicles.RemoveAt(i);
                    veh.conn = null; veh.link = nl;
                    veh.s = cn.mergeS + Mathf.Max(0f, veh.s - cn.geom.length);

                    foreach (var o in nl.vehicles)
                        if (o.s >= veh.s - 0.5f && o.s - veh.s < o.length + 0.4f)
                            veh.s = Mathf.Max(0f, o.s - o.length - 0.4f);
                    ChooseExit(veh, nl);
                    nl.vehicles.Add(veh);
                }
            }
        }

        static int ConnCap(SimConnector cn)
        {

            return Mathf.Max(1, (int)(cn.geom.length / 12f));
        }

        static bool LandingBlocked(SimLink nl, SimConnector cn, SimVehicle veh)
        {

            float land = cn.mergeS + Mathf.Max(0f, veh.s - cn.geom.length);
            foreach (var o in nl.vehicles)
                if (o.s >= land - 0.5f && o.s - land < o.length + 0.4f)
                    land = Mathf.Max(0f, o.s - o.length - 0.4f);
            foreach (var o in nl.vehicles)
                if (o.s < land && land - o.s < veh.length + 0.4f)
                    return true;
            return false;
        }

        bool CrossingClear(SimConnector cn, float patience = 0f)
        {

            if (cn.phase != null && cn.phase.GreenRemaining(time) < 2.5f + Mathf.Min(cn.geom.length, 45f) / 6f)
            { cn.dbgBlockGreen++; return false; }

            float staticWin = cn.phase != null ? 2.8f : 5.0f;
            foreach (var cp in cn.crossPts)
            {

                float tNeed = 2.2f + Mathf.Min(cp.sC, 22f) / 6f;

                if (patience > 15f) tNeed *= 0.5f;
                foreach (var v in cp.lk.vehicles)
                {
                    float d = cp.sL - v.s;
                    if (d <= -4f || d >= Mathf.Max(staticWin, v.v * tNeed)) continue;

                    bool held = false;
                    foreach (var st in cp.lk.stops)
                        if (st.s > v.s - 0.5f && st.s < cp.sL && st.phase.State(time) != SignalState.Green)
                        { held = true; break; }
                    if (held) continue;
                    cn.dbgBlockClear++; return false;
                }
            }
            return true;
        }

        bool Occupied(SimLink lk, float s, SimConnector self = null)
        {
            foreach (var v in lk.vehicles)
                if (v.s - s > -5f && v.s - s < 7f) return true;

            foreach (var cn in connectors)
            {
                if (cn == self || cn.to != lk) continue;
                if (Mathf.Abs(cn.mergeS - s) >= 8f) continue;
                foreach (var v in cn.vehicles)
                    if (cn.geom.length - v.s < 6f) return true;
            }
            return false;
        }

        void Finish(SimVehicle veh, SimLink lk)
        {
            veh.done = true; finishedTotal++;
            float freeT = veh.dist / Mathf.Max(1f, veh.v0);
            float delay = Mathf.Max(0f, (time - veh.spawnTime) - freeT);
            if (lk.isCorridor) { delaySumGM += delay; delayNGM++; }
            else { delaySumCross += delay; delayNCross++; }
        }

        void ChooseExit(SimVehicle veh, SimLink lk)
        {

            veh.chosen = null;
            var opts = new List<SimConnector>();
            foreach (var c in lk.downstream)
                if (c.branchS > veh.s - 0.5f) opts.Add(c);
            if (opts.Count == 0) return;
            if (opts.Count == 1) { veh.chosen = opts[0]; opts[0].dbgChosen++; return; }
            float total = 0f;
            var weights = new List<float>();
            foreach (var c in opts)
            {
                float w = c.movement == "through" ? 6f : (c.movement == "uturn" ? 1.6f : (c.movement == "slip" ? 2f : (c.movement == "left" ? 1.0f : 1.5f)));
                if (c.movement == "slip" && lk.laneIndex != 0 && lk.kind == "lateral") w = 0f;
                weights.Add(w); total += w;
            }
            double r = rng.NextDouble() * total;
            for (int i = 0; i < opts.Count; i++)
            {
                r -= weights[i];
                if (r <= 0) { veh.chosen = opts[i]; opts[i].dbgChosen++; return; }
            }
            veh.chosen = opts[opts.Count - 1];
        }

        SimConnector PickIfBranch(SimVehicle veh, SimConnector c) { return c; }

        void Spawn(float dt)
        {
            foreach (var lk in links)
            {
                if (!lk.isSpawn) continue;
                lk.spawnAccum += lk.spawnRate * Demand(lk) * dt;
                while (lk.spawnAccum >= 1f)
                {
                    lk.spawnAccum -= 1f;

                    int jam = 0;
                    foreach (var v in lk.vehicles) if (v.s < 30f && v.v < 1f) jam++;
                    if (jam >= 3) { lk.spawnAccum = 0f; break; }
                    bool blocked = false;
                    foreach (var v in lk.vehicles) if (v.s < 8f + v.length) { blocked = true; break; }
                    if (blocked) continue;
                    SpawnOn(lk, 0f, lk.speed * (lk.kind == "cross" ? 0.6f : 0.85f));
                }
            }
        }

        float Demand(SimLink lk)
        {

            if (lk.kind == "cross" && lk.id.StartsWith("C3/") && time >= 140f && time <= 320f) return 1.8f;
            return 1f;
        }

        SimVehicle SpawnOn(SimLink lk, float s, float v)
        {
            var veh = new SimVehicle();
            veh.id = nextId++;
            veh.link = lk; veh.s = s; veh.v = v; veh.spawnTime = time;
            veh.modelIndex = FLEET[rng.Next(FLEET.Length)];
            veh.length = veh.modelIndex == 6 ? 10f : (veh.modelIndex == 7 ? 7.5f : 4.6f);
            veh.v0 = lk.speed * (0.9f + 0.18f * (float)rng.NextDouble());
            ChooseExit(veh, lk);
            lk.vehicles.Add(veh);
            spawnedTotal++;
            return veh;
        }

        public void WarmUp()
        {
            foreach (var lk in links)
            {
                if (lk.kind != "gm_through" && lk.kind != "lateral" && lk.kind != "cross") continue;
                float spacing = 48f;
                for (float s0 = 8f; s0 < lk.geom.length - 12f; s0 += spacing)
                {
                    float s = s0 + (float)(rng.NextDouble() * 14);
                    if (s > lk.geom.length - 12f) continue;
                    bool blocked = false;

                    foreach (var st in lk.stops) if (s > st.s - 2f && s < st.s + 30f) { blocked = true; break; }
                    if (!blocked)
                        foreach (var g in lk.crossGuards) if (Mathf.Abs(s - g.sL) < 14f) { blocked = true; break; }
                    if (!blocked)
                        foreach (var v in lk.vehicles) if (Mathf.Abs(v.s - s) < 8f) { blocked = true; break; }
                    if (blocked) continue;
                    SpawnOn(lk, s, lk.speed * 0.7f);
                }
            }
        }
    }
}
