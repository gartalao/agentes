using System;

namespace OndaVerde
{
    [Serializable] public class PaintDoc { public bool edgeOuter, dashL, dashR, yellowPair; }
    [Serializable] public class SigDoc { public float x, y; public string group; }
    [Serializable] public class StopDoc { public float sAlong; public string phaseGroup; }

    [Serializable]
    public class LinkDoc
    {
        public string id, kind, dir, fromNode, toNode;
        public int laneIndex, n;
        public float[] pts;
        public float widthM, speed;
        public PaintDoc paint;
        public StopDoc[] stops;
    }

    [Serializable]
    public class ConnDoc
    {
        public string id, fromLink, toLink, movement, control, phaseGroup;
        public int n;
        public float[] pts;
        public float widthM;
        public string[] conflicts;
    }

    [Serializable]
    public class PhaseDoc
    {
        public string id, label;
        public float greenStart, greenDur, yellow, allred;
    }

    [Serializable]
    public class BldgDoc { public string name, fbx; public float ox, oz, rotDeg, scale; }

    [Serializable]
    public class ZoneDoc
    {
        public string id;
        public float enuX, enuZ, bearingDeg, sM, cycle, yellow, allred, medianW, H;
        public bool desnivel;
        public LinkDoc[] links;
        public ConnDoc[] connectors;
        public PhaseDoc[] phaseGroups;
        public BldgDoc[] buildings;
        public SigDoc[] signals;
    }

    [Serializable]
    public class DipDoc { public string id; public float sC, depth, halfDeck, ramp; }

    public class NetDoc
    {
        public float designSpeedKmh;
        public string[] order;
        public ZoneDoc[] zones;
        public float[] axisCm;
        public float expressHalf;
        public float d0Off;
        public DipDoc[] dips;
    }
}
