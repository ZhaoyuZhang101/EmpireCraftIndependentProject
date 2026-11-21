Shader "ORBIS/FlatWorld/WildlifeActors"
{
 Properties
 {
  [HideInInspector] _VegetationWindTex("Seasonal vegetation wind",2D)="black"{}
  [HideInInspector] _VegetationWindReady("Vegetation wind ready",Float)=0
  [HideInInspector] _VegetationWindBlend("Vegetation wind month blend",Float)=0
  _SkyTex("Climate cloud shadows",2D)="black"{}
  _VisualTex("Terrain fields",2D)="black"{}
  _InfoTex("Terrain objects",2D)="black"{}
  [HideInInspector] _EarthSurveyMask("Unedited Earth terrain",2D)="white"{}
  [HideInInspector] _EarthStraitShore("Regional marine shorelines",2D)="black"{}
  [HideInInspector] _EarthStraitDetail("Surveyed strait banks",2D)="black"{}
  [HideInInspector] _EarthStraitReady("Strait shoreline survey ready",Float)=0
  [HideInInspector] _PlantHeightTex("Local terrain vertex heights",2D)="black"{}
  [HideInInspector] _PlantLattice("Local height lattice",Vector)=(0,0,0,0)
  [HideInInspector] _ScrollRelief("Terrain relief view",Vector)=(0,0,.72,0)
  [HideInInspector] _MapSize("World dimensions and inverse dimensions",Vector)=(1,1,1,1)
  [HideInInspector] _FieldReady("Terrain and bed fields ready",Float)=0
  [HideInInspector] _RiverAtlas("Continuous river field",2D)="black"{}
  [HideInInspector] _RiverAtlasLayout("River field layout",Vector)=(0,0,0,0)
  [HideInInspector] _RiverAtlasReady("River field ready",Float)=0
  [HideInInspector] _RiverPreviewReady("Initial river field ready",Float)=0
  [HideInInspector] _RiverSeasonTex("Seasonal river discharge",2D)="black"{}
  [HideInInspector] _RiverSeasonReady("River seasons ready",Float)=0
  [HideInInspector] _RiverSeason("Current and next discharge month",Vector)=(0,1,0,0)
  _EcologyTex("Vegetation species",2D)="black"{}
  _VegetationSeasonTex("Seasonal foliage",2D)="white"{}
  [HideInInspector] _ClimateYearHeat("Regional temperature anomaly",Vector)=(0,0,0,0)
  [HideInInspector] _ClimateYearRain("Regional rainfall anomaly",Vector)=(0,0,0,0)
 }
 SubShader
 {
  Tags {"Queue"="Transparent-10" "RenderType"="Transparent"}
  Pass
  {
   Cull Off ZWrite Off ZTest LEqual
   Blend SrcAlpha OneMinusSrcAlpha
   CGPROGRAM
   #pragma target 3.0
   #pragma multi_compile_local __ ORBIS_ATLAS_PLANTS
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   #define ORBIS_FLAT_NO_SWE
   #pragma multi_compile_local_fragment __ ORBIS_RIVER_RASTER ORBIS_RIVER_VECTORS
   #define ORBIS_SURFACE_RIVER_VARIANTS 1
   #include "Water/FlatWater.cginc"
   #ifdef ORBIS_ATLAS_PLANTS
   #include "ScrollProjection.cginc"
   #else
   #define ORBIS_LOCAL_PLANTS 1
   #include "LocalProjection.cginc"
   #endif
   #include "FlatSkyComposite.cginc"
   sampler2D _InfoTex;
   #include "FlatPixelDetail.cginc"
   float _WildlifeOcclusionReady;
   struct appdata
   {
    float4 vertex:POSITION;float2 pixel:TEXCOORD0;
    float4 actor:TEXCOORD1;float4 state:TEXCOORD2;
    float4 geographic:TEXCOORD3;float4 motion:TEXCOORD4;
    float4 cellAxes:TEXCOORD5;
   };
   struct v2f
   {
    float4 pos:SV_POSITION;float2 pixel:TEXCOORD0;
    float4 actor:TEXCOORD1;float4 state:TEXCOORD2;
    float4 geographic:TEXCOORD3;float4 motion:TEXCOORD4;
    float4 cellAxes:TEXCOORD5;
    float2 projected:TEXCOORD6;
    #ifdef ORBIS_ATLAS_PLANTS
    float2 atlasUV:TEXCOORD7;
    #endif
   };
   v2f vert(appdata v)
   {
    v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.pixel=v.pixel;
    float3 offset=mul(unity_ObjectToWorld,v.vertex).xyz-_ScrollNormal;
    o.projected=float2(dot(offset,_ScrollRight),dot(offset,_ScrollUp));
    #ifdef ORBIS_ATLAS_PLANTS
    float relative=dot(offset,_ScrollNormal)-_ScrollRelief.y*_ScrollRelief.w;
    float2 plane=o.projected*(1-relative/.18)-float2(0,relative*_ScrollRelief.z);
    o.atlasUV=(plane/(6.283185307*_MapSize.z)+_ScrollPan.xy)/_ProjectionSize.xy;
    #endif
    o.actor=v.actor;o.state=v.state;o.geographic=v.geographic;o.motion=v.motion;o.cellAxes=v.cellAxes;return o;
   }
   float4 AnimalArt(float2 pixel,float4 actor,float4 state,float2 environment)
   {
    return PDWildlifeActorArt(pixel,actor,state,environment);
   }
   float4 frag(v2f i):SV_Target
   {
    float3 radial=FlatRadial(i.geographic.xy);
    float3 sun=FlatGeographicLight(i.geographic.xy,normalize(_FlatSun+float3(0,1e-7,0)));
    float daylight=smoothstep(-.04,.18,sun.z);
    float height=PDWildlifeHeight(i.actor,i.state);
    float2 cast=clamp(-i.geographic.zw/max(.35,sun.z),-2.6,2.6)*height;
    float4 art=AnimalArt(i.pixel,i.actor,i.state,i.motion.zw);
    float shadow=AnimalArt(i.pixel-cast,i.actor,i.state,i.motion.zw).a*daylight*.25;
    float3 light=FlatDayLight(radial,i.geographic.xy);
    float3 ink=FlatColor(art.rgb)*light;
    float particle=0;
    if(i.state.y>.001)
    {
     [unroll] for(int p=0;p<4;p++)
     {
      float age=frac(i.actor.z*.8+p*.25+i.actor.w);
      float2 side=float2(-i.motion.y,i.motion.x);
      float2 centre=-i.motion.xy*(4+age*10)+side*(frac(i.actor.w*17+p*1.618)*4-2);
      float2 distance=abs(floor(i.pixel)-floor(centre));
      float mask=1-step(.8+age*.5,max(distance.x,distance.y));
      particle=max(particle,mask*(1-age)*(i.actor.x<2.5?lerp(.35,.14,i.state.w):.20));
     }
    }
    float opacity=art.a+(1-art.a)*(particle+(1-particle)*shadow);
    float3 trailColour=lerp(float3(.65,.58,.43),float3(.37,.35,.27),i.state.w);
    if(i.actor.x>2.5)trailColour=i.actor.x>3.5?float3(.81,.73,.45):float3(.71,.74,.69);
    float3 colour=ink*art.a+FlatColor(trailColour)*light*particle*(1-art.a);
    opacity*=i.state.z;
    clip(opacity-.001);
    if(_WildlifeOcclusionReady>.5)
    {
     float2 cell=i.geographic.xy*_MapSize.xy+i.cellAxes.xy*i.pixel.x+i.cellAxes.zw*i.pixel.y;
     float2 uv=(floor(cell)+.5)*_MapSize.zw;
     float4 info=tex2Dlod(_InfoTex,float4(uv,0,0))*255;
     float4 field=FlatField(cell);
     // The difference cancels painted colour and preserves the exact alpha of
     // existing tree/building artwork, including seasonal crowns and silhouettes.
     #ifdef ORBIS_ATLAS_PLANTS
     PDPlantView plantView=PDAtlasPlantView(i.projected,i.atlasUV);
     #else
     PDPlantView plantView=PDLocalPlantView(i.projected,0);
     #endif
     float3 covered=PixelProps(0,cell,field,0,floor(info.b+.5),1,plantView);
     float3 open=PixelProps(1,cell,field,0,floor(info.b+.5),1,plantView)-covered;
     float visible=step(.1,max(open.r,max(open.g,open.b)));
     opacity*=lerp(1,visible,saturate(_DetailWeight));
     clip(opacity-.001);
    }
    colour/=max(.001,art.a+(1-art.a)*(particle+(1-particle)*shadow));
    #ifdef UNITY_COLORSPACE_GAMMA
    colour=GammaToLinearSpace(max(0,colour));
    #endif
    return float4(max(0,colour),opacity);
   }
   ENDCG
  }
 }
 Fallback Off
}
