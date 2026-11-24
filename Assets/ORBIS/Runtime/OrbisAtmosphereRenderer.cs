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
        [Tooltip("A/B verification of the original fourfold cloud integration cost.")]
        public bool referenceCloudIntegration;
        [Range(0,5)]public int cloudDebug;
        Camera cameraComponent;Material composite,display,clearSky;
        OrbisCelestialLens lens;
        readonly Vector3[] corners=new Vector3[4];
        readonly RenderBuffer[] buffers=new RenderBuffer[2];
        bool warned;
        static readonly string[] RayNames={"_RayBL","_RayTL","_RayTR","_RayBR"};
        static readonly string[] MapNames={"_MapBL","_MapTL","_MapTR","_MapBR"};
        static readonly Vector2[] MapUV={new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),new Vector2(1,0)};
        static readonly float[] Scale={.56f,.68f,.80f,1f},RefScale={.65f,.78f,.86f,1f};
        static readonly int[] CloudSteps={26,36,48,64},RefCloudSteps={32,48,64,64};
        static readonly int[] AirSteps={10,14,20,48},RefAirSteps={16,24,32,48};
        static readonly int[] LightSteps={3,4,5,8},RefLightSteps={4,6,8,8},CloudRefinement={1,2,2,3};
        void OnEnable()
        {
            cameraComponent=GetComponent<Camera>();cameraComponent.depthTextureMode|=DepthTextureMode.Depth;
            Shader shader=Resources.Load<Shader>("ORBIS/AtmosphereComposite");
            if(shader&&shader.isSupported)composite=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            Shader finalShader=Resources.Load<Shader>("ORBIS/Display");
            if(finalShader&&finalShader.isSupported)display=new Material(finalShader){hideFlags=HideFlags.HideAndDontSave};
            Shader skyShader=Resources.Load<Shader>("ORBIS/CelestialLens");
            if(skyShader&&skyShader.isSupported)clearSky=new Material(skyShader){hideFlags=HideFlags.HideAndDontSave};
        }
        void SetRays(Material m)
        {
            var projection=cameraComponent.projectionMatrix;
            Vector3 originRight=Vector3.zero,originUp=Vector3.zero;
            Vector4 depthProjection=Vector4.zero;
            if(scroll&&scroll.IsTransitioning&&projection.m33>0)
            {
                // The unfold interpolates projection matrices. Each viewport ray then has
                // an offset at eye depth zero and a slope determined by clip W(z).
                float offset=projection.m33,slope=-projection.m32;
                float near=cameraComponent.nearClipPlane,far=cameraComponent.farClipPlane;
                float k=(offset+slope*near)/(far-near);
                depthProjection=new Vector4(far*k,offset,k,slope);
                originRight=view.transform.InverseTransformVector(cameraComponent.transform.right)*(offset/projection.m00);
                originUp=view.transform.InverseTransformVector(cameraComponent.transform.up)*(offset/projection.m11);
                for(int i=0;i<4;i++)
                    corners[i]=new Vector3((2*MapUV[i].x-1)*slope/projection.m00,
                                           (2*MapUV[i].y-1)*slope/projection.m11,1);
            }
            else cameraComponent.CalculateFrustumCorners(new Rect(0,0,1,1),1,Camera.MonoOrStereoscopicEye.Mono,corners);
            m.SetVector("_OrbisDepthProjection",depthProjection);
            m.SetVector("_OrbisRayOriginRight",originRight);m.SetVector("_OrbisRayOriginUp",originUp);
            // Unity corners: bottom-left, top-left, top-right, bottom-right.
            string[] names=RayNames;
            for(int i=0;i<4;i++)m.SetVector(names[i],view.transform.InverseTransformVector(cameraComponent.transform.TransformVector(corners[i])));
            m.SetVector("_CameraLocal",view.transform.InverseTransformPoint(cameraComponent.transform.position));
        }
        bool SetAtlasRays(Material m)
        {
            if(scroll)
            {
                bool local=!scroll.IsAtlas;
                if(m.HasProperty("_MarkleyDirections"))Orbis.Planet.MarkleyProjection.Bind(m);m.SetFloat("_MarkleyMap",local?0:1);
                m.SetFloat("_LocalCloudMap",local?1:0);
                if(local)
                {
                    var frame=scroll.LocalFrame;
                    m.SetFloat("_CloudProjectionBlend",scroll.Morph);
                    m.SetVector("_CloudLocalUp",new Vector4((float)frame.Up.X,(float)frame.Up.Y,(float)frame.Up.Z,0));
                    m.SetVector("_CloudLocalEast",new Vector4((float)frame.East.X,(float)frame.East.Y,(float)frame.East.Z,0));
                    m.SetVector("_CloudLocalNorth",new Vector4((float)frame.North.X,(float)frame.North.Y,(float)frame.North.Z,0));
                    // A deep deck stays opaque until only its lowest kilometre is ahead.
                    // Give this last stretch its own wheel travel in both directions.
                    float upper=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.50f,.78f,scroll.Morph));
                    float lower=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.67f,1,scroll.Morph));
                    const float aboveClouds=14500,lowerDeck=1600,belowClouds=150;
                    float height=belowClouds+(lowerDeck-belowClouds)*(1-lower)+(aboveClouds-lowerDeck)*(1-upper);
                    m.SetFloat("_CloudCameraHeight",height);
                }
                for(int i=0;i<4;i++)
                {
                    Vector2 screen=cameraComponent.pixelRect.min+Vector2.Scale(MapUV[i],cameraComponent.pixelRect.size);
                    if(!scroll.TryProjectionPoint(screen,out Vector2 tile))return false;
                    var world=view.world.FlatSource;
                    m.SetVector(MapNames[i],new Vector4(tile.x/world.Width,tile.y/scroll.MapHeight,0,0));
                }
                m.SetFloat("_ScrollCloudSource",1);return true;
            }
            m.SetFloat("_LocalCloudMap",0);m.SetFloat("_MarkleyMap",0);if(!atlas)return false;
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
            bool map=scroll?scroll.IsAtlas:atlas&&atlas.isActiveAndEnabled&&cameraComponent.orthographic;
            bool natural=view.layer==0||view.layer==1;
            bool clear=!settings.atmosphere&&(!settings.clouds||(map&&!scroll&&!settings.mapClouds));
            if((scroll&&scrollVolumeWeight<=.0001f)||settings.surfaceDebug>0||!natural||(!view.VolumeVisible&&!map)||
                (clear&&(map||cameraComponent.orthographic||!clearSky)))
            {DisplayOnly(source,destination);return;}
            Material m=view.CloudMaterial;
            if(!m||!m.shader.isSupported){DisplayOnly(source,destination);return;}
            int q=Mathf.Clamp(settings.cloudQuality,0,3);
            // Crossing the globe/local threshold can otherwise issue a burst of
            // full-quality HP ray marches while the camera changes projection.
            // Keep resolution stable while wheeling. Per-frame quality switches
            // at the transition boundary caused allocation and rendering spikes.
            m.SetFloat("_ScrollVolumeWeight",scroll?scrollVolumeWeight:1);
            m.SetFloat("_CloudViewBlend",0);m.SetFloat("_LocalCloudMap",0);
            m.SetInt("_CloudSteps",referenceCloudIntegration?RefCloudSteps[q]:CloudSteps[q]);m.SetInt("_AirSteps",referenceCloudIntegration?RefAirSteps[q]:AirSteps[q]);m.SetInt("_LightSteps",referenceCloudIntegration?RefLightSteps[q]:LightSteps[q]);m.SetInt("_CloudDebug",cloudDebug);
            m.SetFloat("_CloudRefinement",referenceCloudIntegration?4:CloudRefinement[q]);
            m.SetFloat("_CloudLightSimple",referenceCloudIntegration?0:1);
            if(map)
            {
                m.SetFloat("_Clouds",settings.clouds&&(scroll||settings.mapClouds)?1:0);
                if(!SetAtlasRays(m)){DisplayOnly(source,destination);return;}
            }
            else
            {
                if(cameraComponent.orthographic&&(!scroll||scroll.Morph>=1)){DisplayOnly(source,destination);return;}
                SetRays(m);
                if(scroll&&!scroll.IsAtlas&&scroll.Morph>.12f&&SetAtlasRays(m))
                    m.SetFloat("_CloudViewBlend",Mathf.SmoothStep(0,1,Mathf.InverseLerp(.12f,.78f,scroll.Morph)));
            }
            Vector4 lensSun=Vector4.zero;
            if(!map)
            {
                if(!lens)TryGetComponent(out lens);
                if(lens&&lens.isActiveAndEnabled&&lens.view==view&&cloudDebug==0)
                    lensSun=lens.Sample(cameraComponent,source,m);
                OrbisCelestialLens.Bind(m,lens,lensSun);
                if(clear)
                {
                    SetRays(clearSky);view.BindOpticalSettings(clearSky);
                    clearSky.SetVector("_Sun",view.VisualSunLocal);
                    OrbisCelestialLens.Bind(clearSky,lens,lensSun);
                    Present(source,destination,clearSky);return;
                }
            }
            bool mapMRT=map&&m.passCount>3&&composite&&composite.passCount>1;
            if(fullResolutionReference||q==3||SystemInfo.supportedRenderTargetCount<2||!composite||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)||(map&&!mapMRT))
            {m.SetVector("_VolumeTexelSize",new Vector4(1f/Mathf.Max(1,source.width),1f/Mathf.Max(1,source.height),source.width,source.height));Present(source,destination,m,map?2:1);return;}
            float renderScale=referenceCloudIntegration?Mathf.Max(RefScale[q],q>=2?1f:RefScale[q]):Scale[q];
            int width=Mathf.Max(4,Mathf.CeilToInt(source.width*renderScale)),height=Mathf.Max(4,Mathf.CeilToInt(source.height*renderScale));
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
                m.SetVector("_VolumeTexelSize",new Vector4(1f/width,1f/height,width,height));
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
                if(map)SetAtlasRays(composite);
                else {SetRays(composite);OrbisCelestialLens.Bind(composite,lens,lensSun);}
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
        void OnDisable(){if(composite)Destroy(composite);if(display)Destroy(display);if(clearSky)Destroy(clearSky);composite=null;display=null;clearSky=null;}
    }
}
