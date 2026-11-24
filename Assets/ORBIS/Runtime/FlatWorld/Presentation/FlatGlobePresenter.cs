using System;
using UnityEngine;
namespace Orbis.Flat
{
    public sealed class FlatGlobePresenter : MonoBehaviour
    {
        public OrbisPlanetView View { get; private set; }
        public OrbisWorld Snapshot { get; private set; }
        public float Yaw,Pitch=18,Distance=3.1f,SunAngle;
        Camera viewCamera;double clock;FlatMapRenderer maps;
        float baseAir,baseHaze,baseCloudDensity;bool scrollPrepared;
        // Satellite-photo look on the globe: thin air and haze let the deep ocean read navy
        // and keep cloud against it crisp. The unfolded surface keeps its own atmosphere.
        const float GlobeAir=.08f,GlobeHaze=0f,GlobeExposure=1f;
        // Orbital cloud look (ScrollCloudSource globals): only thick cloud reads white, thin cover and
        // cirrus stay see-through and texel-size specks feather out; the deep ocean takes a navy tint.
        // Near values match FlatMapRenderer.
        float globeCoverCut=.42f,globeThinEdge=1f,globeHighCut=.82f,globeStreak=1f,globeSoftLod=2f,globeOceanTint=.5f;
        float globeOrbitVeil=1f,globeOrbitTau=.05f,globeVeilStart=.32f,globeOrbitHighTau=.15f,globeOrbitCore=1.4f,globeOrbitSmooth=0f,globeOrbitVolume=1f,globeOrbitVolumeStart=.78f;
        // Orbit mist (AtmosphereComposite globals): ground under cloud is blurred and greyed, bright cloud glows.
        float globeMist=1f,globeMistBlur=1.5f,globeMistGrey=.6f,globeHalo=.6f;
        const float NearCoverCut=.15f,NearHighCut=.7f;
        float baseExposure,nearAir,nearHaze,nearExposure;
        OrbisAtmosphereRenderer scrollVolume;FlatScrollNavigator scrollNav;Vector2 poles;int poleRevision=-1;
        int materialTerrainRevision=-1;
        public void Initialize(WorldData world,Camera target,FlatMapRenderer renderer,bool scrollPresentation=false)
        {
            viewCamera=target;maps=renderer;
            Snapshot=OrbisWorld.CreateFlatPresentation(world);
            View=gameObject.AddComponent<OrbisPlanetView>();View.Initialize(Snapshot,viewCamera,null,null,scrollPresentation);View.showRivers=false;
            View.SurfaceMaterial.SetFloat("_FlatWorldReady",1);View.SurfaceMaterial.SetTexture("_FlatTerrain",maps.TerrainTexture);View.SurfaceMaterial.SetTexture("_FlatDisplay",maps.DisplayTexture);View.SurfaceMaterial.SetTexture("_FlatOwners",maps.OwnerTexture);View.SurfaceMaterial.SetTexture("_FlatInfo",maps.InfoTexture);
            View.SurfaceMaterial.SetFloat("_FlatDiagnosticDisplay",maps.VisibleLayer>=MapLayer.SurfaceWinds?1:0);
            View.SurfaceMaterial.SetFloat("_RiverGlobeNarrowing",FlatRiverCurves.GlobeWidthReduction(world.Preset,0));
            View.SurfaceMaterial.SetTexture("_VisualTex",maps.VisualTexture);View.SurfaceMaterial.SetFloat("_FieldReady",1);
            maps.BindPolarTerrain(View.SurfaceMaterial);
            materialTerrainRevision=maps.PublishedTerrainRevision;
            View.SurfaceMaterial.SetTexture("_HydroA",maps.HydroTexture);View.SurfaceMaterial.SetTexture("_HydroB",maps.MixingTexture);View.SurfaceMaterial.SetTexture("_GeoTex",maps.GeometryTexture);
            View.SurfaceMaterial.SetVector("_MapSize",new Vector4(world.Width,world.Height,1f/world.Width,1f/world.Height));
            View.SurfaceMaterial.SetFloat("_ShowWater",1);View.SurfaceMaterial.SetFloat("_ShowBanks",1);View.SurfaceMaterial.SetFloat("_WaveAmount",1);View.SurfaceMaterial.SetFloat("_SWEReady",0);
            View.SurfaceMaterial.SetVector("_FlatSize",new Vector4(world.Width,world.Height,1f/world.Width,1f/world.Height));
            Orbis.Planet.OrbisAssetVisuals.BindWater(View.SurfaceMaterial);
            maps.SetVegetationDay(world.Day,View.SurfaceMaterial);
        }
        public void Tick(float dt,bool political,bool clouds,bool shadows,bool atmosphere,bool dayNight,Vector3 sun)
        {
            viewCamera.backgroundColor=Color.black;
            clock+=dt;View.SurfaceMaterial.SetFloat("_WaterClock",(float)clock);Snapshot.settings.clouds=clouds;Snapshot.settings.cloudShadows=shadows;Snapshot.settings.atmosphere=atmosphere;Snapshot.settings.night=dayNight;
            viewCamera.orthographic=false;viewCamera.allowHDR=true;viewCamera.allowMSAA=false;viewCamera.nearClipPlane=.002f;viewCamera.farClipPlane=100;viewCamera.fieldOfView=42;
            Quaternion q=Quaternion.Euler(Pitch,Yaw,0);viewCamera.transform.position=q*new Vector3(0,0,-Distance);viewCamera.transform.LookAt(Vector3.zero);
            Snapshot.day=(float)maps.World.Day; // Read-only calendar mirror, no second simulation.
            maps.SetVegetationDay(maps.World.Day,View.SurfaceMaterial);
            if(materialTerrainRevision!=maps.PublishedTerrainRevision)
            {
                maps.BindPolarTerrain(View.SurfaceMaterial);
                materialTerrainRevision=maps.PublishedTerrainRevision;
            }

            View.SurfaceMaterial.SetFloat("_FlatPolitical",political?1:0);
            View.SurfaceMaterial.SetFloat("_FlatDiagnosticDisplay",maps.VisibleLayer>=MapLayer.SurfaceWinds?1:0);
            View.SurfaceMaterial.SetFloat("_RiverMap",maps.VisibleLayer==MapLayer.Rivers?1:0);
            View.SyncVolumeSettings(sun,clock,Snapshot.day);View.Sync(clock,sun,0);
        }
        public void PrepareScroll()
        {
            if(scrollPrepared)return;scrollPrepared=true;
            nearAir=Snapshot.settings.airDensity*.10f;nearHaze=Snapshot.settings.haze;nearExposure=Snapshot.settings.exposure;
            baseAir=GlobeAir;baseHaze=GlobeHaze;baseExposure=GlobeExposure;baseCloudDensity=Snapshot.settings.cloudDensity;
            Snapshot.settings.surfaceLOD=false;Snapshot.settings.spriteActors=false;
            // Preserve the native atmosphere implementation, but never draw a second terrain world.
            foreach(var renderer in GetComponentsInChildren<Renderer>(true))renderer.forceRenderingOff=true;
            var actors=GetComponent<OrbisBillboardActors>();if(actors)actors.enabled=false;
            if(View.LocalSurface)View.LocalSurface.enabled=false;
        }
        public void TickScroll(float dt,double visualTime,double calendarDay,float spin,float unfold,
            bool clouds,bool atmosphere,bool night,bool shadows,Vector3 sun,bool natural)
        {
            PrepareScroll();clock=visualTime;Snapshot.day=(float)calendarDay;
            maps.SetVegetationDay(calendarDay,View.SurfaceMaterial);
            transform.localScale=new Vector3(1,1,-1);
            transform.localRotation=Quaternion.AngleAxis(spin*Mathf.Rad2Deg,Vector3.up);
            float weight=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.96f,1,unfold));
            float near=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.3f,.85f,unfold));
            Snapshot.settings.airDensity=Mathf.Lerp(baseAir,nearAir,near);
            Snapshot.settings.exposure=Mathf.Lerp(baseExposure,nearExposure,near);
            Snapshot.settings.haze=Mathf.Lerp(baseHaze,nearHaze,near);
            Shader.SetGlobalFloat("_CloudCoverCut",Mathf.Lerp(globeCoverCut,NearCoverCut,near));
            Shader.SetGlobalFloat("_CloudThinEdge",Mathf.Lerp(globeThinEdge,0,near));
            Shader.SetGlobalFloat("_CloudHighCut",Mathf.Lerp(globeHighCut,NearHighCut,near));
            Shader.SetGlobalFloat("_CloudStreak",Mathf.Lerp(globeStreak,0,near));
            Shader.SetGlobalFloat("_CloudSoftLod",near<.5f?globeSoftLod:0);
            Shader.SetGlobalFloat("_OceanOrbitTint",globeOceanTint*(1-near));
            Shader.SetGlobalFloat("_CloudOrbitVeil",globeOrbitVeil*(1-near));Shader.SetGlobalFloat("_CloudOrbitTau",globeOrbitTau);
            Shader.SetGlobalFloat("_CloudOrbitSmooth",globeOrbitSmooth);Shader.SetGlobalFloat("_CloudOrbitVolume",globeOrbitVolume);Shader.SetGlobalFloat("_CloudOrbitVolumeStart",globeOrbitVolumeStart);
            Shader.SetGlobalFloat("_OrbitMist",globeMist*(1-near));Shader.SetGlobalFloat("_OrbitMistBlur",globeMistBlur);
            Shader.SetGlobalFloat("_OrbitMistGrey",globeMistGrey);Shader.SetGlobalFloat("_OrbitHalo",globeHalo);
            Shader.SetGlobalFloat("_CloudOrbitCore",globeOrbitCore);Shader.SetGlobalFloat("_CloudVeilStart",globeVeilStart);Shader.SetGlobalFloat("_CloudOrbitHighTau",globeOrbitHighTau);
            Snapshot.settings.cloudDensity=baseCloudDensity;
            Snapshot.settings.clouds=clouds&&natural;
            Snapshot.settings.atmosphere=atmosphere&&natural;
            Snapshot.settings.night=night;
            // The old shadow atlas belongs to the other weather source.
            Snapshot.settings.cloudShadows=false;
            View.layer=natural?0:2;
            View.SyncVolumeSettings(sun,visualTime,calendarDay);
            // The HP radial column cache is also the spherical terrain's cloud
            // shadow source. The retired native shadow atlas must remain disabled.
            maps.Sky.Bind(View.SurfaceMaterial,sun,clouds,shadows,atmosphere,night,false,false,true,0);
            var m=View.CloudMaterial;
            bool weatherReady=maps.Sky.Ready;
            m.SetFloat("_ScrollCloudSource",weatherReady?1:0);
            m.SetFloat("_CloudShellTop",OrbisAtmosphereProfile.CloudTop);
            m.SetTexture("_ScrollWeatherTex",maps.Sky.Texture);
            m.SetTexture("_ScrollCloudWindTex",maps.Sky.DynamicWind?maps.Sky.DynamicWind:Texture2D.blackTexture);
            m.SetVectorArray("_OrbisLightning",maps.Sky.LightningFlashes);
            m.SetTexture("_ScrollHeightTex",maps.VisualTexture);
            m.SetVector("_ScrollSourceSize",new Vector4(maps.World.Width,maps.World.Height,0,0));
            if(poleRevision!=maps.World.TerrainRevision){poles=FlatGlobeGeometry.PolarHeights(maps.World);poleRevision=maps.World.TerrainRevision;}
            m.SetVector("_ScrollPoleHeights",poles);
            m.SetFloat("_ScrollHeightScale",FlatGlobeGeometry.HeightScale);
            m.SetFloat("_ScrollWeatherTime",(float)maps.Sky.RenderClock);
            m.SetTexture("_ScrollCloudColumn",weatherReady?(Texture)maps.Sky.ColumnTexture:Texture2D.blackTexture);
            m.SetFloat("_OrbisShadowReady",0);
            bool volumeLight=weatherReady&&Snapshot.settings.clouds&&Snapshot.settings.atmosphere&&Snapshot.settings.godRays;
            m.SetFloat("_GodRays",volumeLight?1:0);
            if(!scrollVolume)foreach(var volume in viewCamera.GetComponents<OrbisAtmosphereRenderer>())if(volume.view==View)scrollVolume=volume;
            if(!scrollNav)scrollNav=maps.GetComponentInParent<FlatWorldApp>()?.Navigation;
            if(scrollVolume){scrollVolume.scroll=scrollNav;scrollVolume.scrollVolumeWeight=weight;}
            // Intentionally NOT View.Sync(): it enables/culls the old meshes and starts local surface LOD.
            // The viewCamera image effect keeps its display transform at both endpoints; only volume density fades.
        }
        public void Orbit(Vector2 delta) { Yaw+=delta.x*.25f;Pitch=Mathf.Clamp(Pitch+delta.y*.2f,-84,84); }
        public void Zoom(float wheel) { Distance=Mathf.Clamp(Distance*Mathf.Exp(wheel*.035f),1.09f,6); }
    }
}
