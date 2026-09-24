Shader "ORBIS/SpriteWorld"
{
 Properties{_MainTex("Transparent sprite",2D)="white"{} _Cutoff("Cutout",Range(0,1))=.35}
 SubShader
 {
  Tags{"Queue"="AlphaTest" "RenderType"="TransparentCutout"}
  Pass
  {
   Tags{"LightMode"="ShadowCaster"} Cull Off ZWrite On
   CGPROGRAM
   #pragma vertex depthVert
   #pragma fragment depthFrag
   #pragma multi_compile_shadowcaster
   #include "UnityCG.cginc"
   sampler2D _MainTex;float _Cutoff;
   struct depthOutput{V2F_SHADOW_CASTER;float2 uv:TEXCOORD1;};
   depthOutput depthVert(appdata_base v){depthOutput o;TRANSFER_SHADOW_CASTER_NORMALOFFSET(o) o.uv=v.texcoord.xy;return o;}
   float4 depthFrag(depthOutput i):SV_Target{clip(tex2D(_MainTex,i.uv).a-_Cutoff);SHADOW_CASTER_FRAGMENT(i)}
   ENDCG
  }
  Pass
  {
   Cull Off ZWrite On ZTest LEqual
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   sampler2D _MainTex;float _Cutoff,_Night;float3 _Sun;
   struct v2f{float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;float3 world:TEXCOORD1;};
   v2f vert(appdata_full v){v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.texcoord.xy;o.color=v.color;o.world=mul(unity_ObjectToWorld,v.vertex).xyz;return o;}
   float4 frag(v2f i):SV_Target
   {
    float4 tex=tex2D(_MainTex,i.uv);clip(tex.a-_Cutoff);
    float light=lerp(.8,max(.12,dot(normalize(i.world),normalize(_Sun))),_Night);
    float3 color=tex.rgb*i.color.rgb*light;
    // Bright warm texels can represent windows in the placeholder house.
    float window=step(.85,tex.r)*step(.5,tex.g)*(1-step(.45,tex.b));
    color+=window*_Night*(1-saturate(light))*float3(.7,.35,.06);
    return float4(color,1);
   }
   ENDCG
  }
 }
}
