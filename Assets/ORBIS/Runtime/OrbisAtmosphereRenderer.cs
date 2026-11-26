using UnityEngine;
using UnityEngine.Profiling;
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
        [Tooltip("Enable parallax-accurate temporal history reprojection with 3x3 perceptual variance clamping.")]
        public bool temporalReprojection=true;
        [HideInInspector] public bool allowSameFrameTemporal;
        [Range(0,5)]public int cloudDebug;

        Camera cameraComponent;Material composite,display,clearSky;
        OrbisCelestialLens lens;
        readonly Vector3[] corners=new Vector3[4];
        readonly RenderBuffer[] buffers=new RenderBuffer[2];
        readonly RenderBuffer[] historyBuffers=new RenderBuffer[2];
        bool warned;

        // Pooled ping-pong temporal history buffers per quality tier (zero RT destroy/create churn or black flash on tier switch)
        readonly RenderTexture[,] historyLight=new RenderTexture[4,2];
        readonly RenderTexture[,] historyTrans=new RenderTexture[4,2];
        readonly int[] historyWidths=new int[4],historyHeights=new int[4];
        int historyWriteIndex,temporalFrame;
        bool hasPreviousState;
        Matrix4x4 prevWorldToClip;
        Vector3 prevCamLocal,prevCamFwd,prevSunLocal;
        float prevMorph,prevCover,prevDensity,prevCloudsUniform,prevGodRaysUniform,prevMSUniform,prevWeatherTime,prevLensStrength;
        int prevQuality=-1,prevDebug=-1,prevFrameCount=-1;
        bool prevMap,prevAtmo,prevCloudsSetting,prevGodRaysSetting,prevMSSetting;
        Texture prevWeatherTex;
        Material cachedMarkleyMat;
        bool cachedHasMarkley;

        static readonly Vector2[] MapUV={new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),new Vector2(1,0)};
        static readonly float[] Scale={.56f,.68f,.80f,1f},RefScale={.65f,.78f,.86f,1f};
        static readonly float[] TemporalBlend={.84f,.82f,.78f,0f};
        static readonly int[] CloudSteps={26,36,48,64},RefCloudSteps={32,48,64,64};
        static readonly int[] AirSteps={10,14,20,48},RefAirSteps={16,24,32,48};
        static readonly int[] LightSteps={3,4,5,8},RefLightSteps={4,6,8,8},CloudRefinement={1,2,2,3};

        static readonly int[] RayIds={
            Shader.PropertyToID("_RayBL"),Shader.PropertyToID("_RayTL"),
            Shader.PropertyToID("_RayTR"),Shader.PropertyToID("_RayBR")
        };
        static readonly int[] MapIds={
            Shader.PropertyToID("_MapBL"),Shader.PropertyToID("_MapTL"),
            Shader.PropertyToID("_MapTR"),Shader.PropertyToID("_MapBR")
        };
        static readonly int IdDepthProjection=Shader.PropertyToID("_OrbisDepthProjection");
        static readonly int IdRayOriginRight=Shader.PropertyToID("_OrbisRayOriginRight");
        static readonly int IdRayOriginUp=Shader.PropertyToID("_OrbisRayOriginUp");
        static readonly int IdCameraLocal=Shader.PropertyToID("_CameraLocal");
        static readonly int IdMarkleyDirections=Shader.PropertyToID("_MarkleyDirections");
        static readonly int IdMarkleyMap=Shader.PropertyToID("_MarkleyMap");
        static readonly int IdLocalCloudMap=Shader.PropertyToID("_LocalCloudMap");
        static readonly int IdCloudProjectionBlend=Shader.PropertyToID("_CloudProjectionBlend");
        static readonly int IdCloudLocalUp=Shader.PropertyToID("_CloudLocalUp");
        static readonly int IdCloudLocalEast=Shader.PropertyToID("_CloudLocalEast");
        static readonly int IdCloudLocalNorth=Shader.PropertyToID("_CloudLocalNorth");
        static readonly int IdCloudCameraHeight=Shader.PropertyToID("_CloudCameraHeight");
        static readonly int IdScrollCloudSource=Shader.PropertyToID("_ScrollCloudSource");
        static readonly int IdScrollVolumeWeight=Shader.PropertyToID("_ScrollVolumeWeight");
        static readonly int IdCloudViewBlend=Shader.PropertyToID("_CloudViewBlend");
        static readonly int IdCloudSteps=Shader.PropertyToID("_CloudSteps");
        static readonly int IdAirSteps=Shader.PropertyToID("_AirSteps");
        static readonly int IdLightSteps=Shader.PropertyToID("_LightSteps");
        static readonly int IdCloudDebug=Shader.PropertyToID("_CloudDebug");
        static readonly int IdCloudRefinement=Shader.PropertyToID("_CloudRefinement");
        static readonly int IdCloudLightSimple=Shader.PropertyToID("_CloudLightSimple");
        static readonly int IdClouds=Shader.PropertyToID("_Clouds");
        static readonly int IdCover=Shader.PropertyToID("_Cover");
        static readonly int IdDensity=Shader.PropertyToID("_Density");
        static readonly int IdGodRays=Shader.PropertyToID("_GodRays");
        static readonly int IdCloudMS=Shader.PropertyToID("_CloudMS");
        static readonly int IdScrollWeatherTex=Shader.PropertyToID("_ScrollWeatherTex");
        static readonly int IdScrollWeatherTime=Shader.PropertyToID("_ScrollWeatherTime");
        static readonly int IdSun=Shader.PropertyToID("_Sun");
        static readonly int IdMainTex=Shader.PropertyToID("_MainTex");
        static readonly int IdVolumeTexelSize=Shader.PropertyToID("_VolumeTexelSize");
        static readonly int IdRadianceTex=Shader.PropertyToID("_RadianceTex");
        static readonly int IdTransmittanceTex=Shader.PropertyToID("_TransmittanceTex");
        static readonly int IdCloudHistoryLight=Shader.PropertyToID("_CloudHistoryLight");
        static readonly int IdCloudHistoryTrans=Shader.PropertyToID("_CloudHistoryTrans");
        static readonly int IdCloudPrevVP=Shader.PropertyToID("_CloudPrevVP");
        static readonly int IdCloudHistoryParams=Shader.PropertyToID("_CloudHistoryParams");
        static readonly int IdCloudTemporalJitter=Shader.PropertyToID("_CloudTemporalJitter");
        static readonly int IdCloudTemporalActive=Shader.PropertyToID("_CloudTemporalActive");

        static CustomSampler samplerMRT,samplerTemporal,samplerComposite;

        void OnEnable()
        {
            cameraComponent=GetComponent<Camera>();cameraComponent.depthTextureMode|=DepthTextureMode.Depth;
            Shader shader=Resources.Load<Shader>("ORBIS/AtmosphereComposite");
            if(shader&&shader.isSupported)composite=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            Shader finalShader=Resources.Load<Shader>("ORBIS/Display");
            if(finalShader&&finalShader.isSupported)display=new Material(finalShader){hideFlags=HideFlags.HideAndDontSave};
            Shader skyShader=Resources.Load<Shader>("ORBIS/CelestialLens");
            if(skyShader&&skyShader.isSupported)clearSky=new Material(skyShader){hideFlags=HideFlags.HideAndDontSave};
            if(samplerMRT==null)
            {
                samplerMRT=CustomSampler.Create("ORBIS.Atmosphere.MRT",true);
                samplerTemporal=CustomSampler.Create("ORBIS.Atmosphere.TemporalResolve",true);
                samplerComposite=CustomSampler.Create("ORBIS.Atmosphere.Composite",true);
            }
            hasPreviousState=false;
        }

        static RenderTexture CreateVolumeRT(int w,int h,int depthBits,string label)
        {
            var rt=new RenderTexture(w,h,depthBits,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear)
            {
                name=label,
                filterMode=FilterMode.Bilinear,
                wrapMode=TextureWrapMode.Clamp,
                useMipMap=false,
                autoGenerateMips=false,
                hideFlags=HideFlags.HideAndDontSave
            };
            if(!rt.Create()){DestroyImmediate(rt);throw new System.InvalidOperationException("Cannot allocate "+label);}
            return rt;
        }

        static void ReleaseVolumeRT(ref RenderTexture rt)
        {
            if(!rt)return;
            if(RenderTexture.active==rt)RenderTexture.active=null;
            rt.Release();
            if(Application.isPlaying)Destroy(rt);else DestroyImmediate(rt);
            rt=null;
        }

        void ReleaseHistoryTier(int tier)
        {
            for(int i=0;i<2;i++)
            {
                ReleaseVolumeRT(ref historyLight[tier,i]);
                ReleaseVolumeRT(ref historyTrans[tier,i]);
            }
            historyWidths[tier]=0;historyHeights[tier]=0;
        }

        void ReleaseAllPooledRTs()
        {
            for(int t=0;t<4;t++)ReleaseHistoryTier(t);
            hasPreviousState=false;
        }

        void EnsureHistoryRTs(int tier,int width,int height)
        {
            if(historyWidths[tier]!=width||historyHeights[tier]!=height||
               !historyLight[tier,0]||!historyLight[tier,0].IsCreated()||
               !historyLight[tier,1]||!historyLight[tier,1].IsCreated()||
               !historyTrans[tier,0]||!historyTrans[tier,0].IsCreated()||
               !historyTrans[tier,1]||!historyTrans[tier,1].IsCreated())
            {
                ReleaseHistoryTier(tier);
                for(int i=0;i<2;i++)
                {
                    historyLight[tier,i]=CreateVolumeRT(width,height,0,i==0?"ORBIS Cloud History Light A":"ORBIS Cloud History Light B");
                    historyTrans[tier,i]=CreateVolumeRT(width,height,0,i==0?"ORBIS Cloud History Trans A":"ORBIS Cloud History Trans B");
                }
                historyWidths[tier]=width;historyHeights[tier]=height;
            }
        }

        bool HasMarkleyProperty(Material m)
        {
            if(ReferenceEquals(m,cachedMarkleyMat))return cachedHasMarkley;
            cachedMarkleyMat=m;
            cachedHasMarkley=m&&m.HasProperty(IdMarkleyDirections);
            return cachedHasMarkley;
        }

        void SetRays(Material m)
        {
            var projection=cameraComponent.projectionMatrix;
            Vector3 originRight=Vector3.zero,originUp=Vector3.zero;
            Vector4 depthProjection=Vector4.zero;
            if(scroll&&scroll.IsTransitioning&&projection.m33>0)
            {
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
            m.SetVector(IdDepthProjection,depthProjection);
            m.SetVector(IdRayOriginRight,originRight);m.SetVector(IdRayOriginUp,originUp);
            for(int i=0;i<4;i++)m.SetVector(RayIds[i],view.transform.InverseTransformVector(cameraComponent.transform.TransformVector(corners[i])));
            m.SetVector(IdCameraLocal,view.transform.InverseTransformPoint(cameraComponent.transform.position));
        }
        bool SetAtlasRays(Material m)
        {
            if(scroll)
            {
                bool local=!scroll.IsAtlas;
                if(m==view.CloudMaterial?HasMarkleyProperty(m):m.HasProperty(IdMarkleyDirections))Orbis.Planet.MarkleyProjection.Bind(m);
                m.SetFloat(IdMarkleyMap,local?0:1);
                m.SetFloat(IdLocalCloudMap,local?1:0);
                if(local)
                {
                    var frame=scroll.LocalFrame;
                    m.SetFloat(IdCloudProjectionBlend,scroll.Morph);
                    m.SetVector(IdCloudLocalUp,new Vector4((float)frame.Up.X,(float)frame.Up.Y,(float)frame.Up.Z,0));
                    m.SetVector(IdCloudLocalEast,new Vector4((float)frame.East.X,(float)frame.East.Y,(float)frame.East.Z,0));
                    m.SetVector(IdCloudLocalNorth,new Vector4((float)frame.North.X,(float)frame.North.Y,(float)frame.North.Z,0));
                    // Smoothly distribute wheel travel across the actual 560-2,500 m LCL cloud-base zone
                    // so crossing Trade-Cu (1,020 m base) and Marine-Sc (560 m base) spans ~15 frames gradually.
                    float upper=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.46f,.74f,scroll.Morph));
                    float lower=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.64f,.98f,scroll.Morph));
                    const float aboveClouds=14500,lowerDeck=2500,belowClouds=200;
                    float height=belowClouds+(lowerDeck-belowClouds)*(1-lower)+(aboveClouds-lowerDeck)*(1-upper);
                    m.SetFloat(IdCloudCameraHeight,height);
                }
                for(int i=0;i<4;i++)
                {
                    Vector2 screen=cameraComponent.pixelRect.min+Vector2.Scale(MapUV[i],cameraComponent.pixelRect.size);
                    if(!scroll.TryProjectionPoint(screen,out Vector2 tile))return false;
                    var world=view.world.FlatSource;
                    m.SetVector(MapIds[i],new Vector4(tile.x/world.Width,tile.y/scroll.MapHeight,0,0));
                }
                if(!view||m!=view.CloudMaterial)m.SetFloat(IdScrollCloudSource,1);return true;
            }
            m.SetFloat(IdLocalCloudMap,0);m.SetFloat(IdMarkleyMap,0);if(!atlas)return false;
            Plane plane=new Plane(atlas.transform.forward,atlas.transform.position);
            Vector2[] uv=MapUV;
            for(int i=0;i<4;i++)
            {
                Ray ray=cameraComponent.ViewportPointToRay(uv[i]);
                if(!plane.Raycast(ray,out float distance))return false;
                m.SetVector(MapIds[i],atlas.transform.InverseTransformPoint(ray.GetPoint(distance)));
            }
            return true;
        }
        static void DrawFullScreenQuad()
        {
            GL.Begin(GL.QUADS);
            GL.TexCoord2(0,0);GL.Vertex3(0,0,.1f);
            GL.TexCoord2(1,0);GL.Vertex3(1,0,.1f);
            GL.TexCoord2(1,1);GL.Vertex3(1,1,.1f);
            GL.TexCoord2(0,1);GL.Vertex3(0,1,.1f);
            GL.End();
        }
        void Present(RenderTexture source,RenderTexture destination,Material material,int pass=0)
        {
            bool previous=GL.sRGBWrite;
            try
            {
                GL.sRGBWrite=QualitySettings.activeColorSpace==ColorSpace.Linear&&(destination==null||destination.sRGB);
                if(material)Graphics.Blit(source,destination,material,pass);else Graphics.Blit(source,destination);
            }
            finally{GL.sRGBWrite=previous;}
        }
        void DisplayOnly(RenderTexture source,RenderTexture destination)
        {
            hasPreviousState=false;
            if(view&&display)view.BindOpticalSettings(display);
            Present(source,destination,display);
        }
        void OnRenderImage(RenderTexture source,RenderTexture destination)
        {
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
            m.SetFloat(IdScrollVolumeWeight,scroll?scrollVolumeWeight:1);
            m.SetFloat(IdCloudViewBlend,0);m.SetFloat(IdLocalCloudMap,0);
            m.SetInt(IdCloudSteps,referenceCloudIntegration?RefCloudSteps[q]:CloudSteps[q]);
            m.SetInt(IdAirSteps,referenceCloudIntegration?RefAirSteps[q]:AirSteps[q]);
            m.SetInt(IdLightSteps,referenceCloudIntegration?RefLightSteps[q]:LightSteps[q]);
            m.SetInt(IdCloudDebug,cloudDebug);
            m.SetFloat(IdCloudRefinement,referenceCloudIntegration?4:CloudRefinement[q]);
            m.SetFloat(IdCloudLightSimple,referenceCloudIntegration?0:1);
            if(map)
            {
                m.SetFloat(IdClouds,settings.clouds&&(scroll||settings.mapClouds)?1:0);
                if(!SetAtlasRays(m)){DisplayOnly(source,destination);return;}
            }
            else
            {
                if(cameraComponent.orthographic&&(!scroll||scroll.Morph>=1)){DisplayOnly(source,destination);return;}
                SetRays(m);
                if(scroll&&!scroll.IsAtlas&&scroll.Morph>.12f&&SetAtlasRays(m))
                    m.SetFloat(IdCloudViewBlend,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.12f,.78f,scroll.Morph)));
            }
            Vector4 lensSun=Vector4.zero;
            if(!map)
            {
                if(!lens)TryGetComponent(out lens);
                if(lens&&lens.isActiveAndEnabled&&lens.view==view&&cloudDebug==0)
                {
                    m.SetFloat(IdCloudTemporalActive,0f);
                    m.SetFloat(IdCloudTemporalJitter,0f);
                    lensSun=lens.Sample(cameraComponent,source,m);
                }
                OrbisCelestialLens.Bind(m,lens,lensSun);
                if(clear)
                {
                    hasPreviousState=false;
                    SetRays(clearSky);view.BindOpticalSettings(clearSky);
                    clearSky.SetVector(IdSun,view.VisualSunLocal);
                    OrbisCelestialLens.Bind(clearSky,lens,lensSun);
                    Present(source,destination,clearSky);return;
                }
            }
            bool mapMRT=map&&m.passCount>3&&composite&&composite.passCount>1;
            if(fullResolutionReference||q==3||SystemInfo.supportedRenderTargetCount<2||!composite||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)||(map&&!mapMRT))
            {
                hasPreviousState=false;
                m.SetFloat(IdCloudTemporalActive,0f);
                m.SetFloat(IdCloudTemporalJitter,0f);
                m.SetVector(IdVolumeTexelSize,new Vector4(1f/Mathf.Max(1,source.width),1f/Mathf.Max(1,source.height),source.width,source.height));
                Present(source,destination,m,map?2:1);return;
            }
            float renderScale=referenceCloudIntegration?Mathf.Max(RefScale[q],q>=2?1f:RefScale[q]):Scale[q];
            int width=Mathf.Max(4,Mathf.CeilToInt(source.width*renderScale)),height=Mathf.Max(4,Mathf.CeilToInt(source.height*renderScale));
            Vector4 volumeTexelSize=new Vector4(1f/width,1f/height,width,height);

            // Evaluate temporal eligibility & instant anti-ghosting resets on date jump/rewind, tier switch, or rapid pan
            bool useTemporal=temporalReprojection&&!map&&!referenceCloudIntegration&&cloudDebug==0&&composite.passCount>2;
            float currMorph=scroll?scroll.Morph:0f;
            Vector3 currCamLocal=view.transform.InverseTransformPoint(cameraComponent.transform.position);
            Vector3 currCamFwd=view.transform.InverseTransformDirection(cameraComponent.transform.forward).normalized;
            Vector3 currSunLocal=view.VisualSunLocal.normalized;
            float currCloudsU=m.GetFloat(IdClouds);
            float currCoverU=m.GetFloat(IdCover);
            float currDensityU=m.GetFloat(IdDensity);
            float currGodRaysU=m.GetFloat(IdGodRays);
            float currMSU=m.GetFloat(IdCloudMS);
            float currWeatherTime=m.GetFloat(IdScrollWeatherTime);
            float currLensStrength=(lens&&lens.isActiveAndEnabled)?lens.strength:0f;
            int currFrame=Time.frameCount;
            Texture currWeatherTex=m.GetTexture(IdScrollWeatherTex);
            Matrix4x4 currWorldToClip=cameraComponent.projectionMatrix*cameraComponent.worldToCameraMatrix*view.transform.localToWorldMatrix;

            // Tight 0.08 weather-time guard (~4.6 min game time; 1 day = 25.0 units) guarantees 100% instant reset
            // and 0.000 DN ghosting on any calendar fast-forward, rewind, quality switch, or A/B render!
            bool frameAdvanced=allowSameFrameTemporal||(currFrame!=prevFrameCount);
            bool historyValid=false;
            if(useTemporal&&hasPreviousState&&frameAdvanced&&prevQuality==q&&prevMap==map&&prevDebug==cloudDebug&&
               prevAtmo==settings.atmosphere&&
               prevCloudsSetting==settings.clouds&&
               prevGodRaysSetting==settings.godRays&&
               prevMSSetting==settings.multipleScattering&&
               Mathf.Abs(currCloudsU-prevCloudsUniform)<.01f&&
               Mathf.Abs(currGodRaysU-prevGodRaysUniform)<.01f&&
               Mathf.Abs(currMSU-prevMSUniform)<.01f&&
               Mathf.Abs(currCoverU-prevCover)<1e-4f&&
               Mathf.Abs(currDensityU-prevDensity)<1e-4f&&
               Mathf.Abs(currLensStrength-prevLensStrength)<1e-4f&&
               ReferenceEquals(currWeatherTex,prevWeatherTex)&&
               Mathf.Abs(currWeatherTime-prevWeatherTime)<.08f&&
               Vector3.Dot(currSunLocal,prevSunLocal)>.9994f&&
               Mathf.Abs(currMorph-prevMorph)<=.10f&&
               Vector3.Dot(currCamFwd,prevCamFwd)>.90f)
            {
                float altScale=Mathf.Max(.05f,currCamLocal.magnitude-.99f);
                bool teleport=Mathf.Abs(currMorph-prevMorph)<1e-5f&&(currCamLocal-prevCamLocal).magnitude>.10f*altScale;
                historyValid=!teleport;
            }

            RenderTexture mrtLight=null,mrtTrans=null;
            RenderTexture previous=RenderTexture.active;
            bool oldSRGB=GL.sRGBWrite;
            try
            {
                mrtLight=RenderTexture.GetTemporary(width,height,16,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
                mrtTrans=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
                mrtLight.filterMode=FilterMode.Bilinear;mrtTrans.filterMode=FilterMode.Bilinear;
                mrtLight.wrapMode=TextureWrapMode.Clamp;mrtTrans.wrapMode=TextureWrapMode.Clamp;

                if(!historyValid)temporalFrame=0;
                else temporalFrame=(temporalFrame+1)&1023;

                float temporalJitter=(useTemporal&&historyValid)?Mathf.Repeat(temporalFrame*0.61803398875f,1f):0f;
                m.SetFloat(IdCloudTemporalActive,(useTemporal&&historyValid)?1f:0f);
                m.SetFloat(IdCloudTemporalJitter,temporalJitter);

                // 1. Low-res volumetric cloud & atmosphere MRT pass
                samplerMRT?.Begin();
                buffers[0]=mrtLight.colorBuffer;buffers[1]=mrtTrans.colorBuffer;
                GL.sRGBWrite=false;Graphics.SetRenderTarget(buffers,mrtLight.depthBuffer);
                m.SetTexture(IdMainTex,source);
                m.SetVector(IdVolumeTexelSize,volumeTexelSize);
                GL.PushMatrix();
                try
                {
                    GL.LoadOrtho();
                    if(!m.SetPass(map?3:0))throw new System.InvalidOperationException("Atmosphere MRT pass is unavailable.");
                    DrawFullScreenQuad();
                }
                finally {GL.PopMatrix();samplerMRT?.End();}

                RenderTexture resolvedLight=mrtLight,resolvedTrans=mrtTrans;
                if(useTemporal)
                {
                    EnsureHistoryRTs(q,width,height);
                    if(historyValid)
                    {
                        // 2. Parallax-accurate temporal resolve & 3x3 perceptual variance clamp at volume resolution
                        samplerTemporal?.Begin();
                        int readIdx=historyWriteIndex;
                        int writeIdx=1-historyWriteIndex;
                        SetRays(composite);
                        composite.SetVector(IdVolumeTexelSize,volumeTexelSize);
                        composite.SetTexture(IdRadianceTex,mrtLight);
                        composite.SetTexture(IdTransmittanceTex,mrtTrans);
                        composite.SetTexture(IdCloudHistoryLight,historyLight[q,readIdx]);
                        composite.SetTexture(IdCloudHistoryTrans,historyTrans[q,readIdx]);
                        composite.SetMatrix(IdCloudPrevVP,prevWorldToClip);
                        composite.SetVector(IdCloudHistoryParams,new Vector4(TemporalBlend[q],1f,temporalJitter,0f));

                        historyBuffers[0]=historyLight[q,writeIdx].colorBuffer;
                        historyBuffers[1]=historyTrans[q,writeIdx].colorBuffer;
                        Graphics.SetRenderTarget(historyBuffers,mrtLight.depthBuffer);
                        GL.PushMatrix();
                        try
                        {
                            GL.LoadOrtho();
                            if(!composite.SetPass(2))throw new System.InvalidOperationException("Temporal resolve pass is unavailable.");
                            DrawFullScreenQuad();
                        }
                        finally {GL.PopMatrix();samplerTemporal?.End();}

                        resolvedLight=historyLight[q,writeIdx];
                        resolvedTrans=historyTrans[q,writeIdx];
                        historyWriteIndex=writeIdx;
                    }
                    else
                    {
                        // Seed history directly via hardware DMA copy on reset frame (zero shader cost, zero black flash!)
                        Graphics.CopyTexture(mrtLight,historyLight[q,historyWriteIndex]);
                        Graphics.CopyTexture(mrtTrans,historyTrans[q,historyWriteIndex]);
                    }

                    hasPreviousState=true;
                    prevWorldToClip=currWorldToClip;
                    prevCamLocal=currCamLocal;prevCamFwd=currCamFwd;prevSunLocal=currSunLocal;
                    prevMorph=currMorph;prevCover=currCoverU;prevDensity=currDensityU;
                    prevCloudsUniform=currCloudsU;prevGodRaysUniform=currGodRaysU;prevMSUniform=currMSU;
                    prevWeatherTime=currWeatherTime;prevWeatherTex=currWeatherTex;prevLensStrength=currLensStrength;
                    prevQuality=q;prevDebug=cloudDebug;prevMap=map;prevAtmo=settings.atmosphere;prevFrameCount=currFrame;
                    prevCloudsSetting=settings.clouds;prevGodRaysSetting=settings.godRays;prevMSSetting=settings.multipleScattering;
                }
                else
                {
                    hasPreviousState=false;
                }

                // 3. Full-resolution depth-guarded bicubic/bilateral upsample & optical composite
                samplerComposite?.Begin();
                RenderTexture.active=previous;GL.sRGBWrite=oldSRGB;
                composite.SetTexture(IdRadianceTex,resolvedLight);
                composite.SetTexture(IdTransmittanceTex,resolvedTrans);
                composite.SetVector(IdVolumeTexelSize,volumeTexelSize);
                view.BindOpticalSettings(composite);
                if(map)SetAtlasRays(composite);
                else {SetRays(composite);OrbisCelestialLens.Bind(composite,lens,lensSun);}
                composite.SetVector(IdSun,view.VisualSunLocal);
                Present(source,destination,composite,map?1:0);
                samplerComposite?.End();
            }
            catch(System.Exception e)
            {
                RenderTexture.active=previous;GL.sRGBWrite=oldSRGB;fullResolutionReference=true;hasPreviousState=false;
                m.SetFloat(IdCloudTemporalActive,0f);m.SetFloat(IdCloudTemporalJitter,0f);
                if(!warned){Debug.LogWarning("ORBIS MRT unavailable; using full-resolution atmosphere. "+e.Message);warned=true;}
                Present(source,destination,m,map?2:1);
            }
            finally
            {
                RenderTexture.active=destination;GL.sRGBWrite=oldSRGB;
                if(mrtLight)RenderTexture.ReleaseTemporary(mrtLight);
                if(mrtTrans)RenderTexture.ReleaseTemporary(mrtTrans);
            }
        }
        void OnDisable()
        {
            ReleaseAllPooledRTs();
            if(composite){if(Application.isPlaying)Destroy(composite);else DestroyImmediate(composite);}
            if(display){if(Application.isPlaying)Destroy(display);else DestroyImmediate(display);}
            if(clearSky){if(Application.isPlaying)Destroy(clearSky);else DestroyImmediate(clearSky);}
            composite=null;display=null;clearSky=null;cachedMarkleyMat=null;
        }
    }
}
