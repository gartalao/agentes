using System.Collections.Generic;
using UnityEngine;

namespace OndaVerde
{

    [System.Serializable] public class PC_Off { public string id; public float off; }
    [System.Serializable] public class PC_Esc { public string nombre; public string tipo; public PC_Off[] offsets; public float verde; }
    [System.Serializable] public class PC_Pol { public string cid; public int bc; public int bt; public float verde; }
    [System.Serializable] public class PC_Unity {
        public PC_Esc[] escenarios;
        public float[] acciones_verde_s;
        public int[] umbrales_cola;
        public float detector_m;
        public PC_Pol[] politica;
    }
    [System.Serializable] public class PC_Root { public PC_Unity unity; }

    public class PlanControl
    {
        public PC_Unity u;
        readonly Dictionary<string, float> polLookup = new Dictionary<string, float>();

        public static PlanControl Load()
        {
            var ta = Resources.Load<TextAsset>("plan_control");
            if (ta == null) { Debug.LogWarning("[PlanControl] falta Resources/plan_control.json"); return null; }
            var root = JsonUtility.FromJson<PC_Root>(ta.text);
            if (root == null || root.unity == null) { Debug.LogWarning("[PlanControl] JSON sin bloque unity"); return null; }
            var p = new PlanControl { u = root.unity };
            if (p.u.politica != null)
                foreach (var e in p.u.politica) p.polLookup[$"{e.cid}|{e.bc}|{e.bt}"] = e.verde;
            return p;
        }

        public PC_Esc Escenario(string nombre)
        {
            if (u?.escenarios == null) return null;
            foreach (var e in u.escenarios) if (e.nombre == nombre) return e;
            return null;
        }

        public float OffsetDe(string escenario, string cid)
        {
            var e = Escenario(escenario);
            if (e?.offsets == null) return 0f;
            foreach (var o in e.offsets) if (o.id == cid) return o.off;
            return 0f;
        }

        public float VerdeBase(string escenario)
        {
            var e = Escenario(escenario);
            return e != null ? e.verde : 46f;
        }

        public int Bucket(float cola)
        {
            var th = u.umbrales_cola;
            if (cola <= th[0]) return 0;
            if (cola <= th[1]) return 1;
            if (cola <= th[2]) return 2;
            return 3;
        }

        public float VerdeAdaptativo(string cid, int bcCorredor, int btTransversal, float fallback)
        {
            string k = $"{cid}|{bcCorredor}|{btTransversal}";
            return polLookup.TryGetValue(k, out var v) ? v : fallback;
        }
    }
}
