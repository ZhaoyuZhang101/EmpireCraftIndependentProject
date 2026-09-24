using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace Orbis
{
    public sealed class OrbisSelectionView:MonoBehaviour
    {
        public OrbisDemo demo;public int Hovered=-1;
        Mesh mesh;Material material;int selected=-2,hover=-2,revision=-1,geography=-1,mode=-1;
        int gridFocus=-1,lastGridFocus=-2,selectedPlot=-2;bool map,gridShown;
        OrbisWorld lastWorld;float elapsed,lastScale,nextGridPick;
        struct Line{public Vector3 a,b;public bool hover,inner,background;}
        readonly List<Line> lines=new List<Line>(2048);
        readonly List<Vector3> vertices=new List<Vector3>(16000);readonly List<Color> colors=new List<Color>(16000);readonly List<int> indices=new List<int>(24000);
        readonly HashSet<int> neighborhood=new HashSet<int>();
        readonly List<int> ring=new List<int>(16);
        public int BoundarySegments=>lines.Count;
        void Awake()
        {
            mesh=new Mesh{name="Stable 4x4 Zone selections",indexFormat=IndexFormat.UInt32};mesh.MarkDynamic();
            material=new Material(Resources.Load<Shader>("ORBIS/Selection"));
            gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
        }
        public void Invalidate(){selected=-2;nextGridPick=0;}
        void LateUpdate()
        {
            if(demo==null||demo.World==null)return;OrbisWorld w=demo.World;
            transform.localRotation=demo.MapMode?Quaternion.identity:Quaternion.Euler(0,demo.VisualRotation,0);
            material.SetFloat("_Map",demo.MapMode?1:0);
            bool show=w.settings.localGrid&&(demo.MapMode?demo.PlanetCamera.orthographicSize<.45f:demo.Altitude<.09f);
            if(show&&Time.unscaledTime>=nextGridPick)
            {nextGridPick=Time.unscaledTime+.20f;gridFocus=demo.TryPick(new Vector2(.5f,.5f),out int focus)?focus:-1;}
            if(!show)gridFocus=-1;
            float scale=demo.MapMode?demo.PlanetCamera.orthographicSize*2/Mathf.Max(1,Screen.height):
                Mathf.Max(.008f,demo.Altitude)*2*Mathf.Tan(demo.PlanetCamera.fieldOfView*.5f*Mathf.Deg2Rad)/Mathf.Max(1,Screen.height);
            bool dirty=selectedPlot!=demo.SelectedPlot||selected!=demo.Selected||hover!=Hovered||lastWorld!=w||revision!=w.Revision||geography!=w.geography.Revision||map!=demo.MapMode||mode!=w.settings.selectionMode||gridShown!=show||lastGridFocus!=gridFocus;
            if(dirty)
            {
                selected=demo.Selected;selectedPlot=demo.SelectedPlot;hover=Hovered;lastWorld=w;revision=w.Revision;geography=w.geography.Revision;map=demo.MapMode;mode=w.settings.selectionMode;gridShown=show;lastGridFocus=gridFocus;
                lines.Clear();
                // Only a small cached neighborhood, never 55,296 GameObjects/colliders.
                if(show&&gridFocus>=0)
                {
                    neighborhood.Clear();ring.Clear();neighborhood.Add(gridFocus);ring.Add(gridFocus);
                    for(int n=0;n<2;n++)
                    {
                        int count=ring.Count;
                        for(int k=0;k<count;k++)foreach(int j in w.grid.neighbors[ring[k]])if(neighborhood.Add(j))ring.Add(j);
                    }
                    foreach(int zone in ring)if(zone!=selected&&zone!=hover)BuildZone(zone,false,true);
                }
                BuildLines(selected,false);if(hover!=selected)BuildLines(hover,true);
                if(selectedPlot>=0&&selectedPlot/16==selected)BuildPickedPlot(selectedPlot);
            }
            elapsed+=Time.unscaledDeltaTime;
            if(dirty||elapsed>.08f&&Mathf.Abs(scale-lastScale)>Mathf.Max(1e-8f,lastScale*.08f))
            {elapsed=0;lastScale=scale;Draw(scale);}
        }
        void AddArc(Vector3 p,Vector3 q,bool hovering,bool inner,bool background)
        {
            float lift=background?.000065f:.00010f;
            if(!demo.MapMode)
            {lines.Add(new Line{a=p*(demo.View.VisibleRadiusAt(p)+lift),b=q*(demo.View.VisibleRadiusAt(q)+lift),hover=hovering,inner=inner,background=background});return;}
            Vector3 x=OrbisMapView.Project(p),y=OrbisMapView.Project(q);float reference=OrbisMapView.Project((p+q).normalized).x;
            if(Mathf.Abs(p.y)>.999999f)x.x=reference;if(Mathf.Abs(q.y)>.999999f)y.x=reference;
            x.x+=Mathf.Round((reference-x.x)/(2*Mathf.PI))*2*Mathf.PI;y.x+=Mathf.Round((reference-y.x)/(2*Mathf.PI))*2*Mathf.PI;x.z=y.z=background?.015f:.016f;
            for(int copy=-1;copy<=1;copy++)lines.Add(new Line{a=x+Vector3.right*(copy*2*Mathf.PI),b=y+Vector3.right*(copy*2*Mathf.PI),hover=hovering,inner=inner,background=background});
        }
        void BuildZone(int zone,bool hovering,bool background)
        {
            const int steps=12;
            for(int axis=0;axis<2;axis++)for(int line=0;line<=OrbisZoneLayout.Side;line++)
            {
                float f=line/(float)OrbisZoneLayout.Side;bool inner=line>0&&line<OrbisZoneLayout.Side;
                for(int k=0;k<steps;k++)
                {
                    float u=k/(float)steps,v=(k+1)/(float)steps;
                    Vector3 p=OrbisZoneLayout.Point(demo.World,zone,axis==0?f:u,axis==0?u:f);
                    Vector3 q=OrbisZoneLayout.Point(demo.World,zone,axis==0?f:v,axis==0?v:f);
                    AddArc(p,q,hovering,inner,background);
                }
            }
        }
        void BuildPickedPlot(int id)
        {
            int zone=id/16,x=id%4,y=id%16/4;
            for(int edge=0;edge<4;edge++)for(int k=0;k<3;k++)
            {
                float t=k/3f,u=(k+1)/3f;
                Vector2 a=edge==0?new Vector2(x+t,y):edge==1?new Vector2(x+1,y+t):edge==2?new Vector2(x+1-t,y+1):new Vector2(x,y+1-t);
                Vector2 b=edge==0?new Vector2(x+u,y):edge==1?new Vector2(x+1,y+u):edge==2?new Vector2(x+1-u,y+1):new Vector2(x,y+1-u);
                AddArc(OrbisZoneLayout.Point(demo.World,zone,a.x/4,a.y/4),OrbisZoneLayout.Point(demo.World,zone,b.x/4,b.y/4),true,false,false);
            }
        }
        void BuildLines(int tile,bool hovering)
        {
            if(tile<0||tile>=demo.World.tiles.Length)return;
            if(demo.World.settings.selectionMode==0){BuildZone(tile,hovering,false);return;}
            OrbisWorld w=demo.World;int[] selectedTiles=w.SelectionTiles(tile,w.settings.selectionMode);
            var edges=new Dictionary<ulong,Vector2Int>();var counts=new Dictionary<ulong,int>();
            foreach(int t in selectedTiles)
            {
                int[] corners=w.grid.tileCorners[t];for(int k=0;k<corners.Length;k++)
                {
                    int a=corners[k],b=corners[(k+1)%corners.Length];uint lo=(uint)Mathf.Min(a,b),hi=(uint)Mathf.Max(a,b);ulong key=((ulong)lo<<32)|hi;
                    if(counts.ContainsKey(key))counts[key]++;else{counts[key]=1;edges[key]=new Vector2Int(a,b);}
                }
            }
            foreach(var edge in edges)if(counts[edge.Key]==1)
            {
                Vector3 a=w.grid.corners[edge.Value.x],b=w.grid.corners[edge.Value.y];
                for(int k=0;k<12;k++)AddArc(Vector3.Slerp(a,b,k/12f),Vector3.Slerp(a,b,(k+1)/12f),hovering,false,false);
            }
            // Observing a natural region never hides the actual one-Zone claim target.
            BuildZone(tile,hovering,false);
        }
        void Draw(float pixel)
        {
            vertices.Clear();colors.Clear();indices.Clear();
            foreach(var line in lines)
            {
                Color color=line.background?new Color(.66f,.77f,.65f,.28f):line.hover?new Color(.25f,.85f,1,.85f):new Color(1,.8f,.28f,1);
                if(line.inner){color.a*=.52f;Add(line,color,pixel*.52f);}
                else if(line.background)Add(line,color,pixel*.65f);
                else{Add(line,color*new Color(1,1,1,.13f),pixel*5);Add(line,color*new Color(1,1,1,.28f),pixel*2.9f);Add(line,color,pixel*1.15f);}
            }
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();
        }
        void Add(Line line,Color color,float width)
        {
            Vector3 normal=demo.MapMode?Vector3.forward:(line.a+line.b).normalized;
            Vector3 side=Vector3.Cross(normal,line.b-line.a).normalized*Mathf.Clamp(width,.000004f,.006f);int n=vertices.Count;
            vertices.Add(line.a-side);vertices.Add(line.a+side);vertices.Add(line.b-side);vertices.Add(line.b+side);
            for(int k=0;k<4;k++)colors.Add(color);
            indices.Add(n);indices.Add(n+1);indices.Add(n+2);indices.Add(n+2);indices.Add(n+1);indices.Add(n+3);
        }
        void OnDestroy(){if(mesh)Destroy(mesh);if(material)Destroy(material);}
    }
}
