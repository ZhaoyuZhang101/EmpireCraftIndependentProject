using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis
{
    /// <summary>Built-in mono globe rays and orthographic atlas columns. Combined air/cloud quadrature outputs
    /// separate RGB scattering and RGB transmittance; source terrain never gets downsampled.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class OrbisAtmosphereRenderer:MonoBehaviour
    {
        public OrbisPlanetView view;
        public OrbisMapView atlas;
        public Orbis.Flat.FlatScrollNavigator scroll;
        public float scrollVolumeWeight=1;
        [Tooltip("Bypass half-resolution buffers to compare thin silhouettes and UV orientation.")]
        public bool fullResolutionReference;
        [Range(0,5)]public int cloudDebug;
        Camera cameraComponent;Material composite,display;
        readonly Vector3[] corners=new Vector3[4];
        readonly RenderBuffer[] buffers=new RenderBuffer[2];
        bool warned;
        static readonly string[] RayNames={"_RayBL","_RayTL","_RayTR","_RayBR"};
        static readonly string[] MapNames={"_MapBL","_MapTL","_MapTR","_MapBR"};
        static readonly Vector2[] MapUV={new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),new Vector2(1,0)};
        static readonly float[] Scale={.65f,.8f,1f,1f};
        static readonly int[] CloudSteps={32,48,64,64};
        static readonly int[] AirSteps={16,24,32,48};
        static readonly int[] LightSteps={4,6,8,8};
        void OnEnable()
        {
            cameraComponent=GetComponent<Camera>();cameraComponent.depthTextureMode|=DepthTextureMode.Depth;
            Shader shader=Resources.Load<Shader>("ORBIS/AtmosphereComposite");
            if(shader&&shader.isSupported)composite=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            Shader finalShader=Resources.Load<Shader>("ORBIS/Display");
            if(finalShader&&finalShader.isSupported)display=new Material(finalShader){hideFlags=HideFlags.HideAndDontSave};
        }
        void SetRays(Material m)
        {
            cameraComponent.CalculateFrustumCorners(new Rect(0,0,1,1),1,Camera.MonoOrStereoscopicEye.Mono,corners);
            // Unity corners: bottom-left, top-left, top-right, bottom-right.
            string[] names=RayNames;
            for(int i=0;i<4;i++)m.SetVector(names[i],view.transform.InverseTransformVector(cameraComponent.transform.TransformVector(corners[i])));
            m.SetVector("_CameraLocal",view.transform.InverseTransformPoint(cameraComponent.transform.position));
        }
        bool SetAtlasRays(Material m)
        {
            if(scroll)
            {
                if(m.HasProperty("_MarkleyDirections"))Orbis.Planet.MarkleyProjection.Bind(m);m.SetFloat("_MarkleyMap",1);
                for(int i=0;i<4;i++)
                {
                    Vector2 screen=cameraComponent.pixelRect.min+Vector2.Scale(MapUV[i],cameraComponent.pixelRect.size);
                    if(!scroll.TryProjectionPoint(screen,out Vector2 tile))return false;
                    var world=view.world.FlatSource;
                    m.SetVector(MapNames[i],new Vector4(tile.x/world.Width,tile.y/scroll.MapHeight,0,0));
                }
                m.SetFloat("_ScrollCloudSource",1);return true;
            }
            m.SetFloat("_MarkleyMap",0);if(!atlas)return false;
            Plane plane=new Plane(atlas.transform.forward,atlas.transform.position);
            Vector2[] uv=MapUV;
            string[] names=MapNames;
            for(int i=0;i<4;i++)
            {
                Ray ray=cameraComponent.ViewportPointToRay(uv[i]);
                if(!plane.Raycast(ray,out float distance))return false;
                m.SetVector(names[i],atlas.transform.InverseTransformPoint(ray.GetPoint(distance)));
            }
            return true;
        }
        void Present(RenderTexture source,RenderTexture destination,Material material,int pass=0)
        {
            bool previous=GL.sRGBWrite;
            try
            {
                // Working radiance is linear even in a Gamma project. Display.shader encodes
                // Gamma projects itself; only Linear destinations request hardware sRGB writes.
                GL.sRGBWrite=QualitySettings.activeColorSpace==ColorSpace.Linear&&(destination==null||destination.sRGB);
                if(material)Graphics.Blit(source,destination,material,pass);else Graphics.Blit(source,destination);
            }
            finally{GL.sRGBWrite=previous;}
        }
        void DisplayOnly(RenderTexture source,RenderTexture destination)
        {
            if(view&&display)view.BindOpticalSettings(display);
            Present(source,destination,display);
        }
        void OnRenderImage(RenderTexture source,RenderTexture destination)
        {
            // Editor reload can render a frame before the runtime world is restored.
            if(!view||view.world==null||view.world.settings==null||!cameraComponent||cameraComponent.stereoEnabled||GraphicsSettings.currentRenderPipeline!=null)
            {
                if(GraphicsSettings.currentRenderPipeline!=null&&!warned){Debug.LogWarning("ORBIS V4.1 needs Built-in Render Pipeline; no URP/HDRP pass is installed.");warned=true;}
                Graphics.Blit(source,destination);return;
            }
            var settings=view.world.settings;
            bool map=scroll?scroll.Morph>=.5f:atlas&&atlas.isActiveAndEnabled&&cameraComponent.orthographic;
            bool natural=view.layer==0||view.layer==1;
            if((scroll&&scrollVolumeWeight<=.0001f)||settings.surfaceDebug>0||!natural||(!view.VolumeVisible&&!map)||
                (!settings.atmosphere&&(!settings.clouds||(map&&!scroll&&!settings.mapClouds))))
            {DisplayOnly(source,destination);return;}
            Material m=view.CloudMaterial;
            if(!m||!m.shader.isSupported){DisplayOnly(source,destination);return;}
            int q=Mathf.Clamp(settings.cloudQuality,0,3);
            // Crossing the globe/local threshold can otherwise issue a burst of
            // full-quality HP ray marches while the camera changes projection.
            if(scroll&&scroll.IsTransitioning)q=0;
            m.SetFloat("_ScrollVolumeWeight",scroll?scrollVolumeWeight:1);
            m.SetInt("_CloudSteps",CloudSteps[q]);m.SetInt("_AirSteps",AirSteps[q]);m.SetInt("_LightSteps",LightSteps[q]);m.SetInt("_CloudDebug",cloudDebug);
            if(map)
            {
                m.SetFloat("_Clouds",settings.clouds&&(scroll||settings.mapClouds)?1:0);
                if(!SetAtlasRays(m)){DisplayOnly(source,destination);return;}
            }
            else
            {
                if(cameraComponent.orthographic){DisplayOnly(source,destination);return;}
                SetRays(m);
            }
            bool mapMRT=map&&m.passCount>3&&composite&&composite.passCount>1;
            if(fullResolutionReference||q==3||SystemInfo.supportedRenderTargetCount<2||!composite||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)||(map&&!mapMRT))
            {Present(source,destination,m,map?2:1);return;}
            int width=Mathf.Max(4,Mathf.CeilToInt(source.width*Scale[q])),height=Mathf.Max(4,Mathf.CeilToInt(source.height*Scale[q]));
            RenderTexture light=null,transmission=null;RenderTexture previous=RenderTexture.active;
            bool oldSRGB=GL.sRGBWrite;
            try
            {
                light=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
                transmission=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
                light.filterMode=FilterMode.Point;transmission.filterMode=FilterMode.Point;
                light.wrapMode=TextureWrapMode.Clamp;transmission.wrapMode=TextureWrapMode.Clamp;
                buffers[0]=light.colorBuffer;buffers[1]=transmission.colorBuffer;
                GL.sRGBWrite=false;Graphics.SetRenderTarget(buffers,light.depthBuffer);
                m.SetTexture("_MainTex",source);
                GL.PushMatrix();
                try
                {
                    GL.LoadOrtho();
                    if(!m.SetPass(map?3:0))throw new System.InvalidOperationException("Atmosphere MRT pass is unavailable.");
                    GL.Begin(GL.QUADS);
                    GL.TexCoord2(0,0);GL.Vertex3(0,0,.1f);
                    GL.TexCoord2(1,0);GL.Vertex3(1,0,.1f);
                    GL.TexCoord2(1,1);GL.Vertex3(1,1,.1f);
                    GL.TexCoord2(0,1);GL.Vertex3(0,1,.1f);
                    GL.End();
                }
                finally {GL.PopMatrix();}
                RenderTexture.active=previous;GL.sRGBWrite=oldSRGB;
                composite.SetTexture("_RadianceTex",light);composite.SetTexture("_TransmittanceTex",transmission);
                composite.SetVector("_VolumeTexelSize",new Vector4(1f/width,1f/height,width,height));
                view.BindOpticalSettings(composite);
                if(map)SetAtlasRays(composite);else SetRays(composite);
                composite.SetVector("_Sun",view.VisualSunLocal);Present(source,destination,composite,map?1:0);
            }
            catch(System.Exception e)
            {
                // Keep the game visible and switch to the simpler, full-resolution reference path.
                RenderTexture.active=previous;GL.sRGBWrite=oldSRGB;fullResolutionReference=true;
                if(!warned){Debug.LogWarning("ORBIS MRT unavailable; using full-resolution atmosphere. "+e.Message);warned=true;}
                Present(source,destination,m,map?2:1);
            }
            finally
            {
                // OnRenderImage must finish with its destination bound. Restoring the
                // entry target after Present leaves the image-effect chain on the source.
                RenderTexture.active=destination;GL.sRGBWrite=oldSRGB;
                if(light)RenderTexture.ReleaseTemporary(light);if(transmission)RenderTexture.ReleaseTemporary(transmission);
            }
        }
        void OnDisable(){if(composite)Destroy(composite);if(display)Destroy(display);composite=null;display=null;}
    }
}
