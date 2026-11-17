Shader "ORBIS/FlatWorld/FlatMap"
{
 Properties
 {
  [HideInInspector] _RiverAtlas("Continuous river field",2D)="black"{}
  [HideInInspector] _RiverAtlasReady("River field ready",Float)=0
  [HideInInspector] _RiverAtlasLayout("River field layout",Vector)=(0,0,0,0)

 [HideInInspector] _VendorWaterReady ("SW3 resources ready", Float) = 0
 [HideInInspector] _VendorStreamNormal ("Vendor stream waves", 2D) = "gray" {}
 [HideInInspector] _VendorOceanNormal ("Vendor ocean waves", 2D) = "gray" {}
 [HideInInspector] _VendorLakeNormal ("SW3 lake ripples", 2D) = "bump" {}
 [HideInInspector] _VendorOceanFoam ("SW3 ocean foam", 2D) = "black" {}
 [HideInInspector] _VendorRiverFoam ("SW3 river foam", 2D) = "black" {}

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
   Name "FLAT_MAP_PERF32"
   Cull Off ZWrite On ZTest LEqual
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma target 3.0
   #include "UnityCG.cginc"
   #include "Water/FlatWater.cginc"
   #include "FlatSkyComposite.cginc"
   sampler2D _MainTex,_OwnerTex,_InfoTex;
   float4 _Selected;
   float _Grid,_PixelsPerTile,_Natural,_Political,_WaterView,_ShowContours,_ContourInterval,_DrawHeight;
   struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;};
   struct v2f {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;};
   v2f vert(appdata v){v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o;}
   #include "FlatPixelDetail.cginc"
   #include "FlatMapFragment.cginc"
   ENDCG
  }
 }
 Fallback Off
}
