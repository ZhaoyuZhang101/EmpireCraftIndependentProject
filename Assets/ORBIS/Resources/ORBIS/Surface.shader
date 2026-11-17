Shader "ORBIS/Surface"
{
 Properties
 {
  [HideInInspector] _RiverAtlas("Continuous river field",2D)="black"{}
  [HideInInspector] _RiverAtlasReady("River field ready",Float)=0
  [HideInInspector] _RiverAtlasLayout("River field layout",Vector)=(0,0,0,0)

  [HideInInspector] _Natural("Natural surface",Float)=1
  [HideInInspector] _VendorWaterReady("SW3 resources ready",Float)=0
  [HideInInspector] _VendorStreamNormal("SW3 stream waves",2D)="gray"{}
  [HideInInspector] _VendorOceanNormal("SW3 ocean waves",2D)="gray"{}
  [HideInInspector] _VendorLakeNormal("SW3 lake ripples",2D)="bump"{}
  [HideInInspector] _VendorOceanFoam("SW3 ocean foam",2D)="black"{}
  [HideInInspector] _VendorRiverFoam("SW3 river foam",2D)="black"{}
  [HideInInspector] _FlatWorldReady("2D authoritative source",Float)=0
  [HideInInspector] _FlatPolitical("2D political tint",Float)=0
  _Tint("Tint",Color)=(1,1,1,1)
  _TerrainAtlas("Biome albedo",2D)="white"{}
  _FlatTerrain("Shared 2D terrain",2D)="white"{} _FlatOwners("Shared owners",2D)="black"{} _FlatInfo("Shared semantics",2D)="black"{}
  _HydroA("Depth flow width",2D)="black"{} _HydroB("Meander sediment freshwater links",2D)="black"{} _GeoTex("Height materials",2D)="black"{}
  _VisualTex("Reconstructed field",2D)="black"{} _SkyTex("Climate cloud layers",2D)="black"{}
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
   // Surface never includes OrbisOptics/OrbisWater/CloudCommon. Atmospheric volume
   // stays in its camera pass. Nine samplers; no ray-march or nested atmosphere paths.
   #define ORBIS_FLAT_NO_SWE
   #include "FlatWorld/Water/FlatWater.cginc"
   #include "FlatWorld/FlatCloudCover.cginc"
   sampler2D _FlatTerrain,_FlatOwners,_FlatInfo,_TerrainAtlas;
   float _FlatWorldReady,_FlatPolitical,_Natural,_Night,_SolarEnergy,_CloudShadow,_ShadowStrength;
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
    float waterEdge=-1;
    float4 field=float4(0,-8,max(.2,i.waterData.x),0),hydro=float4(max(.2,i.waterData.x),0,0,0),mixing=0,geo=0,own=0,info=0;
    if(_FlatWorldReady>.5)
    {
     float2 cell=uv*_MapSize.xy,tileUV=(floor(cell)+.5)*_MapSize.zw;
     field=FlatField(cell);hydro=tex2Dlod(_HydroA,float4(tileUV,0,0));mixing=tex2Dlod(_HydroB,float4(tileUV,0,0));geo=tex2Dlod(_GeoTex,float4(tileUV,0,0));
     own=tex2D(_FlatOwners,tileUV);info=tex2D(_FlatInfo,tileUV)*255;
     col=tex2D(_FlatTerrain,lerp(tileUV,FlatBlendUV(FlatEcotoneCell(cell,field.g)),_SmoothTerrain)).rgb;
     col=FlatEcotoneColor(col,cell,field.a,tex2Dlod(_GeoTex,float4(uv,0,0)).b);
     float aa=clamp(.65/max(1,pixelsPerTile),.008,.28),edgeDistance;float2 riverFlow;
     water=FlatWaterMask(cell,tileUV,hydro,mixing,field,aa,edgeDistance,riverFlow);
     waterEdge=edgeDistance;
     if(hydro.a>.01)hydro.gb=riverFlow;else hydro.gb=tex2D(_HydroA,uv).gb;
     mixing.gb=tex2D(_HydroB,uv).gb;
     col=FlatVegetation(col,cell,field.a,pixelsPerTile);
     col*=FlatFieldRelief(cell,0,float3(-.45,.6,.75));
     float bank=(1-water)*(1-smoothstep(0,(hydro.a>.01?.075:.16)+aa,edgeDistance))*_ShowBanks;
     col=lerp(col,FlatMaterialColor(floor(geo.g+.5)),bank*.58);
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
    float polarIce=FlatPolarIceMask(uv,water,0)*step(.5,_Natural);
    float landSnow=0;
    if(_FlatWorldReady>.5)landSnow=FlatLandSnow(uv*_MapSize.xy,_FlatInfo)*(1-water)*step(.5,_Natural);
    if(_Natural>.5)
    {
     float3 waterColor=FlatColor(float3(.20,.43,.55))*illumination;
     float waterKind=floor(info.g+.5);
     if(_FlatWorldReady>.5)waterKind=hydro.a>.01&&field.g<0?5:FlatAreaWaterKind(uv*_MapSize.xy,_FlatInfo,waterKind);
     if(_ShowWater>.5)waterColor=FlatWaterShade(uv*_MapSize.xy,hydro,mixing,field,FlatMaterialColor(floor(geo.g+.5))*.72,illumination,localView,localLight,_WaveAmount*(1-polarIce),waterKind,waterEdge);
     col=lerp(col,waterColor,water);
    }
    if(max(polarIce,landSnow)>.001)col=lerp(col,FlatPolarIceColor(uv,water)*illumination,max(polarIce,landSnow));
    if(_FlatWorldReady>.5)
    {
     float major;float contour=FlatContour(field.r,_ContourInterval,pixelsPerTile,major);
     float strength=_ShowContours*smoothstep(3,5,pixelsPerTile)*(1-water)*step(.01,field.r);
     strength*=FlatGentleContourWeight(uv*_MapSize.xy,FlatFieldGradient(uv*_MapSize.xy,0));
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
    return float4(max(0,col),1);
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
