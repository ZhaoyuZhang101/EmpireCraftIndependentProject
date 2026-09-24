Shader "ORBIS/CelestialLens"
{
 Properties{_MainTex("Source",2D)="black"{} [HideInInspector] _LensSun("Sun position and intensity",Vector)=(.5,.5,0,1)}
 SubShader{Cull Off ZWrite Off ZTest Always Pass{
 CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 #pragma target 3.0
 #include "UnityCG.cginc"
 sampler2D _MainTex;float4 _LensSun;
 float4 frag(v2f_img i):SV_Target
 {
  float3 scene=tex2D(_MainTex,i.uv).rgb;
  float2 p=(i.uv-_LensSun.xy)*float2(_LensSun.w,1);float r=length(p);
  float offscreen=1-smoothstep(.05,.6,length(max(abs(_LensSun.xy-.5)-.5,0)));
  // White solar core, warm glare and restrained lens-axis reflections.
  float core=exp(-r*r/.000045)*1.35;
  float halo=exp(-r*30)*.64+exp(-r*8)*.11;
  float streak=exp(-p.y*p.y/.000018)*exp(-abs(p.x)*5)*.20;
  float diffraction=pow(abs(cos(atan2(p.y,p.x)*3)),24)*exp(-r*55)*.07;
  float3 ghosts=0;
  [unroll] for(int g=0;g<3;g++)
  {
   float factor=.35+g*.48;
   float2 gp=(i.uv-(.5-(_LensSun.xy-.5)*factor))*float2(_LensSun.w,1);
   float radius=.018+g*.012;
   float ring=exp(-pow((length(gp)-radius)*150,2));
   float fill=exp(-dot(gp,gp)/(radius*radius));
   ghosts+=lerp(float3(.45,.62,.8),float3(1,.55,.23),g*.5)*(ring*.018+fill*.009);
  }
  float3 flare=(float3(1,.98,.90)*core+float3(1,.88,.65)*(halo+diffraction)
   +float3(.75,.86,1)*streak+ghosts)*_LensSun.z*offscreen;
  return float4(scene+flare,1);
 }
 ENDCG
 }}
}
