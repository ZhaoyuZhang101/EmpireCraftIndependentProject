Shader "ORBIS/Surface"
{
 Properties
 {
  [HideInInspector] _VegetationSeasonTex("Climate vegetation seasons",2D)="white"{}
  [HideInInspector] _LakeIceTex("Lake temperature history",2D)="white"{}
  [HideInInspector] _LakeIceSeason("Lake cooling months, blend and availability",Vector)=(0,1,0,0)
  [HideInInspector] _VegetationSeasonReady("Vegetation seasons ready",Float)=0
  [HideInInspector] _VegetationSeason("Current month next month blend",Vector)=(0,1,0,0)
  [HideInInspector] _ClimateYearHeat("Regional temperature anomaly",Vector)=(0,0,0,0)
  [HideInInspector] _ClimateYearRain("Regional rainfall anomaly",Vector)=(0,0,0,0)
  [HideInInspector] _MapSize("World dimensions and inverse dimensions",Vector)=(1,1,1,1)
  [HideInInspector] _FieldReady("Terrain and bed fields ready",Float)=0
  [HideInInspector] _ShowWater("Render water",Float)=1
  [HideInInspector] _ShowBanks("Render banks",Float)=1
  [HideInInspector] _WaveAmount("Water wave strength",Float)=1
  [HideInInspector] _RiverAtlas("Continuous river field",2D)="black"{}
  [HideInInspector] _RiverOverview("River network overview",2D)="black"{}
  [HideInInspector] _RiverOverviewReady("River overview ready",Float)=0
  [HideInInspector] _RiverSurvey("Surveyed river courses",Float)=0
  [HideInInspector] _KoppenMap("Surveyed climate map",Float)=0
  [HideInInspector] _RiverMap("River network map",Float)=0
  [HideInInspector] _RiverPreviewReady("Initial river field ready",Float)=0
  [HideInInspector] _RiverSeasonTex("Seasonal river discharge",2D)="black"{}
  [HideInInspector] _RiverSeasonReady("River seasons ready",Float)=0
  [HideInInspector] _RiverSeason("Current and next discharge month",Vector)=(0,1,0,0)
  [HideInInspector] _RiverAtlasReady("River field ready",Float)=0
  [HideInInspector] _RiverAtlasLayout("River field layout",Vector)=(0,0,0,0)

  [HideInInspector] _Natural("Natural surface",Float)=1
  [HideInInspector] _VendorWaterReady("SW3 resources ready",Float)=0
  [HideInInspector] _VendorStreamNormal("SW3 stream waves",2D)="gray"{}
  [HideInInspector] _VendorOceanNormal("SW3 ocean waves",2D)="gray"{}
  [HideInInspector] _VendorLakeNormal("SW3 lake ripples",2D)="bump"{}
  [HideInInspector] _VendorLakeSmoothNormal("SW3 calm lake ripples",2D)="bump"{}
  [HideInInspector] _VendorOceanFoam("SW3 ocean foam",2D)="black"{}
  [HideInInspector] _VendorRiverFoam("SW3 river foam",2D)="black"{}
  [HideInInspector] _FlatWorldReady("2D authoritative source",Float)=0
  [HideInInspector] _FlatDiagnosticDisplay("Shared diagnostic layer",Float)=0
  [HideInInspector] _FlatPolitical("2D political tint",Float)=0
  _Tint("Tint",Color)=(1,1,1,1)
  _TerrainAtlas("Biome albedo",2D)="white"{}
  _FlatTerrain("Shared 2D terrain",2D)="white"{} _FlatDisplay("Shared 2D display",2D)="white"{} _FlatOwners("Shared owners",2D)="black"{} _FlatInfo("Shared semantics",2D)="black"{}
  [HideInInspector] _GlebaWindFlowTex("Gleba two-month surface wind flow",2D)="black"{}
  [HideInInspector] _GlebaCurrentFlowTex("Gleba two-month ocean current flow",2D)="black"{}
  [HideInInspector] _GlebaBandsTex("Gleba circulation front latitudes",2D)="black"{}
  [HideInInspector] _GlebaCirculation("Gleba circulation mode clock blend ready",Vector)=(0,0,0,0)
  _HydroA("Depth flow width",2D)="black"{} _HydroB("Meander sediment freshwater links",2D)="black"{} _GeoTex("Height materials",2D)="black"{}
  _VisualTex("Reconstructed field",2D)="black"{} _SkyTex("Climate cloud layers",2D)="black"{}
  [HideInInspector] _PoleBedDepth("Polar bed and depth, south then north",Vector)=(0,0,0,0)
  [HideInInspector] _EarthPolarBed("Surveyed polar bed",2D)="black"{}
  [HideInInspector] _EarthLandOverview("Filtered Earth elevations",2D)="black"{}
  [HideInInspector] _EarthLandDirectory("Native Earth land directory",2D)="black"{}
  [HideInInspector] _EarthLandPages("Native Earth land elevations",2D)="black"{}
  [HideInInspector] _EarthPolarCoast("Surveyed marine shoreline",2D)="black"{}
  [HideInInspector] _EarthSurveyMask("Unedited Earth terrain",2D)="white"{}
  [HideInInspector] _EarthSurveyAtPoles("Unedited south and north poles",Vector)=(1,1,0,0)
  [HideInInspector] _EarthPolarReady("Earth polar survey ready",Float)=0
  [HideInInspector] _EarthLakeShore("Surveyed inland shorelines",2D)="black"{}
  [HideInInspector] _EarthLakeDetail("Near lake shorelines",2D)="black"{}
  [HideInInspector] _EarthLakeReady("Inland shoreline survey ready",Float)=0
  [HideInInspector] _EarthOceanDirectory("Global marine coast directory",2D)="black"{}
  [HideInInspector] _EarthOceanPages("Global marine banks",2D)="black"{}
  [HideInInspector] _EarthStraitShore("Regional marine shorelines",2D)="black"{}
  [HideInInspector] _EarthStraitDetail("Surveyed strait banks",2D)="black"{}
  [HideInInspector] _EarthMarineDepth("Surveyed coastal depths",2D)="black"{}
  [HideInInspector] _EarthStraitReady("Strait shoreline survey ready",Float)=0
  [HideInInspector] _SkyReady("Sky cache ready",Float)=0
  [HideInInspector] _SkyClock("Sky time",Float)=0
  [HideInInspector] _FlatCloudShadows("Cloud shadows enabled",Float)=1
 }
 SubShader
 {
  Tags { "RenderType"="Opaque" "Queue"="Geometry" }
  Pass
  {
   Name "SURFACE_LIT_PERF32"
   Tags { "LightMode"="ForwardBase" }
   Cull Off ZWrite On
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma target 3.0
   #include "UnityCG.cginc"
   #include "OrbisPatch.cginc"
   // The camera pass renders the atmosphere. Survey, ice, river and circulation
   // textures on this surface reuse the same presentation sampler.
   #define ORBIS_FLAT_NO_SWE
   #pragma multi_compile_local_fragment __ ORBIS_RIVER_RASTER ORBIS_RIVER_VECTORS ORBIS_DIAGNOSTIC
   #define ORBIS_SURFACE_RIVER_VARIANTS 1
   #include "FlatWorld/Water/FlatWater.cginc"
   #include "FlatWorld/FlatLakeIce.cginc"
   #include "FlatWorld/FlatCloudCover.cginc"
   #ifdef ORBIS_DIAGNOSTIC
   #include "FlatWorld/FlatCirculationOverlay.cginc"
   #endif
   #include "FlatWorld/Water/FlatWaterReflection.cginc"
   UNITY_DECLARE_TEX2D_NOSAMPLER(_FlatDisplay);
   sampler2D _FlatTerrain,_FlatOwners,_FlatInfo,_TerrainAtlas;
   float _FlatWorldReady,_FlatDiagnosticDisplay,_FlatPolitical,_Natural,_Night,_SolarEnergy,_CloudShadow,_ShadowStrength;
   float _TerrainAtlasReady,_SurfaceDetail,_Settlement,_Clouds,_SurfaceDebug;
   float _SkyReady,_FlatCloudShadows,_FlatNightLights,_ShowContours,_ContourInterval;
   float3 _Sun;float4 _Tint,_FlatSize;
   float4 _OrbisNightParams,_OrbisMoon,_OrbisMoonColor,_OrbisNightSkyColor,_OrbisAirglowColor;
   struct appdata {float4 vertex:POSITION;float3 normal:NORMAL;float4 color:COLOR;float3 geo:TEXCOORD0;float4 climate:TEXCOORD1;float4 waterData:TEXCOORD2;float3 flow:TEXCOORD3;};
   struct v2f {float4 pos:SV_POSITION;float3 local:TEXCOORD0;float3 normal:TEXCOORD1;float3 view:TEXCOORD2;float4 color:COLOR;float4 climate:TEXCOORD3;float4 waterData:TEXCOORD4;};
   v2f vert(appdata v)
   {
    v2f o;o.pos=UnityObjectToClipPos(v.vertex);
    o.local=_Map>.5?v.geo:mul(_WorldToPlanet,mul(unity_ObjectToWorld,v.vertex)).xyz;
    o.normal=_Map>.5?normalize(v.normal):normalize(mul((float3x3)_WorldToPlanet,UnityObjectToWorldNormal(v.normal)));
    o.view=_Map>.5?normalize(v.geo):mul(_WorldToPlanet,float4(_WorldSpaceCameraPos,1)).xyz-o.local;
    o.color=v.color;o.climate=v.climate;o.waterData=v.waterData;return o;
   }
   float4 frag(v2f i):SV_Target
   {
    OrbisPatchClip(i.local,i.pos.xy);
#ifdef ORBIS_DIAGNOSTIC
    float2 uv=OrbisGeographicUV(normalize(i.local));
    float2 cell=float2(frac(uv.x),clamp(uv.y,0,1-_MapSize.w*.0001))*_MapSize.xy;
    float2 dx,dy;FlatCellScreenDerivatives(cell,dx,dy);
    float pixelsPerTile=1/max(1e-5,max(length(dx),length(dy)));
    float2 displayUV=lerp((floor(cell)+.5)*_MapSize.zw,FlatBlendUV(cell),step(.5,_GlebaCirculation.x));
    float4 display=FlatPresentationSample(_FlatDisplay,displayUV,0);
    float areaWater;
    float3 col=FlatDiagnosticSurface(display,uv,cell,_FlatInfo,pixelsPerTile,areaWater);
    #ifdef UNITY_COLORSPACE_GAMMA
    col=GammaToLinearSpace(max(0,col));
    #endif
    return float4(max(0,col),1);
#else
    float3 radial=normalize(i.local),normal=normalize(i.normal),sun=normalize(_Sun+float3(0,1e-7,0)),view=normalize(i.view);
    if(dot(normal,radial)<0)normal=-normal;
    float2 uv=OrbisGeographicUV(radial);
    float3 east=normalize(float3(radial.z,0,-radial.x)+float3(1e-6,0,0)),north=normalize(cross(radial,east));
    float3 localLight=float3(dot(sun,east),dot(sun,north),dot(sun,radial));
    float3 localView=normalize(float3(dot(view,east),dot(view,north),max(.02,dot(view,radial))));
    float pixelsPerTile=1/max(1e-5,max(length(ddx(uv*_MapSize.xy)),length(ddy(uv*_MapSize.xy))));
    float night=1-smoothstep(-.12,.16,dot(radial,sun));
    float3 sky=_OrbisNightSkyColor.rgb*_OrbisNightParams.y+_OrbisAirglowColor.rgb*_OrbisNightParams.z;
    float moon=max(0,dot(normal,_OrbisMoon.xyz))*_OrbisMoon.w;
    float3 illumination=lerp(float3(.055,.073,.10),sky,_OrbisNightParams.x)+moon*_OrbisMoonColor.rgb;
    float cloudShade=1;
    if(_FlatWorldReady>.5&&_SkyReady>.5)
       cloudShade=1-FlatCloudShadowOpacity(uv,sun,_FlatCloudShadows)*.52;
    illumination+=max(0,dot(normal,sun))*float3(1.04,1.02,.96)*max(.1,_SolarEnergy)*1.35;
    illumination=lerp(float3(.95,.97,1),illumination,_Night);
    float3 col=FlatColor(i.color.rgb)*_Tint.rgb;
    float water=smoothstep(.45,.9,i.color.a);
    float2 terrainGradient=0;
    float landBed=0;
    float4 terrainRelief=float4(1,1,1,0);
    float waterKind=0,lakeReceiver=0,lakeIce=0,lakePixelKm=40030.2*FlatEarthGroundFootprint(uv);
    float riverOverviewInk=0;
    float3 riverMapColor=0;
    FlatRiverSection channel=(FlatRiverSection)0;channel.edge=8;
    FlatCoastalSediment coast=(FlatCoastalSediment)0;coast.shore=-8;
    float4 season=float4(1,1,0,.5);
    float4 field=float4(0,-8,max(.2,i.waterData.x),0),hydro=float4(max(.2,i.waterData.x),0,0,0),mixing=0,geo=0,own=0,info=0;
    if(_FlatWorldReady>.5)
    {
     float2 cell=uv*_MapSize.xy,tileUV=(floor(cell)+.5)*_MapSize.zw;
     field=FlatField(cell);hydro=tex2Dlod(_HydroA,float4(tileUV,0,0));mixing=tex2Dlod(_HydroB,float4(tileUV,0,0));geo=tex2Dlod(_GeoTex,float4(tileUV,0,0));
     own=tex2D(_FlatOwners,tileUV);info=tex2D(_FlatInfo,tileUV)*255;
     float gridBed=field.r;
     terrainGradient=FlatFieldGradient(cell,0);
     float2 surveyCoast;
     float surveyWeight=FlatEarthRelief(uv,field.r,terrainGradient,surveyCoast);
     float surveyStrait=FlatSurveyBasinShore(uv,cell,_FlatInfo,info.g,field.g,surveyCoast);
     float surveyLake=FlatEarthLake(uv,info.g,surveyStrait,field.g);
     col=FlatEcotoneAlbedo(_FlatTerrain,cell,field.g,_SmoothTerrain);
     season=FlatSurfaceSeason(cell,radial);
     float aa=FlatEarthShoreAA(uv,clamp(.65/max(1,pixelsPerTile),.008,.28),surveyStrait),edgeDistance;
     float2 riverBank;
     float4 river=FlatRiverReceiver(cell,tileUV,hydro,mixing,field,riverBank);
     float closeRiver,riverInk=FlatRiverMapInk(cell,river,closeRiver);
     if(_RiverMap>.5)riverMapColor=FlatRiverMapColor(cell,_FlatInfo,info.g,field,aa,riverInk,surveyLake);
     river=FlatRiverDisplaySample(river,pixelsPerTile);
     channel=FlatRiverSectionAt(cell,river,field,riverBank);
     water=FlatDisplayedWaterMask(channel.edge,field.g,aa,pixelsPerTile,edgeDistance);
     landBed=field.r;
     float landSurvey=FlatEarthLandRelief(uv,1-water,landBed,terrainGradient);
     float4 substrate=FlatNaturalGround(_GeoTex,cell,geo,pixelsPerTile,water);
     float surfaceWetness=lerp(substrate.a,season.a,saturate(_VegetationSeasonReady));
     col=FlatEcotoneColor(col,cell,field.a,substrate.a);
     col=lerp(col,FlatSeasonGround(col,field.a,season),1-water);
     float wetSupport;
     float4 basin=FlatAreaWaterOptics(cell,_FlatInfo,float4(field.b,mixing.g,mixing.b,floor(info.g+.5)),wetSupport);
     if(surveyWeight>0&&wetSupport>0)basin.r=max(.15,basin.r+(gridBed-field.r));
     float surveyDepth=FlatEarthMarineOptics(uv,surveyCoast,field.r,surveyStrait,basin,wetSupport);
     lakeReceiver=step(.5,_Natural)*FlatLakeIceReceiver(basin.a,wetSupport,surveyLake,field.g,aa);
     FlatEarthLakeOptics(surveyLake,field.g,basin,wetSupport);
     [branch] if(lakeReceiver>.0001)lakeIce=lakeReceiver*FlatLakeIceCover(uv,radial,basin.r,field.g,lakePixelKm);
     float marineSupport=wetSupport*saturate(4-basin.a);
     riverOverviewInk=riverInk*(1-closeRiver)*max(1-smoothstep(-.04,.08,field.g),channel.influence*marineSupport);
     float beachScale=FlatEarthBeachScale(surveyStrait*(1-surveyLake));
     coast=FlatCoastAt(cell,field,basin,wetSupport,edgeDistance,0,_WaterClock,beachScale);
     coast.surveyDepthWeight=surveyDepth*(1-surveyLake);
     float sediment=FlatReachSediment(cell,channel,field.g,mixing.g);
     waterKind=basin.a;field.b=basin.r;mixing.gb=basin.gb;
     mixing.g=lerp(mixing.g,sediment,channel.influence);
     col=FlatVegetation(col,cell,field.a,pixelsPerTile);
     float4 localMoon=float4(FlatGeographicLight(uv,_OrbisMoon.xyz),_OrbisMoon.w*_OrbisNightParams.x);
     float4 terrainLight=FlatTerrainLight(localLight,localMoon,_Night);
     float4 landField=field;landField.r=landBed;
     col=FlatTerrainFine(col,cell,pixelsPerTile*(1-step(.999,water)),terrainGradient,landField,substrate.xy,surfaceWetness,terrainLight);
     if(water<.999)
      terrainRelief=FlatTerrainRelief(cell,gridBed,terrainGradient,terrainLight,localView,FlatWetMineral(substrate.z,surfaceWetness,field.a),max(surveyWeight,landSurvey));
     col=lerp(col*terrainRelief.rgb,FlatColor(float3(.84,.87,.90)),terrainRelief.a);
     col=FlatWaterBanks(cell,col,terrainRelief.rgb,channel,sediment,field.g,(1-water)*_ShowBanks,aa,1/max(1,pixelsPerTile),beachScale);
     col=lerp(col,coast.albedo*terrainRelief.rgb,coast.coverage*_ShowBanks);
    }
    else if(_Natural>.5&&_TerrainAtlasReady>.5)
    {
     // Lightweight legacy scene compatibility; outer atmospheric camera effects remain.
     float2 p=frac(uv*float2(96,48));float2 atlasUV=p*.488+.006;
     float2 quadrant=i.climate.y>.5?float2(.5,0):i.climate.z>.5?float2(0,0):i.climate.x<.25?float2(.5,.5):float2(0,.5);
     col=lerp(col,col*(.75+tex2D(_TerrainAtlas,atlasUV+quadrant).rgb*.5),saturate(_SurfaceDetail*.5)*(1-water));
    }
    if(_SurfaceDebug==1)return float4(col,1);
    if(_SurfaceDebug==2)return float4(saturate(dot(normal,sun)).xxx,1);
    if(_SurfaceDebug==4)return float4(cloudShade.xxx,1);
    col*=illumination;
    float polarIce=FlatPolarIceMask(uv,water,0)*step(.5,_Natural)*(1-lakeReceiver);
    float waterIce=max(polarIce,lakeIce);
    float landSnow=0;
    if(_FlatWorldReady>.5&&_Natural>.5)
      landSnow=FlatTerrainSnow(max(FlatLandSnow(uv*_MapSize.xy,_FlatInfo),season.b)*(1-water),
          uv*_MapSize.xy,terrainGradient,pixelsPerTile);
    if(_Natural>.5)
    {
     float3 waterColor=FlatColor(float3(.20,.43,.55))*illumination;
     if(_ShowWater>.5)
     {
      FlatWaterSurfaceData waterSurface;
      waterColor=FlatWaterShade(uv*_MapSize.xy,mixing,field,FlatMaterialColor(floor(geo.g+.5))*.72,illumination,localView,localLight,_WaveAmount*(1-waterIce),waterKind,coast,channel,waterSurface);
      [branch] if(water>.001&&lakeIce<.999)
       waterColor=FlatReflectWater(waterColor,uv*_MapSize.xy,field,field.r+field.b,localView,localLight,illumination,waterSurface,_Clouds*_SkyReady,0);
     }
     col=lerp(col,waterColor,water);
     col=lerp(col,FlatColor(float3(.15,.40,.56))*illumination,riverOverviewInk*(1-waterIce)*.79);
    }
    if(max(polarIce,landSnow)>.001)col=lerp(col,FlatPolarIceColor(uv,water)*illumination*lerp(terrainRelief.rgb,1,water),max(polarIce,landSnow));
    if(lakeIce>.001)col=lerp(col,FlatLakeIceColor(radial,lakePixelKm)*illumination,lakeIce);
    if(_FlatWorldReady>.5)
    {
     float major;float contour=FlatContour(landBed,_ContourInterval,pixelsPerTile,major);
     float strength=_ShowContours*smoothstep(3,5,pixelsPerTile)*(1-water)*step(.01,landBed);
     strength*=FlatGentleContourWeight(terrainGradient);
     col=lerp(col,FlatColor(float3(.31,.30,.24))*illumination,contour*strength*lerp(.23,.42,major));
     float2 f=frac(uv*_MapSize.xy),tileUV=(floor(uv*_MapSize.xy)+.5)*_MapSize.zw;
     float4 adjacentE=tex2D(_FlatOwners,tileUV+float2(_MapSize.z,0)),adjacentN=tex2D(_FlatOwners,tileUV+float2(0,_MapSize.w));
     float ew=min(.22,1/max(1,pixelsPerTile));
     float border=max(step(.002,abs(own.a-adjacentE.a))*step(1-ew,f.x),step(.002,abs(own.a-adjacentN.a))*step(1-ew,f.y));
     col=lerp(col,own.rgb*illumination,.34*_FlatPolitical*step(.001,own.a)*(1-water*.8));
     col=lerp(col,FlatColor(float3(.88,.80,.60))*max(illumination,.15),border*.70*_FlatPolitical*step(.001,own.a));
     float settlement=1-step(.5,abs(floor(info.b+.5)-1));
     col+=settlement*exp(-dot(f-.5,f-.5)*14)*night*_Night*_FlatNightLights*FlatColor(float3(.82,.42,.13))*.75;
    }
    else col+=_Settlement*_Night*night*float3(.9,.38,.09);
    col*=lerp(1,cloudShade,smoothstep(-.05,.18,dot(radial,sun)));
    if(_RiverMap>.5&&_FlatWorldReady>.5)col=riverMapColor;
    if(_FlatDiagnosticDisplay>.5&&_FlatWorldReady>.5)
    {
     float2 displayUV=(floor(uv*_MapSize.xy)+.5)*_MapSize.zw;
     col=FlatPresentationSample(_FlatDisplay,displayUV,0).rgb;
     // Match the folded surface's linear output to the native atmosphere/display pass.
     #ifdef UNITY_COLORSPACE_GAMMA
     col=GammaToLinearSpace(max(0,col));
     #endif
    }
    return float4(max(0,col),1);
#endif
   }
   ENDCG
  }
  Pass
  {
   Name "SURFACE_DEPTH_PERF32"
   Tags { "LightMode"="ShadowCaster" }
   ZWrite On Cull Off
   CGPROGRAM
   #pragma vertex depthVert
   #pragma fragment depthFrag
   #pragma target 3.0
   #pragma multi_compile_shadowcaster
   #include "UnityCG.cginc"
   #include "OrbisPatch.cginc"
   struct depthOutput { V2F_SHADOW_CASTER; float3 local:TEXCOORD1; };
   depthOutput depthVert(appdata_base v){depthOutput o;TRANSFER_SHADOW_CASTER_NORMALOFFSET(o) o.local=mul(_WorldToPlanet,mul(unity_ObjectToWorld,v.vertex)).xyz;return o;}
   float4 depthFrag(depthOutput i):SV_Target {OrbisPatchClip(i.local,i.pos.xy);SHADOW_CASTER_FRAGMENT(i)}
   ENDCG
  }
 }
 Fallback Off
}
