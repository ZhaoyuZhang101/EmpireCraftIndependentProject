Shader "ORBIS/FlatWorld/ScrollSurface"
{
 Properties
 {
  [HideInInspector] _VegetationWindTex("Seasonal vegetation wind",2D)="black"{}
  [HideInInspector] _VegetationWindReady("Vegetation wind ready",Float)=0
  [HideInInspector] _VegetationWindBlend("Vegetation wind month blend",Float)=0
  [HideInInspector] _VegetationSeasonTex("Climate vegetation seasons",2D)="white"{}
  [HideInInspector] _VegetationSeasonReady("Vegetation seasons ready",Float)=0
  [HideInInspector] _VegetationSeason("Current month next month blend",Vector)=(0,1,0,0)
  [HideInInspector] _ClimateYearHeat("Regional temperature anomaly",Vector)=(0,0,0,0)
  [HideInInspector] _ClimateYearRain("Regional rainfall anomaly",Vector)=(0,0,0,0)
  [HideInInspector] _ScrollRelief("Terrain relief view",Vector)=(0,0,.72,0)
  [HideInInspector] _PlantHeightTex("Atlas terrain vertex heights",2D)="black"{}
  [HideInInspector] _PlantLattice("Atlas height lattice",Vector)=(0,0,0,0)
  [HideInInspector] _EcologyTex("Derived ecology traits",2D)="black"{}
  [HideInInspector] _EcologyReady("Ecology traits ready",Float)=0
  [HideInInspector] _MapSize("World dimensions and inverse dimensions",Vector)=(1,1,1,1)
  [HideInInspector] _FieldReady("Terrain and bed fields ready",Float)=0
  [HideInInspector] _PoleBedDepth("Polar bed and depth, south then north",Vector)=(0,0,0,0)
  [HideInInspector] _EarthPolarBed("Surveyed polar bed",2D)="black"{}
  [HideInInspector] _EarthPolarCoast("Surveyed marine shoreline",2D)="black"{}
  [HideInInspector] _EarthSurveyMask("Unedited Earth terrain",2D)="white"{}
  [HideInInspector] _EarthSurveyAtPoles("Unedited south and north poles",Vector)=(1,1,0,0)
  [HideInInspector] _EarthPolarReady("Earth polar survey ready",Float)=0
  [HideInInspector] _EarthLakeShore("Surveyed inland shorelines",2D)="black"{}
  [HideInInspector] _EarthLakeDetail("Near lake shorelines",2D)="black"{}
  [HideInInspector] _EarthLakeReady("Inland shoreline survey ready",Float)=0
  [HideInInspector] _EarthStraitShore("Regional marine shorelines",2D)="black"{}
  [HideInInspector] _EarthStraitDetail("Surveyed strait banks",2D)="black"{}
  [HideInInspector] _EarthStraitReady("Strait shoreline survey ready",Float)=0
  [HideInInspector] _RiverAtlas("Continuous river field",2D)="black"{}
  [HideInInspector] _RiverOverview("River network overview",2D)="black"{}
  [HideInInspector] _RiverOverviewReady("River overview ready",Float)=0
  [HideInInspector] _RiverSurvey("Surveyed river courses",Float)=0
  [HideInInspector] _RiverMap("River network map",Float)=0
  [HideInInspector] _RiverPreviewReady("Initial river field ready",Float)=0
  [HideInInspector] _RiverSeasonTex("Seasonal river discharge",2D)="black"{}
  [HideInInspector] _RiverSeasonReady("River seasons ready",Float)=0
  [HideInInspector] _RiverSeason("Current and next discharge month",Vector)=(0,1,0,0)
  [HideInInspector] _RiverAtlasReady("River field ready",Float)=0
  [HideInInspector] _RiverAtlasLayout("River field layout",Vector)=(0,0,0,0)

 [HideInInspector] _VendorWaterReady ("SW3 resources ready", Float) = 0
 [HideInInspector] _VendorStreamNormal ("Vendor stream waves", 2D) = "gray" {}
 [HideInInspector] _VendorOceanNormal ("Vendor ocean waves", 2D) = "gray" {}
 [HideInInspector] _VendorLakeNormal ("SW3 lake ripples", 2D) = "bump" {}
 [HideInInspector] _VendorOceanFoam ("SW3 ocean foam", 2D) = "black" {}
 [HideInInspector] _VendorRiverFoam ("SW3 river foam", 2D) = "black" {}

 [HideInInspector] _MarkleyDirections ("Markley directions", 2D) = "white" {}

  _MainTex("Map",2D)="white"{} _OwnerTex("Ownership",2D)="black"{} _InfoTex("Semantic data",2D)="black"{}
  _HydroA("Depth flow channel width",2D)="black"{} _HydroB("Meander sediment freshwater links",2D)="black"{}
  _GeoTex("Height material soil water",2D)="black"{} _SWEState("Local shallow water",2D)="gray"{}
  [HideInInspector] _SWEPreviousState("Previous shallow water patch",2D)="gray"{}
  [HideInInspector] _SWEBlend("Patch transition",Float)=1
  _VisualTex("Continuous presentation fields",2D)="black"{} _SkyTex("Climate cloud layers",2D)="black"{}
  [HideInInspector] _SkyReady("Sky cache ready",Float)=0
  [HideInInspector] _SkyClock("Sky time",Float)=0
  [HideInInspector] _FlatClouds("Clouds enabled",Float)=1
  [HideInInspector] _FlatCloudShadows("Cloud shadows enabled",Float)=1
  [HideInInspector] _CloudOpacity("Cloud opacity",Float)=0
  [HideInInspector] _FlatSun("Sun direction",Vector)=(0,1,0,0)
 }
 SubShader
 {
  Tags { "RenderType"="Opaque" "Queue"="Geometry" }
  Pass
  {
   Name "SCROLL_DEPTH"
   Tags { "LightMode"="ShadowCaster" }
   Cull Off ZWrite On ZTest LEqual
   CGPROGRAM
   #pragma vertex depthVert
   #pragma fragment depthFrag
   #pragma target 3.0
   #pragma multi_compile_shadowcaster
   #include "UnityCG.cginc"
   #include "FlatVisualTexture.cginc"
   float4 _MapSize;
   #include "ScrollProjection.cginc"
   struct depthOut { V2F_SHADOW_CASTER; };
   depthOut depthVert(appdata_full v)
   {
    depthOut o;float2 geo;
    v.vertex=float4(ScrollPosition(v.texcoord.xy,0,geo),1);
    v.normal=normalize(lerp(normalize(v.vertex.xyz),_ScrollNormal,_ScrollMorph));
    TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
    return o;
   }
   float4 depthFrag(depthOut i):SV_Target { SHADOW_CASTER_FRAGMENT(i) }
   ENDCG
  }
  Pass
  {
   Name "SCROLL_SURFACE"
   Cull Off ZWrite On ZTest LEqual
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma target 3.0
   #include "UnityCG.cginc"
   #pragma multi_compile_local_fragment __ ORBIS_RIVER_RASTER ORBIS_RIVER_VECTORS
   #define ORBIS_SURFACE_RIVER_VARIANTS 1
   #include "Water/FlatWater.cginc"
   #define ORBIS_SCROLL_SURFACE 1
   #define ORBIS_ATLAS_PLANTS 1
   #include "ScrollProjection.cginc"
   #include "FlatSkyComposite.cginc"
   sampler2D _MainTex,_OwnerTex,_InfoTex;
   float4 _Selected;
   float _Grid,_Natural,_Political,_WaterView,_ShowContours,_ContourInterval,_DrawHeight;
   struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;};
   struct v2f {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float4 world:TEXCOORD1;};
   v2f vert(appdata v)
   {
    v2f o;float2 geo;float weight;float3 p=ScrollPosition(v.uv,0,geo,weight);
    o.pos=UnityObjectToClipPos(float4(p,1))*weight;o.uv=geo;
    o.world=float4(mul(unity_ObjectToWorld,float4(p,1)).xyz*weight,weight);return o;
   }
   #include "FlatPixelDetail.cginc"
   #include "Water/FlatWaterReflection.cginc"
   #include "FlatMapFragment.cginc"
   ENDCG
  }
 }
 Fallback Off
}
