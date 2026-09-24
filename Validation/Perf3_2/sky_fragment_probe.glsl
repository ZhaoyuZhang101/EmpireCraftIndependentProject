#version 300 es
precision highp float;
precision highp int;
#define UNITY_COLORSPACE_GAMMA
in vec2 vUV;
out vec4 targetColor;
struct v2f_img {vec2 uv;};
vec3 GammaToLinearSpace(vec3 s){return s*(s*(s*.305306011+.682171111)+.012522878);}
float saturate(float x){return clamp(x,0.0,1.0);}
vec2 saturate(vec2 x){return clamp(x,0.0,1.0);}
vec3 saturate(vec3 x){return clamp(x,0.0,1.0);}
vec4 saturate(vec4 x){return clamp(x,0.0,1.0);}
vec4 hlslTextureLod(sampler2D s,vec4 uv){return textureLod(s,uv.xy,uv.w);}
void OrbisPatchClip(vec3 p,vec2 q){}



   // Engine transform/clip omitted in the fragment-only probe.
   uniform sampler2D _ClimateTex;
   uniform float _VisualTime,_MonthBlend,_ClimateRows;
   uniform vec4 _ClimatePoleSouth,_ClimatePoleNorth;
   float Hash3(vec3 p){p=fract(p*.1031);p+=dot(p,p.yzx+33.33);return fract((p.x+p.y)*p.z);}
   float Noise3(vec3 p)
   {
    vec3 b=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);
    float a=mix(Hash3(b),Hash3(b+vec3(1.0,0.0,0.0)),f.x);
    float c=mix(Hash3(b+vec3(0.0,1.0,0.0)),Hash3(b+vec3(1.0,1.0,0.0)),f.x);
    float d=mix(Hash3(b+vec3(0.0,0.0,1.0)),Hash3(b+vec3(1.0,0.0,1.0)),f.x);
    float e=mix(Hash3(b+vec3(0.0,1.0,1.0)),Hash3(b+vec3(1.0,1.0,1.0)),f.x);
    return mix(mix(a,c,f.y),mix(d,e,f.y),f.z);
   }
   float Fbm(vec3 p)
   {
    // Fixed, small octaves, on a low-resolution cache only; not per terrain pixel.
    float a=Noise3(p)*.54;
    a+=Noise3(p*2.03+vec3(17.0,3.0,11.0))*.28;
    a+=Noise3(p*4.07+vec3(4.0,29.0,5.0))*.13;
    return a+Noise3(p*8.13+vec3(8.0,6.0,13.0))*.05;
   }
   vec3 RotateY(vec3 p,float angle)
   {float s=sin(angle),c=cos(angle);return vec3(c*p.x+s*p.z,p.y,-s*p.x+c*p.z);}
   vec4 frag(v2f_img i)
   {
    vec4 monthly=texture(_ClimateTex,i.uv);
    if(_ClimateRows>0.0)monthly=mix(mix(_ClimatePoleSouth,_ClimatePoleNorth,step(.5,i.uv.y)),monthly,smoothstep(0.0,1.0,min(i.uv.y,1.0-i.uv.y)*_ClimateRows));
    float wet=mix(monthly.r,monthly.g,_MonthBlend),temperature=mix(monthly.b,monthly.a,_MonthBlend);
    float longitude=(i.uv.x-.5)*6.283185307,latitude=(i.uv.y-.5)*3.141592654;
    vec3 radial=vec3(sin(longitude)*cos(latitude),sin(latitude),cos(longitude)*cos(latitude));
    // Rotate a sphere instead of cumulatively advecting a UV texture. Longitude
    // remains periodic and clouds cannot stretch into needles over long sessions.
    vec3 p=RotateY(radial,_VisualTime*.005);
    vec3 wander=vec3(sin(_VisualTime*.007),cos(_VisualTime*.009),sin(_VisualTime*.004))*.65;
    float broad=Fbm(p*4.3+wander);
    float warp=Noise3(p*2.7+vec3(8.0,21.0,4.0));
    float cells=Fbm(p*(17.0+warp*3.0)+wander*.7);
    float threshold=mix(.60,.43,saturate(wet*.85+.10));
    float low=smoothstep(threshold-.055,threshold+.115,broad*.60+cells*.40);
    float erosion=Noise3(p*72.0+wander);
    low=saturate(low*(.78+erosion*.28));
    vec3 upper=RotateY(radial,-_VisualTime*.0035);
    float highNoise=Fbm(upper*vec3(25.0,10.0,25.0)+wander*.4+vec3(13.0,7.0,2.0));
    float high=smoothstep(.58,.75,highNoise)*(.35+.45*wet);
    float brightness=saturate(.5+cells*.45+erosion*.12-low*.22);
    float storm=smoothstep(.4,.85,wet)*smoothstep(.62,.9,low)*smoothstep(.28,.65,temperature);
    return vec4(low,high,brightness,storm);
   }
   
void main(){v2f_img i;i.uv=vUV;targetColor=frag(i);}