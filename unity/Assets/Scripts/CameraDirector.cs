using UnityEngine;

namespace OndaVerde
{
    public class CameraDirector : MonoBehaviour
    {
        [System.Serializable]
        public class Shot
        {
            public string name;
            public string title;
            public string subtitle;
            public Vector3 pos;
            public Vector3 look;
            public float fov = 45f;
        }

        public Shot[] shots;
        public bool autoCycle = true;
        public float dwell = 10f;
        public float orbit = 2.2f;
        public float blend = 0.85f;

        Camera cam;
        int idx = 0;
        float t = 0f;
        float ang = 0f;
        Vector3 basePos, lookAt;
        Vector3 curPos, curLook;
        float curFov;
        GUIStyle title;

        void Start()
        {
            cam = GetComponent<Camera>();
            if (shots == null || shots.Length == 0) return;
            basePos = shots[0].pos; lookAt = shots[0].look;
            curPos = basePos; curLook = lookAt; curFov = shots[0].fov;
            cam.transform.position = curPos; cam.transform.LookAt(curLook);
            cam.fieldOfView = curFov;
        }

        void Update()
        {
            if (shots == null || shots.Length == 0) return;
            for (int i = 0; i < shots.Length && i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Apply(i);
            if (Input.GetKeyDown(KeyCode.Space)) autoCycle = !autoCycle;

            ang += orbit * Time.deltaTime;
            Vector3 off = basePos - lookAt;
            float r = Mathf.Sqrt(off.x * off.x + off.z * off.z);
            float a0 = Mathf.Atan2(off.z, off.x) + Mathf.Deg2Rad * ang;
            Vector3 target = lookAt + new Vector3(Mathf.Cos(a0) * r, off.y, Mathf.Sin(a0) * r);

            float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.05f, blend));
            curPos = Vector3.Lerp(curPos, target, k);
            curLook = Vector3.Lerp(curLook, lookAt, k);
            curFov = Mathf.Lerp(curFov, shots[idx].fov, k);
            cam.transform.position = curPos;
            cam.transform.LookAt(curLook);
            cam.fieldOfView = curFov;

            if (autoCycle)
            {
                t += Time.deltaTime;
                if (t > dwell) Apply((idx + 1) % shots.Length);
            }
        }

        void Apply(int i)
        {
            idx = i; t = 0f; ang = 0f;
            basePos = shots[i].pos;
            lookAt = shots[i].look;
        }

        void OnGUI()
        {
            if (shots == null || shots.Length == 0) return;
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
                title.normal.textColor = Color.white;
            }
            var s = shots[idx];
            GUI.color = new Color(0f, 0f, 0f, 0.4f);
            GUI.DrawTexture(new Rect(18, 18, 300, 44), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(32, 26, 280, 30), string.IsNullOrEmpty(s.title) ? s.name : s.title, title);
        }
    }
}
