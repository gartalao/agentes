using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace OndaVerde
{
    public class MetricsLogger
    {
        readonly StringBuilder traj = new StringBuilder();
        readonly StringBuilder sigs = new StringBuilder();
        readonly string tag;
        readonly string outDir;
        float nextLog = 0f;
        public float interval = 0.5f;

        public MetricsLogger(string modeTag)
        {
            tag = modeTag;
            outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "out"));
            Directory.CreateDirectory(outDir);
            traj.AppendLine("t,id,dir,s,v,state");
            sigs.AppendLine("t,id,s,main,cross,greenMain,greenCross,offset");
        }

        static int StateCode(SignalState s) { return s == SignalState.Green ? 2 : (s == SignalState.Yellow ? 1 : 0); }
        static string F(float x) { return x.ToString("0.###", CultureInfo.InvariantCulture); }

        public void Tick(SimWorld w)
        {
            if (w.time < nextLog) return;
            nextLog += interval;

            foreach (var lk in w.links)
            {
                if (!lk.isCorridor) continue;
                foreach (var veh in lk.vehicles)
                {
                    float sCorr = lk.CorridorS(veh.s);
                    int st = 2;
                    foreach (var stop in lk.stops)
                        if (stop.s > veh.s) { st = StateCode(stop.phase.State(w.time)); break; }
                    traj.Append(F(w.time)).Append(',').Append(veh.id).Append(',')
                        .Append(lk.dir).Append(',')
                        .Append(F(sCorr)).Append(',').Append(F(veh.v)).Append(',').Append(st).Append('\n');
                }
            }

            foreach (var c in w.controllers)
            {
                var gm = c.Group("GM_through");
                var cr = c.Group("CROSS");
                if (gm == null) continue;
                sigs.Append(F(w.time)).Append(',').Append(c.id).Append(',').Append(F(c.sM)).Append(',')
                    .Append(StateCode(gm.State(w.time))).Append(',')
                    .Append(cr != null ? StateCode(cr.State(w.time)) : 0).Append(',')
                    .Append(F(gm.greenDur)).Append(',').Append(F(cr != null ? cr.greenDur : 0f)).Append(',')
                    .Append(F(c.offset)).Append('\n');
            }
        }

        public void Flush()
        {
            File.WriteAllText(Path.Combine(outDir, $"trajectories_{tag}.csv"), traj.ToString());
            File.WriteAllText(Path.Combine(outDir, $"signals_{tag}.csv"), sigs.ToString());
            Debug.Log($"[MetricsLogger] escrito trajectories_{tag}.csv y signals_{tag}.csv en {outDir}");
        }
    }
}
