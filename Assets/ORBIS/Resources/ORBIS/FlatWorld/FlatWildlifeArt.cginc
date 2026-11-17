#ifndef ORBIS_FLAT_WILDLIFE_ART
#define ORBIS_FLAT_WILDLIFE_ART
// Compact overhead sprites retain a short shaded body, visible back and a few
// articulated pixels. The same alpha mask also casts each creature's shadow.
float4 PDAmbientArt(float2 pixel,float species,float4 pose,float variant)
{
 float2 p=floor(pixel)+.5;
 p.x*=pose.z;
 float stride=step(0,sin(pose.w*3.4));
 float mask=0;
 float3 ink=0;
 if(species<1.5)
 {
  p.y-=floor(max(0,sin(pose.w*3.4))*1.7);
  float2 bodyPoint=(p-float2(-1,0))/float2(4,2.7);
  float body=1-step(1,dot(bodyPoint,bodyPoint));
  float head=PDBox(p,float2(2,-1),float2(5,3));
  float earTilt=floor(sin(pose.w*.9)*.8+.5);
  float ears=PDBox(p,float2(1,2),float2(2,6+earTilt));
  ears=max(ears,PDBox(p,float2(3,2),float2(4,7-earTilt)));
  float tail=PDBox(p,float2(-6,-1),float2(-4,1));
  float feet=PDBox(p,float2(-3,-3-stride),float2(-1,-2));
  feet=max(feet,PDBox(p,float2(2,-3+stride),float2(4,-1)));
  mask=max(max(body,head),max(max(ears,tail),feet));
  float3 fur=lerp(float3(.57,.51,.40),float3(.76,.75,.65),variant);
  ink=fur*lerp(.64,1.03,step(-.5,p.y));
  ink=lerp(ink,fur*1.14,PDBox(p,float2(-3,0),float2(0,2))*body);
  ink=lerp(ink,float3(.85,.82,.71),tail);
  ink=lerp(ink,float3(.43,.29,.26),PDBox(p,float2(3,3),float2(4,6-earTilt)));
  ink=lerp(ink,float3(.10,.09,.075),PDBox(p,float2(4,1),float2(5,2)));
 }
 else if(species<2.5)
 {
  float2 bodyPoint=(p-float2(-1,0))/float2(5,3);
  float body=1-step(1,dot(bodyPoint,bodyPoint));
  float neck=PDBox(p,float2(3,0),float2(6,4));
  float head=PDBox(p,float2(5,2),float2(8,5));
  float ears=PDBox(p,float2(4,4),float2(5,7));
  ears=max(ears,PDBox(p,float2(7,4),float2(8,6)));
  float legs=PDBox(p,float2(-4,-5+stride),float2(-3,-1));
  legs=max(legs,PDBox(p,float2(2,-4-stride),float2(3,-1)));
  float tail=PDBox(p,float2(-7,0),float2(-5,2));
  mask=max(max(body,neck),max(max(head,ears),max(legs,tail)));
  float3 coat=lerp(float3(.53,.34,.19),float3(.66,.47,.26),variant);
  ink=coat*lerp(.61,1.10,step(-.5,p.y));
  ink=lerp(ink,coat*1.16,PDBox(p,float2(-3,0),float2(2,2))*body);
  ink=lerp(ink,float3(.30,.23,.16),legs);
  ink=lerp(ink,float3(.83,.76,.57),tail);
  float spots=PDBox(p,float2(-3,1),float2(-2,2))+PDBox(p,float2(0,0),float2(1,1));
  ink=lerp(ink,float3(.79,.66,.42),spots*step(.42,variant));
  ink=lerp(ink,float3(.11,.085,.06),PDBox(p,float2(7,3),float2(8,4)));
 }
 else if(species<3.5)
 {
  float reach=2+floor((.5+.5*sin(pose.w*5.7))*5);
  float wings=PDBox(p,float2(-2,-reach),float2(1,reach+1));
  wings=max(wings,PDBox(p,float2(-4,1-reach),float2(-2,reach)));
  float body=PDBox(p,float2(-3,-1),float2(4,2));
  float head=PDBox(p,float2(3,0),float2(5,3));
  float tail=PDBox(p,float2(-6,-1),float2(-3,1));
  float beak=PDBox(p,float2(5,1),float2(6,2));
  mask=max(max(wings,body),max(max(head,tail),beak));
  float3 feather=lerp(float3(.27,.34,.40),float3(.39,.47,.32),variant);
  ink=feather*lerp(.70,1.12,step(0,p.y));
  float tips=step(reach-1,abs(p.y))*wings;
  ink=lerp(ink,float3(.14,.18,.20),max(tips,tail));
  ink=lerp(ink,float3(.74,.74,.61),PDBox(p,float2(-2,0),float2(3,1))*body);
  ink=lerp(ink,float3(.78,.61,.27),beak);
  ink=lerp(ink,float3(.07,.08,.075),PDBox(p,float2(4,1),float2(5,2)));
 }
 else
 {
  float span=2+floor((.5+.5*sin(pose.w*7.2))*3);
  float2 mirrored=float2(abs(p.x),p.y);
  float2 upperPoint=(mirrored-float2(1+span*.46,1.8))/float2(span*.65,2.2);
  float2 lowerPoint=(mirrored-float2(1+span*.38,-1.4))/float2(span*.55,1.7);
  float upper=(1-step(1,dot(upperPoint,upperPoint)))*step(1,mirrored.x);
  float lower=(1-step(1,dot(lowerPoint,lowerPoint)))*step(1,mirrored.x);
  float wings=max(upper,lower);
  float body=PDBox(p,float2(-1,-3),float2(1,4));
  float antenna=PDBox(mirrored,float2(1,4),float2(2,5));
  mask=max(wings,max(body,antenna));
  float3 wing=lerp(float3(.78,.57,.28),float3(.43,.60,.69),step(.58,variant));
  ink=wing*lerp(.70,1.08,step(0,p.y));
  float edge=max(upper*step(.52,dot(upperPoint,upperPoint)),lower*step(.48,dot(lowerPoint,lowerPoint)));
  ink=lerp(ink,float3(.22,.20,.17),max(body,max(antenna,edge*wings)));
  ink=lerp(ink,float3(.91,.81,.51),PDBox(mirrored,float2(2,1),float2(3,2))*wings);
 }
 return float4(saturate(ink),saturate(mask));
}

#endif
