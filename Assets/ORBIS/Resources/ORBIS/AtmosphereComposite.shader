Shader "ORBIS/AtmosphereComposite"
{
 Properties {_MainTex("Source",2D)="black"{} _RadianceTex("Scattering",2D)="black"{} _TransmittanceTex("RGB transmission",2D)="white"{}}
 SubShader {Cull Off ZWrite Off ZTest Always
 Pass {
 CGPROGRAM
 #pragma target 3.5
 #pragma vertex OrbisLensVertex
 #pragma fragment frag
 #include "OrbisOptics.cginc"
 #include "OrbisAtmosphereView.cginc"
 #include "OrbisCelestialLens.cginc"
 sampler2D _MainTex,_RadianceTex,_TransmittanceTex;
 float4 _MainTex_TexelSize,_VolumeTexelSize;
 // Orbit mist (globals, 0 = off): cloud between the eye and the ground scatters light, so the
 // surface behind it is blurred and greyed in proportion to cloud opacity; _OrbitHalo lets
 // bright cloud bleed softly into its surroundings.
 float _OrbitMist,_OrbitMistBlur,_OrbitMistGrey,_OrbitHalo;
 UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
 static const float2 OrbitRing[8]={float2(1,0),float2(.7071,.7071),float2(0,1),float2(-.7071,.7071),
                                   float2(-1,0),float2(-.7071,-.7071),float2(0,-1),float2(.7071,-.7071)};
 float4 frag(OrbisLensVaryings i):SV_Target
 {
  float2 duv=i.uv;
  #if UNITY_UV_STARTS_AT_TOP
  if(_MainTex_TexelSize.y<0)duv.y=1-duv.y;
  #endif
  float depth=OrbisEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture,duv));
  float2 grid=i.uv*_VolumeTexelSize.zw-.5,base=floor(grid),fracUV=frac(grid);
  float3 L=0,T=0;float sum=0,best=1e20;float3 nearestL=0,nearestT=1;
  [unroll] for(int y=0;y<2;y++)[unroll] for(int x=0;x<2;x++)
  {
   float2 uv=(base+float2(x,y)+.5)*_VolumeTexelSize.xy;
   float4 light=tex2D(_RadianceTex,uv),trans=tex2D(_TransmittanceTex,uv);
   float error=abs(light.a-depth)/max(.01,depth);
   if(error<best){best=error;nearestL=light.rgb;nearestT=trans.rgb;}
   float bilinear=(x?fracUV.x:1-fracUV.x)*(y?fracUV.y:1-fracUV.y);
   float w=bilinear*exp(-error*100.0);L+=light.rgb*w;T+=trans.rgb*w;sum+=w;
  }
  if(sum<1e-5){L=nearestL;T=nearestT;}else {L/=sum;T/=sum;}
  float3 scene=tex2D(_MainTex,i.uv).rgb;
  [branch] if(_OrbitMist>.001)
  {
   float aspect=_MainTex_TexelSize.x/max(1e-6,abs(_MainTex_TexelSize.y));
   float veil=saturate((1.0-dot(saturate(T),float3(.3333,.3333,.3334)))*1.25-.04)*saturate(_OrbitMist);
   [branch] if(veil>.01)
   {
    // Two 4-tap rings: radius grows with the cloud's opacity (fractions of screen height).
    float radius=_OrbitMistBlur*veil*.0075;
    float3 blurred=scene;
    [unroll] for(int k=0;k<8;k++)
     blurred+=tex2D(_MainTex,i.uv+OrbitRing[k]*float2(aspect,1)*radius*((k&1)?.55:1.0)).rgb;
    blurred/=9.0;
    float grey=dot(blurred,float3(.2126,.7152,.0722));
    scene=lerp(blurred,grey*float3(.96,.99,1.05),veil*saturate(_OrbitMistGrey));
   }
   [branch] if(_OrbitHalo>.001)
   {
    float3 glow=0;
    [unroll] for(int h=0;h<8;h++)
    {
     float2 offset=OrbitRing[h]*float2(aspect,1)*((h&1)?.011:.020);
     glow+=max(0,tex2D(_RadianceTex,i.uv+offset).rgb-.48);
    }
    L+=glow*(_OrbitHalo*saturate(_OrbitMist)/8.0);
   }
  }
  float3 background=OrbisBackgroundWithSun(scene,i.uv,depth);
  return float4(OrbisDisplay(background*saturate(T)+L+OrbisLensRadiance(i)),1);
 }
 ENDCG }
 Pass {
 Name "MAP_COMPOSITE"
 CGPROGRAM
 #pragma target 3.5
 #pragma vertex vert_img
 #pragma fragment frag
 #include "OrbisOptics.cginc"
 sampler2D _MainTex,_RadianceTex,_TransmittanceTex;
 float4 _VolumeTexelSize;
  float3 _MapBL,_MapTL,_MapTR,_MapBR;float _MarkleyMap,_LocalCloudMap;
 float4 frag(v2f_img i):SV_Target
 {
  float3 at=lerp(lerp(_MapBL,_MapBR,i.uv.x),lerp(_MapTL,_MapTR,i.uv.x),i.uv.y);
  float3 background=tex2D(_MainTex,i.uv).rgb;
  if(_LocalCloudMap<.5&&_MarkleyMap<.5&&((_ScrollCloudSource<.5&&abs(at.x)>ORBIS_PI)||abs(at.y)>ORBIS_PI*.5))return float4(OrbisDisplay(background),1);
  float2 grid=i.uv*_VolumeTexelSize.zw-.5,base=floor(grid),f=frac(grid);
  float3 L=0,T=0;float sum=0;
  [unroll] for(int y=0;y<2;y++)[unroll] for(int x=0;x<2;x++)
  {
   float2 uv=(base+float2(x,y)+.5)*_VolumeTexelSize.xy;
   float4 light=tex2D(_RadianceTex,uv),trans=tex2D(_TransmittanceTex,uv);
   float w=(x?f.x:1-f.x)*(y?f.y:1-f.y);
   // Atlas rays inside the map report depth 8, outside report 100. Do not mix the two.
   w*=light.a<50?1:0;L+=light.rgb*w;T+=trans.rgb*w;sum+=w;
  }
  if(sum>1e-5){L/=sum;T/=sum;}else {L=0;T=1;}
  return float4(OrbisDisplay(background*saturate(T)+L),1);
 }
 ENDCG
 }

 }
}
