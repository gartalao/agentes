using System.Collections.Generic;
using UnityEngine;

namespace OndaVerde
{
    public class SimRunner : MonoBehaviour
    {
        [Header("Modo de coordinacion")]
        public SimMode mode = SimMode.Fixed;
        public Escenario escenario = Escenario.Adaptativo;
        public int seed = 7;

        [Header("Carros (Car, car2, coupe, patricio, daniel, bus, truck)")]
        public GameObject[] carPrefabs;
        public float[] carYaws;
        public float[] carScales;
        public float[] carYOffsets;

        [Header("Simulacion / metricas")]
        public bool logging = true;
        public float runDuration = 300f;
        public float timeScale = 1f;

        [Header("Cesium drape")]
        public Transform georef;
        public bool drape = false;

        SimWorld world;
        public SimWorld World => world;
        MetricsLogger metrics;
        readonly Dictionary<int, GameObject> cars = new Dictionary<int, GameObject>();
        readonly Dictionary<int, float> carLift = new Dictionary<int, float>();
        readonly Dictionary<int, float> carYaw = new Dictionary<int, float>();
        readonly HashSet<int> alive = new HashSet<int>();
        bool flushed;

        void Start()
        {
            var net = NetworkModel.LoadNet();
            if (net == null) { enabled = false; return; }
            world = NetworkModel.Build(net, mode, seed);
            world.plan = PlanControl.Load();
            world.AplicarEscenario(escenario);
            world.WarmUp();
            if (logging) metrics = new MetricsLogger(escenario.ToString());
            Debug.Log($"[SimRunner] red lista: {world.links.Count} links, {world.connectors.Count} conectores, {world.controllers.Count} controladores, escenario {escenario}");
        }

        void OnGUI()
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            st.normal.textColor = Color.white;
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(14, 70, 360, 34), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(24, 76, 350, 22), "Control adaptativo (Q-learning)", st);
        }

        public static void StripFx(GameObject go)
        {
            foreach (var l in go.GetComponentsInChildren<Light>(true)) Destroy(l);
            foreach (var p in go.GetComponentsInChildren<Projector>(true)) Destroy(p);
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) Destroy(ps);
            foreach (var c in go.GetComponentsInChildren<Camera>(true)) if (c.gameObject != go) Destroy(c.gameObject); else Destroy(c);
            foreach (var b in go.GetComponentsInChildren<Behaviour>(true))
            {
                if (b == null) continue;
                string tn = b.GetType().Name;
                if (tn == "Halo" || tn == "LensFlare" || tn == "FlareLayer") Destroy(b);
            }
        }

        static Bounds BodyBounds(GameObject go)
        {

            var rends = go.GetComponentsInChildren<Renderer>(true);
            Bounds b = default; bool first = true;
            foreach (var r in rends)
            {
                var rb = r.bounds;
                bool flat = rb.size.y < 0.06f * Mathf.Max(rb.size.x, rb.size.z);
                bool planeName = r.gameObject.name.StartsWith("Plane");
                if (flat || planeName) continue;
                if (first) { b = rb; first = false; } else b.Encapsulate(rb);
            }
            if (first)
            {
                foreach (var r in rends)
                { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            }
            return b;
        }

        public static float NormalizeAndLift(GameObject go, float targetLen, int idx, out float autoYaw)
        {
            autoYaw = 0f;
            var rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) return 0f;
            var b = BodyBounds(go);
            autoYaw = (b.size.x > b.size.z ? 90f : 0f);
            float horiz = Mathf.Max(b.size.x, b.size.z);
            if (horiz > 0.05f) go.transform.localScale *= targetLen / horiz;
            var b2 = BodyBounds(go);
            return -b2.min.y;
        }

        void FixedUpdate()
        {
            if (world == null) return;
            float dt = Time.fixedDeltaTime * timeScale;
            world.Step(dt);
            SyncCars();
            if (metrics != null) metrics.Tick(world);
            if (logging && !flushed && world.time >= runDuration) { metrics.Flush(); flushed = true; Debug.Log("[SimRunner] metricas volcadas"); }
        }

        void Place(SimVehicle veh, Polyline g)
        {
            alive.Add(veh.id);
            int idx = Mathf.Clamp(veh.modelIndex, 0, (carPrefabs?.Length ?? 1) - 1);
            if (!cars.TryGetValue(veh.id, out var go))
            {
                var prefab = carPrefabs != null && carPrefabs.Length > 0 ? carPrefabs[idx] : null;
                go = prefab != null ? Instantiate(prefab) : GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Auto_" + veh.id;
                StripFx(go);
                float scaleK = (carScales != null && idx < carScales.Length && carScales[idx] > 0.05f) ? carScales[idx] : 1f;
                float lift0 = NormalizeAndLift(go, veh.length * scaleK, idx, out float ay);
                if (carYaws != null && idx < carYaws.Length && Mathf.Abs(carYaws[idx]) > 0.01f) ay = carYaws[idx];
                if (carYOffsets != null && idx < carYOffsets.Length) lift0 += carYOffsets[idx];
                carLift[veh.id] = lift0;
                carYaw[veh.id] = ay;
                go.transform.SetParent(transform, true);
                cars[veh.id] = go;
            }
            float yaw = carYaw.TryGetValue(veh.id, out var yv) ? yv : 0f;
            float lift = carLift.TryGetValue(veh.id, out var lv) ? lv : 0f;
            Vector3 p = g.At(veh.s);

            Vector3 tn0 = g.Tangent(veh.s);
            float half = veh.length * 0.5f;
            float ry = Mathf.Max(RoadProfile.RoadY(p.x, p.z),
                Mathf.Max(RoadProfile.RoadY(p.x + tn0.x * half, p.z + tn0.z * half),
                          RoadProfile.RoadY(p.x - tn0.x * half, p.z - tn0.z * half)));
            if (p.y < ry) p.y = ry;

            if (p.y > -0.5f) p.y = Mathf.Max(p.y, RoadProfile.LidY(p.x, p.z) + 0.02f);
            Vector3 tan = g.Tangent(veh.s);
            Vector3 tanF = new Vector3(tan.x, tan.y * 0.35f, tan.z);
            if (drape && georef != null)
            {
                float y = DrapeY(p) + lift;
                go.transform.localPosition = new Vector3(p.x, y, p.z);
                if (tanF.sqrMagnitude > 1e-4f)
                    go.transform.localRotation = Quaternion.LookRotation(tanF, Vector3.up) * Quaternion.Euler(0f, yaw, 0f);
            }
            else
            {
                go.transform.position = p + Vector3.up * lift;
                if (tanF.sqrMagnitude > 1e-4f)
                    go.transform.rotation = Quaternion.LookRotation(tanF, Vector3.up) * Quaternion.Euler(0f, yaw, 0f);
            }
        }

        float DrapeY(Vector3 localPt)
        {
            Vector3 wHigh = georef.TransformPoint(new Vector3(localPt.x, 320f, localPt.z));
            Vector3 wLow = georef.TransformPoint(new Vector3(localPt.x, -120f, localPt.z));
            Vector3 dir = wLow - wHigh;
            float dist = dir.magnitude;
            var hits = Physics.RaycastAll(wHigh, dir.normalized, dist);
            if (hits == null || hits.Length == 0) return localPt.y;
            bool underpass = localPt.y < -1f;
            float bestLocalY = georef.InverseTransformPoint(hits[0].point).y;
            for (int i = 1; i < hits.Length; i++)
            {
                float ly = georef.InverseTransformPoint(hits[i].point).y;
                if (underpass ? (ly < bestLocalY) : (ly > bestLocalY)) bestLocalY = ly;
            }
            return bestLocalY;
        }

        void SyncCars()
        {
            alive.Clear();
            foreach (var lk in world.links)
                foreach (var veh in lk.vehicles) Place(veh, lk.geom);
            foreach (var cn in world.connectors)
                foreach (var veh in cn.vehicles) Place(veh, cn.geom);

            var gone = new List<int>();
            foreach (var kv in cars) if (!alive.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone) { Destroy(cars[id]); cars.Remove(id); carLift.Remove(id); carYaw.Remove(id); }
        }

        void OnApplicationQuit()
        {
            if (logging && !flushed && metrics != null) metrics.Flush();
        }
    }
}
