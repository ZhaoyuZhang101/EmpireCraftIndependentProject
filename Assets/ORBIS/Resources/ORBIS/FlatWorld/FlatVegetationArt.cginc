#ifndef ORBIS_FLAT_VEGETATION_ART
#define ORBIS_FLAT_VEGETATION_ART

// Presentation only. Species reuse the same five ellipsoids for visible crowns
// and ground shadows in the orthographic 62-degree pixel-art view.
// 0 broadleaf, 1 pine, 2 birch, 3 palm, 4 acacia, 5 willow, 6 shrub, 7 tropical.
static const float3 PDTreeView=float3(0,-.46947156,.88294759);
static const float3 PDTreeUp=float3(0,.88294759,.46947156);

float4 PDTreePlacement(float2 id)
{
    float seed=PDHash(id);
    float2 anchor=float2(16+(seed-.5)*16,13+(PDHash(id+71)-.5)*14);
    return float4(floor(anchor)+.5,.78+.30*PDHash(id+float2(29,13)),seed);
}

float PDTreeSpecies(float4 ecology,float4 info,float shore)
{
    // A negative cover identifies an older renderer without ecology metadata.
    if(ecology.r<-.5)return step(16.5,info.a)*step(info.a,28.5);
    float cover=floor(ecology.r+.5);
    float seed=saturate(ecology.a);
    bool tropical=info.a>.5&&info.a<3.5;
    bool wetEdge=ecology.b>.68&&shore>.2;
    if(cover<.5||cover>10.5)return 6;
    if(cover<1.5)return seed<.52?6:(seed<.76?2:0);
    if(cover<2.5)return 6;
    if(cover<3.5)return seed<.82?4:6;
    if(cover<4.5)return seed<.80?6:0;
    if(cover<5.5)
    {
        if(tropical)return seed<.22?3:7;
        return seed<.20?2:0;
    }
    if(cover<6.5)
    {
        if(wetEdge&&seed<.40)return 5;
        return seed<.32?2:0;
    }
    if(cover<7.5)
    {
        if(wetEdge&&seed<.22)return 5;
        return seed>.60?1:0;
    }
    if(cover<8.5)return seed<.14?2:1;
    if(cover<9.5)return seed<(shore>.4?.48:.18)?3:7;
    if(tropical&&seed<.28)return 3;
    return seed<.75?5:6;
}

float4 PDTreeLobe(int index,float type,float seed)
{
    float kind=floor(type+.5);
    float4 volume;
    if(kind<1.5)
    {
        // These two sphere sets retain the original broadleaf and pine artwork.
        float4 broadleaf=float4(0,0,15.5,6.8),pine=float4(0,0,7,10.3);
        if(index==1){broadleaf=float4(-5,-1,12,6.3);pine=float4(0,0,13,8);}
        if(index==2){broadleaf=float4(5,0,13.5,6.5);pine=float4(0,0,18,5.5);}
        if(index==3){broadleaf=float4(-1,5,15.5,6.8);pine=float4(0,0,22,3.6);}
        if(index==4){broadleaf=float4(1,-4.5,11,6.4);pine=float4(0,0,25,1.8);}
        broadleaf.x*=.86+seed*.28;
        broadleaf.z+=(seed-.5)*index*.8;
        float angle=floor(seed*4)*1.570796327;
        float sine=sin(angle),cosine=cos(angle);
        broadleaf.xy=float2(cosine*broadleaf.x-sine*broadleaf.y,sine*broadleaf.x+cosine*broadleaf.y);
        pine.x=(seed-.5)*index*.5;
        volume=lerp(broadleaf,pine,saturate(kind));
        volume.z+=12;
        return volume;
    }
    if(kind<2.5)
    {
        volume=float4(0,0,30,6);
        if(index==1)volume=float4(-2,-1,24,5);
        if(index==2)volume=float4(2,.2,25,5.2);
        if(index==3)volume=float4(0,3.5,29,4.4);
        if(index==4)volume=float4(0,-3.4,23,4.5);
    }
    else if(kind<3.5)
    {
        volume=float4(0,0,33,4.2);
        if(index==1)volume=float4(-6,0,32,6);
        if(index==2)volume=float4(6,.5,33,6);
        if(index==3)volume=float4(0,6,33.5,6);
        if(index==4)volume=float4(0,-6,31,6);
    }
    else if(kind<4.5)
    {
        volume=float4(0,0,25,7);
        if(index==1)volume=float4(-5,-1,25,5.8);
        if(index==2)volume=float4(5,0,26,5.6);
        if(index==3)volume=float4(0,4,25,6);
        if(index==4)volume=float4(1,-4,25,5.2);
    }
    else if(kind<5.5)
    {
        volume=float4(0,0,23,7);
        if(index==1)volume=float4(-5,0,19,6);
        if(index==2)volume=float4(5,0,20,6);
        if(index==3)volume=float4(-1,4,21,6);
        if(index==4)volume=float4(1,-5,17,5.8);
    }
    else if(kind<6.5)
    {
        volume=float4(0,0,11,4.2);
        if(index==1)volume=float4(-3.8,-.8,9,3.5);
        if(index==2)volume=float4(3.4,.6,10,3.8);
        if(index==3)volume=float4(-.7,2.7,11,3.6);
        if(index==4)volume=float4(.7,-3,9,3.4);
    }
    else
    {
        volume=float4(0,0,29,8.2);
        if(index==1)volume=float4(-5,-1.3,24,6.6);
        if(index==2)volume=float4(5,0,27,6.7);
        if(index==3)volume=float4(-1,5,29,7);
        if(index==4)volume=float4(1,-5,23,6.4);
    }
    float turns=kind>3.5&&kind<4.5?floor(seed*2)*2:floor(seed*4);
    float angle=turns*1.570796327;
    float sine=sin(angle),cosine=cos(angle);
    volume.xy=float2(cosine*volume.x-sine*volume.y,sine*volume.x+cosine*volume.y);
    volume.z+=(seed-.5)*index*.6;
    volume.w*=.94+seed*.12;
    return volume;
}

float3 PDTreeLobeScale(int index,float type,float seed)
{
    float kind=floor(type+.5);
    float3 axes=1;
    if(kind>1.5&&kind<2.5)axes=float3(.62,.83,1.4);
    else if(kind>2.5&&kind<3.5)
    {
        axes=float3(1,1,.52);
        if(index==1||index==2)axes=float3(1.05,.34,.30);
        if(index==3||index==4)axes=float3(.34,1.05,.30);
    }
    else if(kind>3.5&&kind<4.5)axes=float3(1.15,.72,.32);
    else if(kind>4.5&&kind<5.5)axes=index==0?float3(1,.9,.8):float3(.95,.85,1.25);
    else if(kind>5.5&&kind<6.5)axes=float3(1,.9,.8);
    else if(kind>6.5)axes=float3(1.02,1.02,.88);
    // The quarter-turn also rotates anisotropic fronds and narrow crowns.
    float turns=kind>3.5&&kind<4.5?floor(seed*2)*2:floor(seed*4);
    float swapAxes=frac(turns*.5)*2;
    axes.xy=lerp(axes.xy,axes.yx,swapAxes);
    return axes;
}

float4 PDTreeCrown(float2 pixel,float type,float seed,out float2 edge)
{
    float3 origin=float3(pixel.x,0,0)+PDTreeUp*pixel.y;
    float front=-100;
    float3 normal=0;
    edge=-100;
    [unroll] for(int lobe=0;lobe<5;lobe++)
    {
        float4 volume=PDTreeLobe(lobe,type,seed);
        float3 axes=volume.w*PDTreeLobeScale(lobe,type,seed);
        float3 relative=(origin-volume.xyz)/axes;
        float3 ray=PDTreeView/axes;
        float rayLengthSquared=dot(ray,ray);
        float depth=-dot(relative,ray)/rayLengthSquared;
        float3 closest=relative+ray*depth;
        float distanceSquared=dot(closest,closest);
        float lobeEdge=volume.w*(1-sqrt(distanceSquared));
        edge.x=max(edge.x,lobeEdge);
        if(distanceSquared<1)
        {
            float visibleDepth=depth+sqrt((1-distanceSquared)/rayLengthSquared);
            if(visibleDepth>front)
            {
                front=visibleDepth;
                normal=normalize((relative+ray*visibleDepth)/axes);
                edge.y=lobeEdge;
            }
        }
    }
    return float4(normal,step(-99,front));
}

float PDTreeCastShadow(float2 pixel,float3 sun,float type,float seed)
{
    float3 origin=float3(pixel.x,pixel.y/PDTreeUp.y,0);
    float3 lightRay=normalize(float3(sun.xy*.40,max(.55,sun.z)));
    float mask=0;
    [unroll] for(int lobe=0;lobe<5;lobe++)
    {
        float4 volume=PDTreeLobe(lobe,type,seed);
        float3 axes=volume.w*PDTreeLobeScale(lobe,type,seed);
        float3 relative=(volume.xyz-origin)/axes;
        float3 ray=lightRay/axes;
        float depth=max(0,dot(relative,ray)/dot(ray,ray));
        float distanceToRay=length(relative-ray*depth);
        mask=max(mask,1-smoothstep(1-.7/volume.w,1+1.5/volume.w,distanceToRay));
    }
    return mask;
}

float PDTreeTrunk(float2 pixel,float type,float seed)
{
    float kind=floor(type+.5);
    float width=1.5,height=9;
    if(kind>1.5&&kind<2.5){width=.9;height=12;}
    if(kind>2.5&&kind<3.5){width=.85;height=17;}
    if(kind>3.5&&kind<4.5){width=1.1;height=14;}
    if(kind>5.5&&kind<6.5){width=.8;height=3;}
    return PDBox(pixel,float2(-width,-1.5),float2(width,height));
}

float3 PDTreeBark(float2 pixel,float type,float seed)
{
    float kind=floor(type+.5);
    float3 bark=lerp(float3(.28,.22,.14),float3(.47,.35,.19),step(0,pixel.x));
    if(kind>1.5&&kind<2.5)
    {
        float band=step(.74,frac((pixel.y+seed*7)/3.5));
        float knot=band*step(.1,pixel.x*lerp(-1,1,step(.5,frac(pixel.y/7))));
        bark=lerp(float3(.71,.72,.60),float3(.24,.27,.24),knot);
    }
    if(kind>2.5&&kind<3.5)
        bark=lerp(float3(.40,.32,.19),float3(.61,.50,.29),step(.28,frac(pixel.y*.4+seed)));
    return bark;
}

float3 PDTreeFoliage(float3 normal,float2 pixel,float2 id,float type,float seed,float3 sun,float2 edge)
{
    float kind=floor(type+.5);
    float daylight=smoothstep(-.06,.15,sun.z);
    float3 key=normalize(lerp(float3(-.3,.4,1),sun,daylight));
    float cluster=PDHash(id+floor(pixel/2)*.071);
    float3 facet=normalize(floor(normal*4+.5)/4+float3(0,0,.001));
    float diffuse=saturate(dot(facet,key)*.70+facet.z*.10+.10);
    float tone=floor(saturate(diffuse+(cluster-.5)*.08)*4+.15)/4;
    float3 dark=float3(.115,.23,.13),middle=float3(.24,.39,.18),bright=float3(.43,.57,.26);
    if(kind>.5&&kind<1.5){dark=float3(.10,.23,.20);middle=float3(.20,.36,.28);bright=float3(.38,.52,.36);}
    if(kind>1.5&&kind<2.5){dark=float3(.19,.29,.11);middle=float3(.40,.52,.20);bright=float3(.65,.72,.33);}
    if(kind>2.5&&kind<3.5){dark=float3(.06,.24,.16);middle=float3(.15,.43,.28);bright=float3(.39,.63,.40);}
    if(kind>3.5&&kind<4.5){dark=float3(.22,.27,.12);middle=float3(.40,.44,.23);bright=float3(.61,.60,.35);}
    if(kind>4.5&&kind<5.5){dark=float3(.17,.30,.21);middle=float3(.34,.49,.35);bright=float3(.57,.67,.47);}
    if(kind>5.5&&kind<6.5){dark=float3(.24,.29,.18);middle=float3(.42,.46,.31);bright=float3(.62,.62,.44);}
    if(kind>6.5){dark=float3(.055,.19,.08);middle=float3(.13,.35,.14);bright=float3(.29,.53,.22);}
    float3 foliage=lerp(lerp(dark,middle,saturate(tone*2)),bright,saturate(tone*2-1));
    foliage*=lerp(float3(1.05,.99,.91),float3(.91,1.03,1.02),seed);
    float crease=(1-smoothstep(.6,2.2,edge.y))*step(1.4,edge.x);
    foliage*=1-crease*.19;
    float rim=1-step(.65,edge.x);
    return lerp(foliage,dark*.85,rim*.65);
}

#endif
