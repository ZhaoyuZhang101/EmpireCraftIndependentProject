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

float PDWildlifeEllipse(float2 p,float2 centre,float2 radius)
{
 float2 q=(p-centre)/radius;
 return 1-step(1,dot(q,q));
}

float PDWildlifeStormWeight(float2 wind)
{
 return smoothstep(.22,1.20,length(wind));
}

float PDWildlifeHeight(float4 actor,float4 state,float2 wind)
{
 bool asleep=state.x>4.5;
 float moving=step(.001,state.y);
 float storm=PDWildlifeStormWeight(wind);
 float h;
 if(actor.x<1.5)h=asleep?.8:1.3+moving*(1-storm*.75)*max(0,sin(actor.z*7.2+actor.w*6));
 else if(actor.x<2.5)h=asleep?1.2:2.5;
 else if(actor.x<3.5)h=asleep?1.0:lerp(1.5,lerp(4.5,1.6,storm),moving);
 else h=asleep?.45:lerp(.8,lerp(3.0,1.0,storm),moving);
 return h*lerp(1,.62,storm*(1-(asleep?1:0)*.5));
}
float PDWildlifeHeight(float4 actor,float4 state)
{
 return PDWildlifeHeight(actor,state,0);
}

// Explicit actor poses keep gait, feeding, sleep and local-season colour on the
// persistent actor clock. The shadow samples this exact function and alpha mask.
// Strong storm wind crouches the body and leans upper silhouette into headwinds
// while strictly anchoring animal feet at footY.
float4 PDWildlifeActorArt(float2 pixel,float4 actor,float4 state,float2 environment,float2 wind,float gustClock)
{
 float2 p=floor(pixel)+.5;
 p.x*=actor.y;
 float clock=actor.z,variant=actor.w;
 float moving=step(.001,state.y);
 float eating=step(2.5,state.x)*(1-step(3.5,state.x));
 float asleep=step(4.5,state.x);
 float cold=saturate((4-environment.y)/14);
 float breath=sin(clock*1.3+variant*4);
 float storm=PDWildlifeStormWeight(wind);
 float localWindX=wind.x*actor.y;
 float headwind=saturate(-localWindX/.95)*storm;
 float tailwind=saturate(localWindX/.95)*storm;
 float gustTremble=storm*sin(gustClock*7.4+variant*19.3);
 // Anchor ground feet at footY so crouching & headwind lean deform ONLY above the feet.
 float footY=actor.x<1.5?-3.0:(actor.x<2.5?-4.0:-3.0);
 float crouch=storm*(1-asleep*.55);
 if(crouch>.001&&p.y>footY)
 {
  float above=p.y-footY;
  float aboveNorm=saturate(above/7.5);
  // Compress vertical posture toward footY (crouch) and lean upper body into headwind.
  p.y=footY+above/lerp(1,.72,crouch);
  p.x-=aboveNorm*(headwind*1.45-tailwind*.55+gustTremble*.25);
 }
 float mask=0;
 float3 ink=0;
 if(actor.x<1.5)
 {
  float gait=clock*7.2+variant*6;
  float lift=moving*(1-storm*.82)*floor(max(0,sin(gait))*3.5);
  p.y-=lift;
  float body=PDWildlifeEllipse(p,float2(-1,-asleep*.6-crouch*.35),lerp(float2(4.3,2.8),float2(4.4,2.0+.4*step(0,breath)),max(asleep,crouch*.65)));
  float belly=PDWildlifeEllipse(p,float2(-.5,-1),float2(3.4,1.6))*body;
  float2 headPoint=p+float2(-headwind*.6,eating*(2+floor((.5+.5*sin(clock*4))*2))+crouch*1.25);
  float head=PDBox(headPoint,float2(2,-1),float2(5,3));
  float earTilt=step(.1,sin(clock*1.2+variant*4));
  float ears=max(PDBox(headPoint,float2(1,2),float2(2,6+earTilt)),PDBox(headPoint,float2(3,2),float2(4,7-earTilt)));
  if(asleep>.5||crouch>.55)
  {
   if(asleep>.5)head=PDWildlifeEllipse(p,float2(2,-.5),float2(2.5,1.8));
   float earFlutter=floor(gustTremble*.65+.5)*crouch;
   ears=PDBox(headPoint,float2(-1,1+earFlutter),float2(3,2.6+earFlutter));
  }
  float extension=moving*floor(sin(gait)*1.8);
  float feet=max(PDBox(p,float2(-4+extension,-4+asleep),float2(-1+extension,-2)),
                 PDBox(p,float2(2-extension,-4+asleep),float2(4-extension,-2)));
  float tail=PDWildlifeEllipse(p,float2(-5,-.3),float2(1.5,1.3));
  float nose=PDBox(headPoint,float2(5,0),float2(6,1))*(1-asleep);
  float leaf=PDBox(p,float2(5,-4),float2(7,-3))*eating*step(0,sin(clock*4));
  mask=max(max(body,head),max(max(ears,tail),max(max(feet,nose),leaf)));
  float3 fur=lerp(float3(.47,.39,.29),float3(.73,.70,.58),variant);
  fur=lerp(fur,float3(.82,.82,.73),cold*.65);
  ink=fur*lerp(.64,1.13,saturate((p.y+3)/6));
  ink=lerp(ink,fur*.55,body*(1-PDWildlifeEllipse(p,float2(-1,-asleep*.6),float2(3.5,1.8))));
  ink=lerp(ink,fur*1.12,belly*.45);
  ink=lerp(ink,float3(.85,.81,.68),max(tail,feet*.65));
  ink=lerp(ink,float3(.46,.27,.23),PDBox(headPoint,float2(3,3),float2(4,6-earTilt))*(1-asleep)*(1-step(.55,crouch)));
  float eye=PDBox(headPoint,float2(4,1),float2(5,2))*(1-asleep)+PDBox(p,float2(2,-1),float2(4,0))*asleep;
  ink=lerp(ink,float3(.085,.075,.06),saturate(eye+nose));
  ink=lerp(ink,float3(.38,.50,.17),leaf);
 }
 else if(actor.x<2.5)
 {
  float gait=clock*6.4+variant*6;
  p.y-=moving*(1-storm*.75)*floor(abs(sin(gait))*1.4);
  float body=PDWildlifeEllipse(p,float2(-1,-crouch*.4),lerp(float2(5.3,3.1),float2(5,2.1+.55*step(0,breath)),max(asleep,crouch*.55)));
  float2 headPoint=p+float2(-eating-headwind*.9,eating*(3+floor(sin(clock*4)))+crouch*2.1);
  float neck=PDBox(headPoint,float2(3,0),float2(6,4));
  float head=PDBox(headPoint,float2(5,2),float2(8,5));
  float ears=max(PDBox(headPoint,float2(4,4),float2(5,7-floor(crouch*1.5))),PDBox(headPoint,float2(7,4),float2(8,6+step(0,sin(clock*.8))-floor(crouch*1.5))));
  if(asleep>.5)
  {
   neck=0;head=PDWildlifeEllipse(p,float2(3.5,0),float2(2.8,1.9));
   ears=PDBox(p,float2(3,1),float2(6,2));
  }
  float swing=moving*floor(sin(gait)*1.8);
  float farLegs=max(PDBox(p,float2(-4-swing,-5),float2(-3-swing,-1)),PDBox(p,float2(1+swing,-5),float2(2+swing,-1)));
  float legs=max(PDBox(p,float2(-2+swing,-5),float2(-1+swing,-1)),PDBox(p,float2(3-swing,-5),float2(4-swing,-1)));
  if(asleep>.5){farLegs=0;legs=PDBox(p,float2(-4,-3),float2(3,-2));}
  float tail=PDBox(p,float2(-7,0+floor(gustTremble*.5)),float2(-5,2));
  mask=max(max(body,neck),max(max(head,ears),max(max(legs,farLegs),tail)));
  float3 coat=lerp(float3(.45,.27,.13),float3(.65,.44,.23),variant);
  coat=lerp(coat,float3(.48,.43,.34),cold*.35);
  ink=coat*lerp(.65,1.15,saturate((p.y+3)/6));
  ink=lerp(ink,coat*.55,body*(1-PDWildlifeEllipse(p,float2(-1,0),float2(4.5,2.2))));
  ink=lerp(ink,float3(.25,.18,.12),farLegs);
  ink=lerp(ink,float3(.34,.23,.14),legs);
  ink=lerp(ink,float3(.13,.11,.09),max(legs,farLegs)*(1-step(-4,p.y)));
  ink=lerp(ink,float3(.83,.73,.52),max(tail,PDBox(p,float2(-3,-2),float2(3,-1))*body*.6));
  float spots=PDBox(p,float2(-3,1),float2(-2,2))+PDBox(p,float2(0,0),float2(1,1))+PDBox(p,float2(2,1),float2(3,2));
  ink=lerp(ink,float3(.83,.68,.43),saturate(spots)*step(.4,variant)*body);
  float eye=PDBox(headPoint,float2(7,3),float2(8,4))*(1-asleep)+PDBox(p,float2(4,0),float2(6,1))*asleep;
  ink=lerp(ink,float3(.09,.075,.055),eye);
 }
 else if(actor.x<3.5)
 {
  float galeGround=step(.45,storm);
  float flight=moving*(1-galeGround);
  p.y-=flight*2;
  float wingClock=clock*9.5+variant*6;
  float reach=2+floor((.5+.5*sin(wingClock))*lerp(5,2.5,storm));
  float wings=PDWildlifeEllipse(p,float2(-1,0),float2(3.3,reach))*flight;
  float body=PDWildlifeEllipse(p,float2(0,-.2-crouch*.5),lerp(float2(4,2.3),float2(3.6,2.0+.6*step(0,breath)),max(asleep,crouch*.6)));
  float2 headPoint=p+float2(-eating-headwind*.5,eating*(2+floor(sin(clock*5)*1.3))+floor(sin(clock*.7+variant)*.7)*(1-flight)*(1-asleep)+crouch*1.1);
  float head=PDBox(headPoint,float2(3,0),float2(5,3))*(1-asleep);
  float tail=PDBox(p,float2(-6,-1+floor(gustTremble*.6)),float2(-3,1));
  float beak=PDBox(headPoint,float2(5,1),float2(7,2))*(1-asleep);
  float feet=max(PDBox(p,float2(-1,-4),float2(0,-2)),PDBox(p,float2(2,-4),float2(3,-2)))*(1-flight)*(1-asleep);
  mask=max(max(body,wings),max(max(head,tail),max(beak,feet)));
  float3 feather=lerp(float3(.23,.34,.42),float3(.37,.43,.24),variant);
  ink=feather*lerp(.65,1.17,saturate((p.y+3)/7));
  float tips=wings*(1-PDWildlifeEllipse(p,float2(-1,0),float2(2.5,max(1,reach-1))));
  float primaries=wings*(PDBox(p,float2(-3,-7),float2(-2,8))+PDBox(p,float2(-1,-7),float2(0,8)))*.5;
  ink=lerp(ink,float3(.12,.17,.21),max(tail,saturate(tips+primaries)));
  ink=lerp(ink,float3(.73,.72,.57),PDWildlifeEllipse(p,float2(1,-.5),float2(2.8,1.4))*body);
  ink=lerp(ink,float3(.26,.32,.30),PDBox(p,float2(-3,0),float2(1,1))*body);
  ink=lerp(ink,feather*.75,PDWildlifeEllipse(p,float2(-1,.2),float2(2.8,1.7))*asleep);
  ink=lerp(ink,float3(.79,.58,.21),beak);
  float eye=PDBox(headPoint,float2(4,1),float2(5,2))*(1-asleep)+PDBox(p,float2(2,0),float2(4,1))*asleep;
  ink=lerp(ink,float3(.055,.07,.065),max(eye,feet));
 }
 else
 {
  float hunker=max(asleep,step(.40,storm));
  float wingClock=clock*11*lerp(.6,1.15,saturate(environment.x))+variant*6;
  float span=lerp(2+step(0,sin(clock*1.1+variant*6)),2+floor((.5+.5*sin(wingClock))*4),moving*(1-hunker));
  span=lerp(span,2,max(eating,storm));
  float2 mirrored=float2(abs(p.x),p.y);
  float2 upperPoint=(mirrored-float2(1+span*.46,1.8))/float2(span*.65,2.2);
  float2 lowerPoint=(mirrored-float2(1+span*.38,-1.4))/float2(span*.55,1.7);
  float upper=(1-step(1,dot(upperPoint,upperPoint)))*step(1,mirrored.x);
  float lower=(1-step(1,dot(lowerPoint,lowerPoint)))*step(1,mirrored.x);
  float wings=max(upper,lower);
  if(hunker>.5)wings=PDWildlifeEllipse(p,float2(-1,0),float2(2.1,3.2+.25*step(0,breath)+gustTremble*.25));
  float body=PDBox(p,float2(-.5,-3),float2(.6,4));
  float antenna=PDBox(mirrored,float2(1,3),float2(2,5))*(1-hunker);
  float nectar=PDBox(p,float2(0,3),float2(1,5+step(0,sin(clock*4))))*eating*(1-hunker);
  mask=max(wings,max(max(body,antenna),nectar));
  float3 wing=lerp(float3(.85,.54,.17),float3(.25,.53,.72),step(.58,variant));
  ink=wing*lerp(.65,1.15,saturate((p.y+3)/7));
  float edge=max(upper*step(.52,dot(upperPoint,upperPoint)),lower*step(.48,dot(lowerPoint,lowerPoint)));
  if(hunker>.5)edge=wings-PDWildlifeEllipse(p,float2(-1,0),float2(1.3,2.4));
  ink=lerp(ink,float3(.16,.16,.14),max(max(body,antenna),edge*wings));
  float spots=PDWildlifeEllipse(mirrored,float2(1+span*.48,1.8),float2(.75,.75))*wings*(1-hunker);
  ink=lerp(ink,float3(.93,.85,.55),spots);
  ink=lerp(ink,float3(.42,.31,.15),nectar);
 }
 ink*=1+breath*lerp(.012,.025,asleep);
 return float4(saturate(ink),saturate(mask));
}
float4 PDWildlifeActorArt(float2 pixel,float4 actor,float4 state,float2 environment)
{
 return PDWildlifeActorArt(pixel,actor,state,environment,0,0);
}

#endif
