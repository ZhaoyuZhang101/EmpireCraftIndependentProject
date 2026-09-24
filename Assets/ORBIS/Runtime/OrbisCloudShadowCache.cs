using System;
using UnityEngine;

namespace Orbis
{
    /// <summary>Sun-aligned Beer optical-depth atlas: eight depths per near cloud column.
    /// Original Built-in implementation inspired by HDRP's shared Beer-shadow architecture.
    /// No HDRP package dependency. The shader traces the exceptional far-side interval directly.</summary>
    public sealed class OrbisCloudShadowCache : IDisposable
    {
        Material material;
        RenderTexture atlas;
        double nextUpdate;
        int quality=-1;
        float lastPhase=-10,lastCover=-10,lastDensity=-10,lastWeather=-10;
        Vector3 lastSun;OrbisWorld lastWorld;
        bool warned,allocationFailed;
        static readonly int[] Sizes={96,144,192,256};
        static readonly int[] Steps={10,16,24,32};
        public OrbisCloudShadowCache()
        {
            Shader shader=Resources.Load<Shader>("ORBIS/CloudShadowCache");
            if(shader&&shader.isSupported)material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            Shader.SetGlobalFloat("_OrbisShadowReady",0);
        }
        public void Update(OrbisWorld world,OrbisWeatherField field,Vector3 sun)
        {
            var settings=world.settings;
            if(!settings.clouds||(!settings.cloudShadows&&!settings.godRays)||!material||allocationFailed||
               !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
            {Shader.SetGlobalFloat("_OrbisShadowReady",0);return;}
            int q=Mathf.Clamp(settings.cloudQuality,0,3);
            try
            {
            if(q!=quality||!atlas)
            {
                Release();int n=Sizes[q];
                atlas=new RenderTexture(n*8,n,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear)
                {name="ORBIS V4.1 cloud optical depth / 8 layers",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
                if(!atlas.Create())throw new InvalidOperationException("Optical-depth atlas could not be created.");quality=q;nextUpdate=0;lastPhase=-10;
            }
            }
            catch(Exception error)
            {
                Release();allocationFailed=true;
                Debug.LogWarning("ORBIS cloud shadow atlas disabled for this session; direct trace fallback. "+error.Message);return;
            }
            float phase=(float)((field.VisualClock/OrbisWeather.FlowPeriod)%1.0);
            bool changed=lastWorld!=world||lastPhase!=phase||lastCover!=settings.cloudCover||lastDensity!=settings.cloudDensity||
                         lastWeather!=field.SampleClock||(sun-lastSun).sqrMagnitude>1e-9f;
            if(!changed){Shader.SetGlobalFloat("_OrbisShadowReady",atlas&&atlas.IsCreated()?1:0);return;}
            double now=Time.realtimeSinceStartupAsDouble;
            if(now<nextUpdate){if(lastWorld!=world)Shader.SetGlobalFloat("_OrbisShadowReady",0);return;}
            Vector3 helper=Mathf.Abs(sun.y)<.9f?Vector3.up:Vector3.right;
            Vector3 right=Vector3.Cross(helper,sun).normalized,up=Vector3.Cross(sun,right).normalized;
            field.Bind(material);material.SetFloat("_FlowPhase",phase);material.SetFloat("_Cover",settings.cloudCover);
            material.SetFloat("_Density",settings.cloudDensity);material.SetFloat("_Clouds",1);material.SetInt("_ShadowSteps",Steps[q]);
            material.SetVector("_OrbisShadowRight",right);material.SetVector("_OrbisShadowUp",up);material.SetVector("_OrbisShadowSun",sun);
            bool oldSRGB=GL.sRGBWrite;RenderTexture previous=RenderTexture.active;
            try
            {
                GL.sRGBWrite=false;Graphics.Blit(Texture2D.blackTexture,atlas,material);
                Shader.SetGlobalTexture("_OrbisShadowAtlas",atlas);Shader.SetGlobalFloat("_OrbisShadowResolution",Sizes[q]);
                Shader.SetGlobalVector("_OrbisShadowRight",right);Shader.SetGlobalVector("_OrbisShadowUp",up);Shader.SetGlobalVector("_OrbisShadowSun",sun);
                Shader.SetGlobalFloat("_OrbisShadowReady",1);
                lastWorld=world;lastPhase=phase;lastCover=settings.cloudCover;lastDensity=settings.cloudDensity;lastWeather=field.SampleClock;lastSun=sun;
                nextUpdate=now+(q==0?.20:q==1?.12:.08);
            }
            catch(Exception error)
            {
                Shader.SetGlobalFloat("_OrbisShadowReady",0);
                if(!warned){Debug.LogWarning("ORBIS cloud shadow cache failed; tracing directly. "+error.Message);warned=true;}
                nextUpdate=now+1;
            }
            finally {GL.sRGBWrite=oldSRGB;RenderTexture.active=previous;}
        }
        void Release(){if(atlas){atlas.Release();UnityEngine.Object.Destroy(atlas);}atlas=null;Shader.SetGlobalFloat("_OrbisShadowReady",0);}
        public void Dispose(){Release();if(material)UnityEngine.Object.Destroy(material);material=null;}
    }
}
