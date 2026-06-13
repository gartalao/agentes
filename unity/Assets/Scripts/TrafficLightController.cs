using UnityEngine;

namespace OndaVerde
{
    public class TrafficLightController : MonoBehaviour
    {
        public string zoneId;
        public string phaseGroup = "GM_through";

        SimRunner runner;
        PhaseGroup grp;
        Renderer[] rends;
        int last = -2;

        void Start()
        {

            rends = GetComponentsInChildren<Renderer>(true);
        }

        void Update()
        {
            if (runner == null) runner = Object.FindFirstObjectByType<SimRunner>();
            if (runner == null || runner.World == null) return;
            if (grp == null)
            {
                var c = runner.World.Controller(zoneId);
                if (c == null) return;
                grp = c.Group(phaseGroup);
                if (grp == null) return;
            }
            var st = grp.State(runner.World.time);
            if ((int)st == last) return;
            last = (int)st;
            foreach (var r in rends) PaintLamp(r, st);
        }

        public static void PaintLamp(Renderer r, SignalState active)
        {
            var mats = r.materials;
            bool touched = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                string nm = m.name.ToLower();
                int kind = nm.Contains("lamp_red") ? 0 : nm.Contains("lamp_yellow") ? 1 : nm.Contains("lamp_green") ? 2 : -1;
                if (kind < 0) continue;
                bool on = (kind == 0 && active == SignalState.Red)
                       || (kind == 1 && active == SignalState.Yellow)
                       || (kind == 2 && active == SignalState.Green);
                Color basec = kind == 0 ? new Color(0.92f, 0.06f, 0.06f)
                            : kind == 1 ? new Color(0.95f, 0.72f, 0.04f)
                            : new Color(0.06f, 0.85f, 0.12f);
                m.EnableKeyword("_EMISSION");
                m.color = on ? basec : basec * 0.10f;
                m.SetColor("_EmissionColor", on ? basec * 4.5f : Color.black);
                touched = true;
            }
            if (touched) r.materials = mats;
        }
    }
}
