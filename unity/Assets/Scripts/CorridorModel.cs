using System.Collections.Generic;
using UnityEngine;

namespace OndaVerde
{
    public static class NetworkModel
    {
        public static NetDoc LoadNet()
        {
            var ta = Resources.Load<TextAsset>("network");
            if (ta == null) { Debug.LogError("No se encontro Resources/network.json"); return null; }
            return JsonUtility.FromJson<NetDoc>(ta.text);
        }

        static float Mod(float t, float c) { t %= c; if (t < 0) t += c; return t; }

        static float NearestS(Polyline g, Vector3 p)
        {
            float best = 1e9f; int bi = 0;
            for (int i = 0; i < g.pts.Length; i++)
            {
                float d = (g.pts[i] - p).sqrMagnitude;
                if (d < best) { best = d; bi = i; }
            }
            return g.cum[bi];
        }

        public static SimWorld Build(NetDoc net, SimMode mode, int seed)
        {
            RoadProfile.Init(net);
            float V = net.designSpeedKmh / 3.6f;
            var w = new SimWorld(mode, seed, V);
            var zoneLinks = new Dictionary<string, Dictionary<string, SimLink>>();
            var zoneById = new Dictionary<string, ZoneDoc>();

            foreach (var z in net.zones)
            {
                zoneById[z.id] = z;
                Vector3 origin = new Vector3(z.enuX, 0f, z.enuZ);
                Quaternion rot = Quaternion.Euler(0f, z.bearingDeg, 0f);
                var ctrl = new SimController { id = z.id, cycle = z.cycle, offset = Mod(z.sM / V, z.cycle), sM = z.sM };
                if (z.phaseGroups != null)
                    foreach (var pg in z.phaseGroups)
                    {
                        var g = new PhaseGroup { id = pg.id, greenStart = pg.greenStart, greenDur = pg.greenDur,
                            yellow = pg.yellow, allred = pg.allred, cycle = z.cycle, offset = ctrl.offset };
                        ctrl.groups[pg.id] = g;
                        if (pg.id == "GM_through") ctrl.baseMainDur = pg.greenDur;
                        if (pg.id == "CROSS") ctrl.baseCrossDur = pg.greenDur;
                    }
                w.controllers.Add(ctrl);

                var lmap = new Dictionary<string, SimLink>();
                foreach (var l in z.links)
                {
                    var sl = new SimLink
                    {
                        id = z.id + "/" + l.id, kind = l.kind, dir = l.dir, laneIndex = l.laneIndex,
                        speed = (l.speed > 0.5f ? l.speed : V), geom = Polyline.FromFlat(l.pts, l.n, origin, rot)
                    };
                    if (l.kind == "gm_through")
                    {
                        sl.isCorridor = true;
                        sl.corridorSign = (l.dir == "NB") ? 1f : -1f;
                        sl.corridorBase = z.sM + ((l.dir == "NB") ? -z.H : z.H);
                    }
                    if (l.stops != null)
                        foreach (var st in l.stops)
                        {
                            var ph = ctrl.Group(st.phaseGroup);
                            if (ph != null) sl.stops.Add(new LinkStop { s = st.sAlong, phase = ph });
                        }
                    sl.stops.Sort((a, b) => a.s.CompareTo(b.s));
                    lmap[l.id] = sl;
                    w.links.Add(sl);
                }
                zoneLinks[z.id] = lmap;
            }

            foreach (var z in net.zones)
            {
                var lmap = zoneLinks[z.id];
                var ctrl = w.Controller(z.id);
                if (z.connectors == null) continue;
                Vector3 origin = new Vector3(z.enuX, 0f, z.enuZ);
                Quaternion rot = Quaternion.Euler(0f, z.bearingDeg, 0f);
                foreach (var c in z.connectors)
                {
                    SimLink from = lmap.ContainsKey(c.fromLink) ? lmap[c.fromLink] : null;
                    SimLink to = lmap.ContainsKey(c.toLink) ? lmap[c.toLink] : null;
                    if (from == null) continue;
                    var sc = new SimConnector
                    {
                        id = z.id + "/" + c.id, movement = c.movement, control = c.control,
                        geom = Polyline.FromFlat(c.pts, c.n, origin, rot), from = from, to = to,
                        phase = (!string.IsNullOrEmpty(c.phaseGroup)) ? ctrl.Group(c.phaseGroup) : null
                    };
                    sc.branchS = NearestS(from.geom, sc.geom.pts[0]);
                    sc.mergeS = (to != null) ? NearestS(to.geom, sc.geom.pts[sc.geom.pts.Length - 1]) : 0f;
                    from.downstream.Add(sc);
                    w.connectors.Add(sc);
                }
            }

            for (int i = 0; i < net.order.Length - 1; i++)
            {
                var south = zoneLinks[net.order[i]];
                var north = zoneLinks[net.order[i + 1]];
                LinkGap(w, south, north, "gm_nb_");
                LinkGap(w, north, south, "gm_sb_");
            }

            foreach (var lk in w.links) lk.isSink = (lk.downstream.Count == 0);

            foreach (var cn in w.connectors)
            {
                if (cn.control != "give_way") continue;
                int slash = cn.id.IndexOf('/');
                string zp = slash >= 0 ? cn.id.Substring(0, slash + 1) : "";
                foreach (var lk in w.links)
                {
                    if (lk == cn.from || lk == cn.to) continue;
                    if (zp.Length > 0 && !lk.id.StartsWith(zp)) continue;
                    float bestD = 1e9f, bestSl = 0f, bestSc = 0f;
                    for (float s = 1.5f; s < cn.geom.length - 1.5f; s += 2.5f)
                    {
                        var p = cn.geom.At(s);
                        float sl = NearestS(lk.geom, p);
                        var q = lk.geom.At(sl);
                        float dx = q.x - p.x, dz = q.z - p.z;
                        float d = Mathf.Sqrt(dx * dx + dz * dz);
                        if (d < bestD) { bestD = d; bestSl = sl; bestSc = s; }
                    }
                    if (bestD < 2.4f)
                    {
                        cn.crossPts.Add(new ConflictPt { lk = lk, sL = bestSl, sC = bestSc });
                        lk.crossGuards.Add(new CrossGuard { cn = cn, sL = bestSl, sC = bestSc });
                    }
                }
            }

            var southZone = zoneLinks[net.order[0]];
            var northZone = zoneLinks[net.order[net.order.Length - 1]];
            foreach (var kv in southZone) if (kv.Value.kind == "gm_through" && kv.Value.dir == "NB") { kv.Value.isSpawn = true; kv.Value.spawnRate = 0.14f; }
            foreach (var kv in northZone) if (kv.Value.kind == "gm_through" && kv.Value.dir == "SB") { kv.Value.isSpawn = true; kv.Value.spawnRate = 0.13f; }

            var hasUpstream = new HashSet<SimLink>();
            foreach (var cn in w.connectors) if (cn.to != null) hasUpstream.Add(cn.to);
            foreach (var lk in w.links)
                if (lk.kind == "gm_through" && !lk.isSpawn && !hasUpstream.Contains(lk))
                { lk.isSpawn = true; lk.spawnRate = 0.10f; }
            foreach (var lk in w.links)
            {
                if (lk.kind == "cross" && (lk.id.Contains("cr_w_eb") || lk.id.Contains("cr_e_wb")))
                { lk.isSpawn = true; lk.spawnRate = 0.11f; }
                if (lk.kind == "lateral")
                { lk.isSpawn = true; lk.spawnRate = 0.11f; }
            }
            return w;
        }

        static void LinkGap(SimWorld w, Dictionary<string, SimLink> a, Dictionary<string, SimLink> b, string prefix)
        {
            for (int k = 0; k < 6; k++)
            {
                string key = prefix + k;
                if (!a.ContainsKey(key) || !b.ContainsKey(key)) continue;
                var la = a[key]; var lb = b[key];
                Vector3 p0 = la.geom.pts[la.geom.pts.Length - 1];
                Vector3 p1 = lb.geom.pts[0];
                int n = Mathf.Max(2, Mathf.RoundToInt(Vector3.Distance(p0, p1) / 4f));
                var pts = new Vector3[n + 1];
                for (int i = 0; i <= n; i++) pts[i] = Vector3.Lerp(p0, p1, (float)i / n);
                var g = new Polyline { pts = pts }; g.Bake();
                var sc = new SimConnector { id = la.id + "->" + lb.id, movement = "through", control = "free", geom = g, from = la, to = lb };
                sc.branchS = la.geom.length;
                sc.mergeS = 0f;
                la.downstream.Add(sc);
                w.connectors.Add(sc);
            }
        }
    }
}
