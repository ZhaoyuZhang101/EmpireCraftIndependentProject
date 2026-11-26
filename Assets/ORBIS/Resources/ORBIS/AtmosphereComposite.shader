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
 UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

 float OrbisCatmullWeight(float x)
 {
  float ax=abs(x);
  if(ax<1.0)return 1.5*ax*ax*ax-2.5*ax*ax+1.0;
  if(ax<2.0)return -0.5*ax*ax*ax+2.5*ax*ax-4.0*ax+2.0;
  return 0.0;
 }

 float4 frag(OrbisLensVaryings i):SV_Target
 {
  float2 duv=i.uv;
  #if UNITY_UV_STARTS_AT_TOP
  if(_MainTex_TexelSize.y<0)duv.y=1-duv.y;
  #endif
  float depth=OrbisEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture,duv));
  // Slope-aware bilateral tolerance: preserves smooth reconstruction across curved ocean/terrain
  // while cleanly rejecting genuine planet-limb / horizon depth jumps.
  float depthTol=max(.020*depth,min(.22*depth,fwidth(depth)*2.8));
  float2 grid=i.uv*_VolumeTexelSize.zw-.5,base=floor(grid),fracUV=frac(grid);

  float4 l00=tex2D(_RadianceTex,(base+float2(0.5,0.5))*_VolumeTexelSize.xy);
  float4 l10=tex2D(_RadianceTex,(base+float2(1.5,0.5))*_VolumeTexelSize.xy);
  float4 l01=tex2D(_RadianceTex,(base+float2(0.5,1.5))*_VolumeTexelSize.xy);
  float4 l11=tex2D(_RadianceTex,(base+float2(1.5,1.5))*_VolumeTexelSize.xy);

  float4 t00=tex2D(_TransmittanceTex,(base+float2(0.5,0.5))*_VolumeTexelSize.xy);
  float4 t10=tex2D(_TransmittanceTex,(base+float2(1.5,0.5))*_VolumeTexelSize.xy);
  float4 t01=tex2D(_TransmittanceTex,(base+float2(0.5,1.5))*_VolumeTexelSize.xy);
  float4 t11=tex2D(_TransmittanceTex,(base+float2(1.5,1.5))*_VolumeTexelSize.xy);

  float4 d4=abs(float4(l00.a,l10.a,l01.a,l11.a)-depth);
  float maxDiff=max(max(d4.x,d4.y),max(d4.z,d4.w));

  float3 L=0,T=1;
  [branch] if(maxDiff<=depthTol)
  {
   // Continuous depth neighborhood (>97% of pixels): smooth 5-tap Catmull-Rom cubic cross
   // reconstruction clamped strictly to the 2x2 neighborhood [min, max] (zero ringing, silky soft edges).
   float3 biL=lerp(lerp(l00.rgb,l10.rgb,fracUV.x),lerp(l01.rgb,l11.rgb,fracUV.x),fracUV.y);
   float3 biT=lerp(lerp(t00.rgb,t10.rgb,fracUV.x),lerp(t01.rgb,t11.rgb,fracUV.x),fracUV.y);

   float3 minL=min(min(l00.rgb,l10.rgb),min(l01.rgb,l11.rgb));
   float3 maxL=max(max(l00.rgb,l10.rgb),max(l01.rgb,l11.rgb));
   float3 minT=min(min(t00.rgb,t10.rgb),min(t01.rgb,t11.rgb));
   float3 maxT=max(max(t00.rgb,t10.rgb),max(t01.rgb,t11.rgb));

   float2 uvL=(base+float2(-0.5,0.5+fracUV.y))*_VolumeTexelSize.xy;
   float2 uvR=(base+float2( 2.5,0.5+fracUV.y))*_VolumeTexelSize.xy;
   float2 uvD=(base+float2(0.5+fracUV.x,-0.5))*_VolumeTexelSize.xy;
   float2 uvU=(base+float2(0.5+fracUV.x, 2.5))*_VolumeTexelSize.xy;

   float4 lL=tex2D(_RadianceTex,uvL),lR=tex2D(_RadianceTex,uvR);
   float4 lD=tex2D(_RadianceTex,uvD),lU=tex2D(_RadianceTex,uvU);
   float4 tL=tex2D(_TransmittanceTex,uvL),tR=tex2D(_TransmittanceTex,uvR);
   float4 tD=tex2D(_TransmittanceTex,uvD),tU=tex2D(_TransmittanceTex,uvU);

   float4 outOk=step(abs(float4(lL.a,lR.a,lD.a,lU.a)-depth),depthTol*1.5);
   float wx0=OrbisCatmullWeight(1.0+fracUV.x)*outOk.x;
   float wx3=OrbisCatmullWeight(2.0-fracUV.x)*outOk.y;
   float wy0=OrbisCatmullWeight(1.0+fracUV.y)*outOk.z;
   float wy3=OrbisCatmullWeight(2.0-fracUV.y)*outOk.w;

   float coreW=1.0-(wx0+wx3+wy0+wy3);
   L=clamp((biL*coreW+lL.rgb*wx0+lR.rgb*wx3+lD.rgb*wy0+lU.rgb*wy3),minL,maxL);
   T=clamp((biT*coreW+tL.rgb*wx0+tR.rgb*wx3+tD.rgb*wy0+tU.rgb*wy3),minT,maxT);
  }
  else
  {
   // Planet limb / mountain silhouette boundary: softened bilateral tent (avoids blocky point-snapping)
   float invDepth=1.0/max(.01,depth);
   float4 excess=max(0.0,d4-depthTol)*invDepth;
   float4 bW=float4((1-fracUV.x)*(1-fracUV.y),fracUV.x*(1-fracUV.y),(1-fracUV.x)*fracUV.y,fracUV.x*fracUV.y);
   float4 w=bW*exp(-excess*24.0);
   float sum=w.x+w.y+w.z+w.w;
   if(sum<1e-5)
   {
    float best=d4.x;L=l00.rgb;T=t00.rgb;
    if(d4.y<best){best=d4.y;L=l10.rgb;T=t10.rgb;}
    if(d4.z<best){best=d4.z;L=l01.rgb;T=t01.rgb;}
    if(d4.w<best){L=l11.rgb;T=t11.rgb;}
   }
   else
   {
    float invSum=1.0/sum;
    L=(l00.rgb*w.x+l10.rgb*w.y+l01.rgb*w.z+l11.rgb*w.w)*invSum;
    T=(t00.rgb*w.x+t10.rgb*w.y+t01.rgb*w.z+t11.rgb*w.w)*invSum;
   }
  }
  float3 background=OrbisBackgroundWithSun(tex2D(_MainTex,i.uv).rgb,i.uv,depth);
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
   w*=light.a<50?1:0;L+=light.rgb*w;T+=trans.rgb*w;sum+=w;
  }
  if(sum>1e-5){L/=sum;T/=sum;}else {L=0;T=1;}
  return float4(OrbisDisplay(background*saturate(T)+L),1);
 }
 ENDCG
 }
 Pass {
 Name "TEMPORAL_RESOLVE"
 CGPROGRAM
 #pragma target 3.5
 #pragma vertex vert_img
 #pragma fragment fragTemporal
 #include "OrbisOptics.cginc"
 #include "OrbisAtmosphereView.cginc"
 sampler2D _RadianceTex,_TransmittanceTex,_CloudHistoryLight,_CloudHistoryTrans;
 float4 _VolumeTexelSize,_CloudHistoryParams;
 float4x4 _CloudPrevVP;

 struct TemporalMRT
 {
  float4 light:SV_Target0;
  float4 trans:SV_Target1;
 };

 float3 OrbisKarisPack(float3 c)
 {
  float peak=max(c.r,max(c.g,c.b));
  return c/(1.0+max(0.0,peak));
 }

 float3 OrbisKarisUnpack(float3 c)
 {
  float peak=max(c.r,max(c.g,c.b));
  return c/max(1e-4,1.0-min(0.999,peak));
 }

 TemporalMRT fragTemporal(v2f_img i)
 {
  TemporalMRT o;
  float4 currL=tex2D(_RadianceTex,i.uv);
  float4 currT=tex2D(_TransmittanceTex,i.uv);
  o.light=currL;
  o.trans=currT;
  if(_CloudHistoryParams.y<.5)return o;

  // Reconstruct 3D planet-local cloud hit position for parallax-accurate reprojection
  float3 camO=OrbisViewOrigin(i.uv);
  float3 camD=normalize(OrbisViewRay(i.uv));
  float3 cloudPos=camO+camD*max(1e-4,currT.a);
  float4 prevClip=mul(_CloudPrevVP,float4(cloudPos,1.0));
  if(prevClip.w<=1e-5)return o;

  float2 prevUV=prevClip.xy/prevClip.w*0.5+0.5;
  float2 border=_VolumeTexelSize.xy*0.5;
  if(any(prevUV<border)||any(prevUV>1.0-border))return o;

  float4 histL=tex2D(_CloudHistoryLight,prevUV);
  float4 histT=tex2D(_CloudHistoryTrans,prevUV);

  // Depth-consistent 3x3 neighborhood moments & min/max box in Karis perceptual space (PlanetVolumetricCloudsV3 _PERCEPTUAL_BLENDING)
  float sceneDepth=currL.a;
  float depthGuard=max(0.045*sceneDepth,0.20);
  float3 cPack=OrbisKarisPack(max(0.0,currL.rgb));
  float3 m1L=0,m2L=0,minL=cPack,maxL=cPack;
  float3 m1T=0,m2T=0,minT=currT.rgb,maxT=currT.rgb;
  float wSum=0;

  [unroll] for(int dy=-1;dy<=1;dy++)
  [unroll] for(int dx=-1;dx<=1;dx++)
  {
   float2 suv=i.uv+float2(dx,dy)*_VolumeTexelSize.xy;
   float4 nL=tex2D(_RadianceTex,suv);
   float4 nT=tex2D(_TransmittanceTex,suv);
   float sameSurf=(dx==0&&dy==0)?1.0:step(abs(nL.a-sceneDepth),depthGuard);
   if(sameSurf>0.5)
   {
    float3 pL=OrbisKarisPack(max(0.0,nL.rgb));
    m1L+=pL;m2L+=pL*pL;
    minL=min(minL,pL);maxL=max(maxL,pL);
    m1T+=nT.rgb;m2T+=nT.rgb*nT.rgb;
    minT=min(minT,nT.rgb);maxT=max(maxT,nT.rgb);
    wSum+=1.0;
   }
  }

  float invW=1.0/max(1.0,wSum);
  float3 muL=m1L*invW,muT=m1T*invW;
  float3 sigL=sqrt(max(0.0,m2L*invW-muL*muL));
  float3 sigT=sqrt(max(0.0,m2T*invW-muT*muT));

  // Tight velocity-adaptive variance & AABB box under rapid UV velocity (wheel zoom / quick pan) -> zero ghost halos!
  float uvVel=length((prevUV-i.uv)*_VolumeTexelSize.zw);
  float velFactor=saturate(uvVel*0.20);
  float gamma=lerp(1.20,0.55,velFactor);

  float3 boxMinL=max(minL*0.98,muL-gamma*sigL);
  float3 boxMaxL=min(maxL*1.02+1e-4,muL+gamma*sigL);
  float3 boxMinT=max(max(0.0,minT-0.006),muT-gamma*sigT);
  float3 boxMaxT=min(min(1.0,maxT+0.006),muT+gamma*sigT);

  float3 hPack=clamp(OrbisKarisPack(max(0.0,histL.rgb)),boxMinL,boxMaxL);
  float3 clampedL=OrbisKarisUnpack(hPack);
  float3 clampedT=clamp(histT.rgb,boxMinT,boxMaxT);

  float depthRel=abs(histL.a-sceneDepth)/max(0.02,sceneDepth);
  float depthConf=saturate(1.0-(depthRel-0.06)*7.0);
  float alpha=_CloudHistoryParams.x*depthConf*lerp(1.0,0.48,velFactor);

  o.light=float4(lerp(currL.rgb,clampedL,alpha),currL.a);
  o.trans=float4(lerp(currT.rgb,clampedT,alpha),currT.a);
  return o;
 }
 ENDCG
 }

 }
}
