using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis
{
    public sealed class OrbisOverlays : MonoBehaviour
    {
        public OrbisDemo demo;
        Mesh mesh; Material material;
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<int> indices = new List<int>();
        readonly List<Color> colors = new List<Color>();
        struct Edge { public int a, b, c, d; }
        readonly List<Edge> boundaries = new List<Edge>();
        float elapsed;

        public void Initialize(OrbisDemo controller)
        {
            demo = controller; material = new Material(Resources.Load<Shader>("ORBIS/Overlay"));
            mesh = new Mesh { name = "風場、洋流、國界與軍團", indexFormat = IndexFormat.UInt32 }; mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            SphereGrid grid = demo.World.grid;
            for (int i = 0; i < grid.points.Length; i++) foreach (int j in grid.neighbors[i]) if (j > i)
            {
                int first = -1, second = -1;
                foreach (int a in grid.tileCorners[i]) foreach (int b in grid.tileCorners[j]) if (a == b) { if (first < 0) first = a; else second = a; }
                if (second >= 0) boundaries.Add(new Edge { a = i, b = j, c = first, d = second });
            }
        }
        public void Tick(float delta, bool force = false)
        {
            transform.localRotation=demo.MapMode?Quaternion.identity:Quaternion.Euler(0,demo.VisualRotation,0);
            elapsed += delta;
            if (!force && elapsed < .1f) return;
            elapsed = 0; Rebuild();
        }
        Vector3 Place(Vector3 p, float height, float lift = .004f)
        {
            return demo.MapMode ? OrbisMapView.Project(p, 0) + Vector3.forward * lift : p.normalized * (1 + height * .075f + lift);
        }
        void Segment(Vector3 a, Vector3 b, Color color, float width = .0015f)
        {
            if(demo.MapMode){if(Mathf.Abs(a.y)>Mathf.PI/2-.0001f)a.x=b.x;if(Mathf.Abs(b.y)>Mathf.PI/2-.0001f)b.x=a.x;}
            if (demo.MapMode && Mathf.Abs(a.x - b.x) > Mathf.PI) return;
            Vector3 normal = demo.MapMode ? Vector3.forward : (a + b).normalized;
            Vector3 across = Vector3.Cross(normal, b - a).normalized * width;
            int n = vertices.Count;
            vertices.Add(a - across); vertices.Add(a + across); vertices.Add(b - across); vertices.Add(b + across);
            indices.AddRange(new[] { n, n + 1, n + 2, n + 1, n + 3, n + 2 });
            for (int k = 0; k < 4; k++) colors.Add(color);
        }
        void Arrow(Vector3 p, Vector3 direction, float height, Color color, float length)
        {
            if (direction.sqrMagnitude < .00001f) return;
            Vector3 end = (p + direction.normalized * length).normalized;
            Vector3 side = Vector3.Cross(p, direction).normalized;
            Vector3 a = Place(p, height), b = Place(end, height);
            Segment(a, b, color);
            Segment(b, Place((end - direction.normalized * .013f + side * .008f).normalized, height), color);
            Segment(b, Place((end - direction.normalized * .013f - side * .008f).normalized, height), color);
        }
        void Rebuild()
        {
            OrbisWorld w = demo.World;
            vertices.Clear(); indices.Clear(); colors.Clear();
            for (int i = 0; i < w.tiles.Length; i++)
            {
                Tile t = w.tiles[i]; Vector3 p = w.grid.points[i];
                if (w.settings.showWind && i % 9 == 0) Arrow(p, t.wind, OrbisPlanetView.SurfaceHeight(t), new Color(.94f, .8f, .42f), .045f);
                if (w.settings.showCurrents && t.h <= 0 && i % 7 == 0) Arrow(p, t.current, 0, t.temp > 15 ? new Color(1, .45f, .25f) : new Color(.25f, .68f, 1), .045f);

            }
            foreach (var e in boundaries)
            {
                Tile a = w.tiles[e.a], b = w.tiles[e.b];
                bool political = w.settings.showBorders && a.owner != b.owner && (a.owner >= 0 || b.owner >= 0);
                bool zone = demo.View.layer == 8 && a.zoneId != b.zoneId;
                bool chunk = demo.View.layer == 9 && a.chunkId != b.chunkId;
                bool tile = demo.MapMode && demo.View.layer == 10;
                if (!political && !zone && !chunk && !tile) continue;
                float h = (OrbisPlanetView.SurfaceHeight(a) + OrbisPlanetView.SurfaceHeight(b)) * .5f;
                for(int k=0;k<6;k++)
                {
                    Vector3 p=Vector3.Slerp(w.grid.corners[e.c],w.grid.corners[e.d],k/6f),q=Vector3.Slerp(w.grid.corners[e.c],w.grid.corners[e.d],(k+1)/6f);
                    Vector3 start=demo.MapMode?Place(p,0):p*(demo.View.VisibleRadiusAt(p)+.00012f);
                    Vector3 end=demo.MapMode?Place(q,0):q*(demo.View.VisibleRadiusAt(q)+.00012f);
                    Segment(start,end,political?new Color(1,.82f,.43f):new Color(.67f,.83f,.86f),(political?1f:.65f)*(demo.MapMode?.0008f:Mathf.Clamp(demo.Altitude*.002f,.000018f,.0012f)));
                }
            }
            foreach (var e in w.expeditions)
            {
                Color color = w.civs[e.civ].color;
                for (int k = e.index; k + 1 < e.path.Length; k++)
                    Segment(Place(w.grid.points[e.path[k]], OrbisPlanetView.SurfaceHeight(w.tiles[e.path[k]])), Place(w.grid.points[e.path[k + 1]], OrbisPlanetView.SurfaceHeight(w.tiles[e.path[k + 1]])), color * .8f, .001f);
                Vector3 p = w.grid.points[e.tile];
                if (e.index + 1 < e.path.Length) p = Vector3.Slerp(p, w.grid.points[e.path[e.index + 1]], Mathf.Clamp01(e.progress));
                Vector3 east = Vector3.Cross(Vector3.up, p).normalized;
                if (east.sqrMagnitude < .1f) east = Vector3.right;
                Vector3 north = Vector3.Cross(p, east).normalized;
                float h = OrbisPlanetView.SurfaceHeight(w.tiles[e.tile]);
                Segment(Place(p - east * .01f, h, .012f), Place(p + east * .01f, h, .012f), color, .007f);
                Segment(Place(p - north * .01f, h, .013f), Place(p + north * .015f, h, .013f), e.settlers ? Color.white : color, .002f);
            }
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0); mesh.SetColors(colors); mesh.RecalculateBounds();
        }
        void OnDestroy() { if (mesh) Destroy(mesh); if (material) Destroy(material); }
    }
}
