using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace Orbis
{
    // Two batched, depth-tested sprite meshes. Simulation remains in authoritative Tiles.
    // Replace these texture references in the Inspector with an art set; transparent PNG required.
    public sealed class OrbisBillboardActors:MonoBehaviour
    {
        public Texture2D buildingTexture,personTexture;
        OrbisPlanetView view;Mesh buildingMesh,personMesh;Material buildingMaterial,personMaterial;
        MeshRenderer buildingRenderer,personRenderer;
        struct Actor{public Vector3 direction;public float radius,size;public Color color;public int tile;}
        readonly List<Actor> buildings=new List<Actor>(),people=new List<Actor>();
        readonly List<Vector3> vertices=new List<Vector3>();readonly List<Vector2> uv=new List<Vector2>();
        readonly List<Color> colors=new List<Color>();readonly List<int> indices=new List<int>();
        public int BuildingCount=>buildings.Count;public int PersonMarkerCount=>people.Count;
        public void Initialize(OrbisPlanetView source)
        {
            view=source;
            if(!buildingTexture)buildingTexture=Resources.Load<Texture2D>("ORBIS/V4/Building");
            if(!personTexture)personTexture=Resources.Load<Texture2D>("ORBIS/V4/Person");
            Create("2D buildings",buildingTexture,out buildingMesh,out buildingMaterial,out buildingRenderer);
            Create("2D residents",personTexture,out personMesh,out personMaterial,out personRenderer);
            Refresh();
        }
        void Create(string title,Texture2D texture,out Mesh mesh,out Material mat,out MeshRenderer display)
        {
            var go=new GameObject(title,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(transform,false);
            mesh=new Mesh{name=title,indexFormat=IndexFormat.UInt32};mesh.MarkDynamic();go.GetComponent<MeshFilter>().sharedMesh=mesh;
            mat=new Material(Resources.Load<Shader>("ORBIS/SpriteWorld"));mat.mainTexture=texture;
            display=go.GetComponent<MeshRenderer>();display.sharedMaterial=mat;display.shadowCastingMode=ShadowCastingMode.Off;
        }
        public void Refresh()
        {
            if(view==null)return;buildings.Clear();people.Clear();OrbisWorld w=view.world;
            for(int i=0;i<w.tiles.Length;i++)
            {
                Tile t=w.tiles[i];if(t.owner<0||t.pop<35||t.h<=0||t.lake>.018f)continue;
                Vector3 p=w.grid.points[i];Color color=Color.Lerp(Color.white,w.civs[t.owner].color,.45f);
                int count=t.city?3:t.pop>300?2:1;
                Vector3 east=Vector3.Cross(p,Vector3.up).normalized;if(east.sqrMagnitude<.1f)east=Vector3.right;
                Vector3 north=Vector3.Cross(east,p).normalized;
                for(int k=0;k<count;k++)
                {
                    Vector3 d=(p+east*((k-1)*.008f)+north*(k%2)*.005f).normalized;
                    if(view.TerrainHeight(d,view.SurfaceSampler.Locate(d))<=0)continue;
                    float bed=view.RadiusAt(d);if(view.VisibleRadiusAt(d)>bed+.0002f)continue;
                    buildings.Add(new Actor{direction=d,radius=bed+.00007f,size=t.city?.009f:.006f,color=color,tile=i});
                }
                // These residents are visual population markers, not an invented per-person simulation.
                for(int k=0;k<2;k++)
                {Vector3 d=(p+east*(.007f+k*.004f)-north*.006f).normalized;float bed=view.RadiusAt(d);
                    if(view.TerrainHeight(d,view.SurfaceSampler.Locate(d))>0&&view.VisibleRadiusAt(d)<bed+.0002f)
                        people.Add(new Actor{direction=d,radius=bed+.00008f,size=.0038f,color=color,tile=i});}
                if(buildings.Count>=1200)break;
            }
        }
        void LateUpdate()
        {
            if(view==null||!view.viewCamera)return;
            bool show=view.world.settings.spriteActors&&!view.viewCamera.orthographic;
            buildingRenderer.enabled=personRenderer.enabled=show;if(!show)return;
            buildingMaterial.SetVector("_Sun",transform.TransformDirection(view.world.SunLocal));
            personMaterial.SetVector("_Sun",transform.TransformDirection(view.world.SunLocal));
            buildingMaterial.SetFloat("_Night",view.world.settings.night?1:0);personMaterial.SetFloat("_Night",view.world.settings.night?1:0);
            Draw(buildingMesh,buildings,false);Draw(personMesh,people,true);
        }
        void Draw(Mesh mesh,List<Actor> actors,bool walking)
        {
            vertices.Clear();uv.Clear();colors.Clear();indices.Clear();
            var camera=view.viewCamera;Vector3 cameraLocal=transform.InverseTransformPoint(camera.transform.position);
            Vector3 right=transform.InverseTransformDirection(camera.transform.right),up=transform.InverseTransformDirection(camera.transform.up);
            float clock=(float)(view.world.VisualTime%4096);
            Texture2D texture=walking?personTexture:buildingTexture;float aspect=texture?(float)texture.width/texture.height:1;
            foreach(var a in actors)
            {
                Vector3 foot=a.direction*a.radius;
                if(Vector3.Dot(a.direction,cameraLocal-foot)<=0)continue;
                // Hide residents from orbital distance instead of inflating people into giant billboards.
                if(walking&&Vector3.Distance(cameraLocal,foot)>.4f)continue;
                float bob=walking?Mathf.Abs(Mathf.Sin(clock*3+a.tile))*.00015f:0;
                Vector3 basePoint=foot+up*bob;int n=vertices.Count;
                vertices.Add(basePoint-right*a.size*aspect*.5f);vertices.Add(basePoint+right*a.size*aspect*.5f);
                vertices.Add(basePoint-right*a.size*aspect*.5f+up*a.size);vertices.Add(basePoint+right*a.size*aspect*.5f+up*a.size);
                uv.Add(new Vector2(0,0));uv.Add(new Vector2(1,0));uv.Add(new Vector2(0,1));uv.Add(new Vector2(1,1));
                for(int k=0;k<4;k++)colors.Add(a.color);indices.AddRange(new[]{n,n+2,n+1,n+1,n+2,n+3});
            }
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.SetUVs(0,uv);mesh.SetColors(colors);mesh.RecalculateNormals();mesh.RecalculateBounds();
        }
        void OnDestroy(){if(buildingMesh)Destroy(buildingMesh);if(personMesh)Destroy(personMesh);if(buildingMaterial)Destroy(buildingMaterial);if(personMaterial)Destroy(personMaterial);}
    }
}
