using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orbis
{
    public static class OrbisSaveMigration
    {
        static bool Has(string json,string field)=>json.IndexOf("\""+field+"\"",StringComparison.Ordinal)>=0;
        public static WorldSave Read(string json)
        {
            if(string.IsNullOrWhiteSpace(json))throw new ArgumentException("Empty save.");
            WorldSave s=JsonUtility.FromJson<WorldSave>(json);
            if(s==null||s.settings==null)throw new ArgumentException("Missing save settings.");
            if(s.version<3)
            {
                bool cloudWater=Has(json,"weatherTime")&&Has(json,"cloudWater");
                s.producer=cloudWater?"LEGACY_V2_CLOUDWATER":"LEGACY_INCOMING";
                s.weatherInitialized=cloudWater;
                if(!cloudWater){s.weatherTime=0;s.weatherRemainder=0;s.visualTime=Math.Max(0,s.weatherClock);}
                var p=s.settings;
                if(!Has(json,"dynamicWeather"))p.dynamicWeather=true;
                if(!Has(json,"multipleScattering"))p.multipleScattering=true;
                if(!Has(json,"enhancedWater"))p.enhancedWater=true;
                if(!Has(json,"scatteringStrength"))p.scatteringStrength=.65f;
                if(!Has(json,"waveStrength"))p.waveStrength=.65f;
                if(!Has(json,"atmosphere"))p.atmosphere=true;
                if(!Has(json,"autoExpeditions"))p.autoExpeditions=true;
                if(!Has(json,"showBorders"))p.showBorders=true;
                if(!Has(json,"showNames"))p.showNames=true;
                if(!Has(json,"cloudShadows"))p.cloudShadows=true;
                if(!Has(json,"cloudCover"))p.cloudCover=1;
                if(!Has(json,"cloudDensity"))p.cloudDensity=1;
                if(!Has(json,"waterRoughness"))p.waterRoughness=1;
                if(!Has(json,"surfaceDetail"))p.surfaceDetail=1;
                p.airDensity=1;p.haze=1;p.surfaceLOD=true;p.localGrid=false;p.transportView=0;
                if(s.expeditions==null)s.expeditions=new List<Expedition>();
                if(s.view!=null){s.view.altitude=Mathf.Clamp(s.view.distance-1,.004f,5.5f);s.view.renderClock=s.visualTime;s.view.simulationRemainder=0;}
            }
            if(s.version<4){s.settings.selectionMode=2;s.settings.regionExpansion=false;s.settings.spriteActors=true;s.territoryClock=0;}
            // Do not rely on JsonUtility field initializers for pre-TerrainPerf1 saves.
            if(!Has(json,"terrainQuality"))s.settings.terrainQuality=1;
            if(!Has(json,"terrainBrushRadius"))s.settings.terrainBrushRadius=.12f;
            if(!Has(json,"terrainBrushStrength"))s.settings.terrainBrushStrength=.12f;
            if(!Has(json,"terrainFlattenHeight"))s.settings.terrainFlattenHeight=0;
            // Explicit defaults: old JSON has no V4.2 presentation switches.
            if(!Has(json,"rainEffects"))s.settings.rainEffects=true;
            if(!Has(json,"lightningEffects"))s.settings.lightningEffects=true;
            if(!Has(json,"weatherAudio"))s.settings.weatherAudio=true;
            if(!Has(json,"wetSurfaces"))s.settings.wetSurfaces=true;
            if(!Has(json,"godRays"))s.settings.godRays=true;
            if(!Has(json,"mapClouds"))s.settings.mapClouds=true;
            if(!Has(json,"exposure"))s.settings.exposure=1;
            if(!Has(json,"shadowStrength"))s.settings.shadowStrength=.8f;
            if(!Has(json,"surfaceDebug"))s.settings.surfaceDebug=0;
            if(s.view!=null){if(!Has(json,"lookTilt"))s.view.lookTilt=0;if(!Has(json,"lookHeading"))s.view.lookHeading=0;}
            return s;
        }
        static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
        static bool Within(float x,float a,float b)=>Finite(x)&&x>=a&&x<=b;
        public static void ValidateV3(WorldSave s)
        {
            if(!Finite(s.weatherTime)||s.weatherTime<0||s.weatherTime>1e12||!Finite(s.visualTime)||s.visualTime<0||s.visualTime>1e12||
                !Finite(s.weatherRemainder)||s.weatherRemainder<0||s.weatherRemainder>.80001)
                throw new ArgumentException("Invalid weather clock.");
            var p=s.settings;
            if(!Within(p.airDensity,0,3)||!Within(p.haze,0,4)||!Within(p.scatteringStrength,0,2)||
                !Within(p.waveStrength,0,2)||p.transportView<0||p.transportView>3)
                throw new ArgumentException("Invalid V3 rendering settings.");
            if(!Within(p.exposure,.1f,4)||!Within(p.shadowStrength,0,1)||p.surfaceDebug<0||p.surfaceDebug>5)
                throw new ArgumentException("Invalid V4.1 optical settings.");
            if(s.weatherInitialized)foreach(var t in s.tiles)
                if(!Within(t.vapor,0,3)||!Within(t.cloudWater,0,2)||!Within(t.cloudRain,0,10)||!Within(t.convection,0,1))
                    throw new ArgumentException("Invalid weather reservoir.");
            if(s.view!=null&&(!Within(s.view.lookTilt,0,110)||!Within(s.view.lookHeading,-360,360)))throw new ArgumentException("Invalid atmosphere camera orientation.");
            if(s.version>=3&&s.view!=null&&(!Within(s.view.altitude,.002f,5.6f)||!Within(s.view.simulationRemainder,0,.2f)||!Finite(s.view.renderClock)||s.view.renderClock<0||s.view.renderClock>1e12))throw new ArgumentException("Invalid camera altitude.");
        }
    }
}
