using UnityEngine;
namespace Orbis
{
    // Built-in lens response, evaluated after atmospheric transmittance and display transform.
    [RequireComponent(typeof(Camera))]
    public sealed class OrbisCelestialLens : MonoBehaviour
    {
        public OrbisPlanetView view;
        public Orbis.Flat.FlatScrollNavigator navigation;
        [Range(0,2)] public float strength=1.15f;
        Camera target;Material material;
        void OnEnable(){target=GetComponent<Camera>();var shader=Resources.Load<Shader>("ORBIS/CelestialLens");if(shader)material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};}
        void OnRenderImage(RenderTexture source,RenderTexture destination)
        {
            if(!material||!view||!view.CloudMaterial){Graphics.Blit(source,destination);return;}
            Vector3 sun=view.VisualSunLocal;sun.Normalize();
            Vector3 origin=view.transform.InverseTransformPoint(target.transform.position);
            float along=Vector3.Dot(origin,sun),impact=Vector3.Cross(origin,sun).magnitude;
            float visible=along>=0?1:Mathf.SmoothStep(0,1,Mathf.InverseLerp(.96f,1.08f,impact));
            if(navigation)visible*=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.06f,.55f,navigation.Morph));
            // TransformVector preserves the presentation's reflected Z axis.
            Vector3 worldSun=view.transform.TransformVector(sun).normalized;
            var viewport=target.WorldToViewportPoint(target.transform.position+worldSun*10);
            if(viewport.z<=0)visible=0;
            material.SetVector("_LensSun",new Vector4(viewport.x,viewport.y,visible*strength,source.width/(float)source.height));
            Graphics.Blit(source,destination,material);
        }
        void OnDisable(){if(material)Destroy(material);}
    }
}
