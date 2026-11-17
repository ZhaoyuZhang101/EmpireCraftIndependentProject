Shader "ORBIS/FlatWorld/WildlifeActors"
{
 Properties
 {
  _SkyTex("Climate cloud shadows",2D)="black"{}
  _VisualTex("Terrain fields",2D)="black"{}
  _InfoTex("Terrain objects",2D)="black"{}
  _EcologyTex("Vegetation species",2D)="black"{}
  _VegetationSeasonTex("Seasonal foliage",2D)="white"{}
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
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   #define ORBIS_FLAT_NO_SWE
   #include "Water/FlatWater.cginc"
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
   };
   v2f vert(appdata v)
   {
    v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.pixel=v.pixel;
    o.actor=v.actor;o.state=v.state;o.geographic=v.geographic;o.motion=v.motion;o.cellAxes=v.cellAxes;return o;
   }
   float4 AnimalArt(float2 pixel,float4 actor,float4 state)
   {
    bool asleep=state.x>4.5;
    bool eating=state.x>2.5&&state.x<3.5;
    float2 samplePoint=pixel;
    if(asleep)samplePoint.y=(samplePoint.y+2)*1.6;
    if(eating&&pixel.x*actor.y>2)samplePoint.y+=1+floor(sin(actor.z*3)*1.2);
    float clock=state.y>.001||eating?actor.z:actor.w*6;
    float4 art=PDAmbientArt(samplePoint,actor.x,float4(0,0,actor.y,clock),actor.w);
    if(asleep)
    {
     float2 eye=float2(pixel.x*actor.y,pixel.y);
     art.rgb=lerp(art.rgb,float3(.15,.12,.10),PDBox(eye,float2(3,-1),float2(5,0))*art.a);
    }
    return art;
   }
   float4 frag(v2f i):SV_Target
   {
    float3 radial=FlatRadial(i.geographic.xy);
    float3 sun=FlatGeographicLight(i.geographic.xy,normalize(_FlatSun+float3(0,1e-7,0)));
    float daylight=smoothstep(-.04,.18,sun.z);
    float height=i.actor.x<1.5?1.5:i.actor.x<2.5?2.5:4;
    if(i.state.x>4.5)height*=.5;
    float2 cast=clamp(-i.geographic.zw/max(.35,sun.z),-2.6,2.6)*height;
    float4 art=AnimalArt(i.pixel,i.actor,i.state);
    float shadow=AnimalArt(i.pixel-cast,i.actor,i.state).a*daylight*.25;
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
    float3 colour=ink*art.a+FlatColor(float3(.64,.59,.47))*light*particle*(1-art.a);
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
     float3 covered=PixelProps(0,cell,field,0,floor(info.b+.5),1);
     float3 open=PixelProps(1,cell,field,0,floor(info.b+.5),1)-covered;
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
