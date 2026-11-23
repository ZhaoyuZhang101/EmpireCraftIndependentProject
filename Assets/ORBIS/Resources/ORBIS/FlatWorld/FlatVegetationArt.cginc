#ifndef ORBIS_FLAT_VEGETATION_ART
#define ORBIS_FLAT_VEGETATION_ART

// Presentation only. Crowns and shadows share their geometry in the orthographic
// 72-degree pixel-art view. Palms use bowed ribbons, other species use ellipsoids.
// 0 broadleaf, 1 pine, 2 birch, 3 palm, 4 acacia, 5 willow, 6 shrub, 7 tropical.
static const float3 PDTreeView=float3(0,-.30901699,.95105652);
static const float3 PDTreeUp=float3(0,.95105652,.30901699);

bool PDTreeInDrawBounds(float2 pixel)
{
    return abs(pixel.x)<=21&&pixel.y>=-14&&pixel.y<=40;
}

float3 PDTreeWindPoint(float3 position,float2 wind,float type)
{
    float kind=floor(type+.5);
    float flexibility=.9;
    if(kind==1||kind==4)flexibility=.65;
    if(kind==3||kind==5)flexibility=1.2;
    if(kind==6)flexibility=.75;
    float height=saturate(position.z/(kind==6?14:36));
    float weight=height*height*flexibility;
    position.xy+=wind*weight;
    // A flexible palm stem also lowers its fan in a strong gust.
    position.z-=dot(wind,wind)*(kind==3?.86:.06)*weight;
    return position;
}

float2 PDTreeBranchCycle(float seed)
{
    #ifdef ORBIS_FLAT_VEGETATION_WIND
    float2 cycle;
    sincos(_SkyClock*2.18+seed*6.283185307,cycle.y,cycle.x);
    return cycle;
    #else
    return 0;
    #endif
}

float3 PDTreeBranchBend(float2 wind,float2 cycle,float2 axis)
{
    float swing=dot(cycle,axis),twist=dot(cycle,float2(-axis.y,axis.x));
    return float3(wind*(.66*swing)+float2(-wind.y,wind.x)*(.31*twist),
                  dot(wind,axis)*(.37*twist));
}

float3 PDPalmRoot(float seed)
{
    return float3((seed-.5)*3.6,sin(seed*6.283185307)*.6,31+seed*1.5);
}

// Seven tapered fronds share their 3D control points, feathered silhouette and
// surface normals between camera and sunlight projection. Two ribbon segments
// per frond keep the geometry bounded at every zoom level.
float4 PDPalmCrown(float2 pixel,float seed,float3 projectionX,float3 projectionY,float2 wind,out float2 edge,out float3 surface)
{
    float3 root=PDPalmRoot(seed),movingRoot=PDTreeWindPoint(root,wind,3),normal=0;
    float2 cycle=PDTreeBranchCycle(seed);
    float front=-100;
    edge=-100;
    surface=0;
    [loop] [fastopt] for(int frond=0;frond<7;frond++)
    {
        float variation=frac(seed*13+frond*.371);
        float angle=seed*6.283185307+frond*.897597901+(frac(seed*17+frond*.73)-.5)*.32;
        float3 direction=float3(cos(angle),sin(angle),0);
        float3 side=float3(-direction.y,direction.x,0);
        float frondLength=9.3+variation*2.1;
        float3 bend=root+direction*(frondLength*.35)+side*((variation-.5)*1.2)+float3(0,0,8+variation*2);
        float3 tip=root+direction*frondLength+side*((variation-.5)*.6)-float3(0,0,4+frac(seed*7+frond*.61)*2.7);
        float3 flutter=PDTreeBranchBend(wind,cycle,direction.xy);
        bend=PDTreeWindPoint(bend,wind,3)+flutter*.35;
        tip=PDTreeWindPoint(tip,wind,3)+flutter*.95;
        tip.xy+=wind*(.26+variation*.16);
        tip.z+=dot(wind,direction.xy)*.25;
        float3 middle=movingRoot*.25+bend*.5+tip*.25;
        float2 across=float2(dot(side,projectionX),dot(side,projectionY));
        [unroll] for(int segment=0;segment<2;segment++)
        {
            float3 start=lerp(movingRoot,middle,segment),end=lerp(middle,tip,segment);
            float2 start2=float2(dot(start,projectionX),dot(start,projectionY));
            float2 delta=float2(dot(end-start,projectionX),dot(end-start,projectionY));
            float determinant=delta.x*across.y-delta.y*across.x;
            if(abs(determinant)<.0001)continue;
            float2 offset=pixel-start2;
            float along=(offset.x*across.y-offset.y*across.x)/determinant;
            float lateral=(delta.x*offset.y-delta.y*offset.x)/determinant;
            float t=(segment+saturate(along))*.5;
            float feather=abs(frac(t*6.5+step(0,lateral)*.38)-.5)*2;
            float width=.55*(1-t)+(1.7+variation*.35)*4*t*(1-t)*(.35+.65*feather);
            float projectedLength=max(.001,length(delta));
            float sideEdge=(width-abs(lateral))*abs(determinant)/projectedLength;
            float frondEdge=min(sideEdge,min(along,1-along)*projectedLength);
            // Root and tip form the outer end caps of the continuous frond.
            float outerEnd=lerp(along,1-along,segment)*projectedLength;
            float withinSegment=step(0,along)*step(along,1);
            float outlineEdge=lerp(frondEdge,min(sideEdge,outerEnd),withinSegment);
            edge.x=max(edge.x,outlineEdge);
            if(frondEdge>=0)
            {
                float3 leafPoint=lerp(start,end,along)+side*lateral;
                float depth=dot(leafPoint,PDTreeView);
                if(depth>front)
                {
                    front=depth;
                    float3 tangent=lerp(bend-movingRoot,tip-bend,t);
                    float fold=lateral/max(.55,width);
                    normal=normalize(normalize(cross(tangent,side))+side*(fold*.48));
                    normal*=dot(normal,PDTreeView)<0?-1:1;
                    surface=float3(t,fold,0);
                    edge.y=outlineEdge;
                }
            }
        }
    }
    return float4(normal,step(-99,front));
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
    float climate=floor(info.a+.5);
    bool subarctic=climate==10||climate==13||climate==16||
        (climate>=17&&climate<=28&&fmod(climate-17,4)>=2);
    if(subarctic&&cover>4.5&&cover<9.5)return seed<.14?2:1;
    if(climate>=29)return 6;
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
    if(cover<9.5)
    {
        if(!tropical)return seed>.6?1:0;
        return seed<(shore>.4?.48:.18)?3:7;
    }
    if(tropical&&seed<.28)return 3;
    return seed<.75?5:6;
}
float PDTreeSpecies(float4 ecology,float4 info,float shore,float derivedTree)
{
    if(derivedTree>=0)return derivedTree;
    return PDTreeSpecies(ecology,info,shore);
}

float2 PDTreeQuarterTurn(float2 offset,float turns)
{
    float2 rotated=lerp(offset,float2(-offset.y,offset.x),frac(turns*.5)*2);
    return frac(turns*.25)>=.5?-rotated:rotated;
}

float4 PDTreeLobe(int index,float kind,float seed)
{
    float4 volume;
    if(kind==0)
    {
        volume=float4(0,0,29,6.1);
        if(index==1)volume=float4(-5.4,-1.5,24.5,5.6);
        if(index==2)volume=float4(5.5,0,25.5,5.8);
        if(index==3)volume=float4(-1.3,5.5,27.5,5.8);
        if(index==4)volume=float4(1.5,-5,23,5.6);
        volume.x*=.86+seed*.28;
        volume.z+=(seed-.5)*index*.8;
        volume.xy=PDTreeQuarterTurn(volume.xy,floor(seed*4));
        return volume;
    }
    if(kind==1)
    {
        // Crossing sprays at different heights narrow to a raised central leader.
        volume=float4(0,0,19,10.3);
        if(index==1)volume=float4(.3,-.2,21,9.7);
        if(index==2)volume=float4(-.3,.3,24,8.7);
        if(index==3)volume=float4(.4,.1,28,6.6);
        if(index==4)volume=float4(0,.2,33,3.7);
        volume.x+=(seed-.5)*index*.38;
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
        if(index==3)volume=float4(-1,5,26,7);
        if(index==4)volume=float4(1,-5,23,6.4);
    }
    float turns=kind>3.5&&kind<4.5?floor(seed*2)*2:floor(seed*4);
    volume.xy=PDTreeQuarterTurn(volume.xy,turns);
    volume.z+=(seed-.5)*index*.6;
    volume.w*=.94+seed*.12;
    return volume;
}

float3 PDTreeLobeScale(int index,float kind,float seed)
{
    float3 axes=1;
    if(kind==1)return index==4?float3(.82,.61,1.08):float3(1,.46,.42);
    if(kind>1.5&&kind<2.5)axes=float3(.62,.83,1.4);
    else if(kind>3.5&&kind<4.5)axes=float3(1.15,.72,.32);
    else if(kind>4.5&&kind<5.5)axes=index==0?float3(1,.9,.8):float3(.95,.85,1.25);
    else if(kind>5.5&&kind<6.5)axes=float3(1,.9,.8);
    else if(kind>6.5)axes=float3(1.02,1.02,.88);
    // Quarter turns also rotate anisotropic crowns.
    float turns=kind>3.5&&kind<4.5?floor(seed*2)*2:floor(seed*4);
    float swapAxes=frac(turns*.5)*2;
    axes.xy=lerp(axes.xy,axes.yx,swapAxes);
    return axes;
}

float2 PDPineHeading(float kind,float seed)
{
    float2 heading=float2(1,0);
    if(kind==1)sincos(seed*3.141592654,heading.y,heading.x);
    return heading;
}

float4 PDTreeMovingLobe(int lobe,float kind,float seed,float foliage,float2 wind,float2 cycle,float2 pineHeading,out float2 axis,out float3 axes)
{
    float4 volume=PDTreeLobe(lobe,kind,seed);
    // Mature broadleaf trees spread sideways into neighbouring crowns. Their
    // lower masses gain width without raising the shadow-casting height.
    float spread=1;
    if(kind==0)spread=1.26+.12*seed;
    if(kind==7)spread=1.33+.10*seed;
    volume.xy*=spread;
    if(lobe!=0)volume.z-=(spread-1)*19;
    float2 branch=float2(cos(lobe*2.39996323),sin(lobe*2.39996323));
    float flexibility=kind==1||kind==4?.64:(kind==5?1.16:.92);
    volume.xyz=PDTreeWindPoint(volume.xyz,wind,kind)+PDTreeBranchBend(wind,cycle,branch)*(lobe==0?.32:flexibility);
    volume.w*=lerp(.48,1,saturate(foliage));
    axes=volume.w*PDTreeLobeScale(lobe,kind,seed);
    // The raised central foliage stays compact above the spreading outer limbs.
    axes.xy*=lobe==0?1:spread;
    axis=float2(1,0);
    if(kind==1)
    {
        float2 turn=float2(cos(lobe*1.17),sin(lobe*1.17));
        axis=pineHeading*turn.x+float2(-pineHeading.y,pineHeading.x)*turn.y;
    }
    return volume;
}

float3 PDTreeToLobe(float3 offset,float2 axis)
{
    return float3(dot(offset.xy,axis),dot(offset.xy,float2(-axis.y,axis.x)),offset.z);
}

float4 PDTreeCrown(float2 pixel,float type,float seed,float foliage,float2 wind,out float2 edge,out float3 surface)
{
    float4 crown=0;
    edge=-100;
    surface=0;
    float kind=floor(type+.5);
    if(kind==3)crown=PDPalmCrown(pixel,seed,float3(1,0,0),PDTreeUp,wind,edge,surface);
    else
    {
        float3 origin=float3(pixel.x,0,0)+PDTreeUp*pixel.y;
        float2 cycle=PDTreeBranchCycle(seed),pineHeading=PDPineHeading(kind,seed);
        float front=-100;
        float3 normal=0;
        [unroll] for(int lobe=0;lobe<5;lobe++)
        {
            float2 axis;float3 axes;
            float4 volume=PDTreeMovingLobe(lobe,kind,seed,foliage,wind,cycle,pineHeading,axis,axes);
            float3 relative=PDTreeToLobe(origin-volume.xyz,axis)/axes;
            float3 ray=PDTreeToLobe(PDTreeView,axis)/axes;
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
                    float3 hit=relative+ray*visibleDepth;
                    normal=normalize(PDTreeToLobe(hit/axes,float2(axis.x,-axis.y)));
                    float3 offset=hit*axes;
                    surface.xy=kind==1?offset.xy:offset.xy+offset.z*float2(.24,.31)+lobe*float2(2,1.7);
                    // Wide deciduous and tropical crowns shelter their low boughs.
                    surface.z=kind==0||kind==7?clamp((volume.z-25)*.009,-.10,.04)*saturate(foliage):0;
                    edge.y=lobeEdge;
                }
            }
        }
        crown=float4(normal,step(-99,front));
    }
    return crown;
}
float4 PDTreeCrown(float2 pixel,float type,float seed,float foliage,float2 wind,out float2 edge)
{
    float3 surface;
    return PDTreeCrown(pixel,type,seed,foliage,wind,edge,surface);
}
float4 PDTreeCrown(float2 pixel,float type,float seed,float foliage,out float2 edge)
{
    return PDTreeCrown(pixel,type,seed,foliage,0,edge);
}
float4 PDTreeCrown(float2 pixel,float type,float seed,out float2 edge)
{
    return PDTreeCrown(pixel,type,seed,1,edge);
}

float PDTreeCastShadow(float2 pixel,float3 sun,float type,float seed,float foliage,float2 wind)
{
    float3 origin=float3(pixel.x,pixel.y/PDTreeUp.y,0);
    float3 lightRay=normalize(float3(sun.xy*.40,max(.55,sun.z)));
    float mask=0;
    float kind=floor(type+.5);
    if(kind==3)
    {
        float2 edge;float3 surface;
        PDPalmCrown(origin.xy,seed,float3(1,0,-lightRay.x/lightRay.z),float3(0,1,-lightRay.y/lightRay.z),wind,edge,surface);
        mask=smoothstep(-.55,.55,edge.x);
    }
    else
    {
        float2 cycle=PDTreeBranchCycle(seed),pineHeading=PDPineHeading(kind,seed);
        [unroll] for(int lobe=0;lobe<5;lobe++)
        {
            float2 axis;float3 axes;
            float4 volume=PDTreeMovingLobe(lobe,kind,seed,foliage,wind,cycle,pineHeading,axis,axes);
            float3 relative=PDTreeToLobe(volume.xyz-origin,axis)/axes;
            float3 ray=PDTreeToLobe(lightRay,axis)/axes;
            float depth=max(0,dot(relative,ray)/dot(ray,ray));
            float distanceToRay=length(relative-ray*depth);
            mask=max(mask,1-smoothstep(1-.7/volume.w,1+1.5/volume.w,distanceToRay));
        }
    }
    return mask;
}
float PDTreeCastShadow(float2 pixel,float3 sun,float type,float seed,float foliage)
{
    return PDTreeCastShadow(pixel,sun,type,seed,foliage,0);
}
float PDTreeCastShadow(float2 pixel,float3 sun,float type,float seed)
{
    return PDTreeCastShadow(pixel,sun,type,seed,1);
}

float PDTreeTrunk(float2 pixel,float type,float seed,float2 wind)
{
    float kind=floor(type+.5);
    float trunk=0;
    if(kind>2.5&&kind<3.5)
    {
        float3 root=PDTreeWindPoint(PDPalmRoot(seed),wind,type);
        float top=dot(root,PDTreeUp);
        float t=saturate(pixel.y/top);
        float centre=root.x*t*t;
        trunk=step(-1.5,pixel.y)*step(pixel.y,top+.5)*step(abs(pixel.x-centre),lerp(1.05,.65,t));
    }
    else
    {
        float width=1.5,height=9;
        if(kind>1.5&&kind<2.5){width=.9;height=12;}
        if(kind>3.5&&kind<4.5){width=1.1;height=14;}
        if(kind>5.5&&kind<6.5){width=.8;height=3;}
        height*=PDTreeUp.z/.46947156;
        float3 bend=PDTreeWindPoint(float3(0,0,max(0,pixel.y)/PDTreeUp.z),wind,type);
        pixel-=float2(bend.x,bend.y*PDTreeUp.y);
        trunk=PDBox(pixel,float2(-width,-1.5),float2(width,height));
    }
    return trunk;
}
float PDTreeTrunk(float2 pixel,float type,float seed)
{
    return PDTreeTrunk(pixel,type,seed,0);
}

float3 PDTreeBark(float2 pixel,float type,float seed,float2 wind)
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
    {
        float3 root=PDTreeWindPoint(PDPalmRoot(seed),wind,type);
        float t=saturate(pixel.y/dot(root,PDTreeUp));
        float lit=step(root.x*t*t,pixel.x);
        bark=lerp(float3(.37,.28,.16),float3(.60,.48,.29),lit);
        bark*=lerp(1,.82,step(.76,frac(pixel.y*.45+seed)));
    }
    return bark;
}
float3 PDTreeBark(float2 pixel,float type,float seed)
{
    return PDTreeBark(pixel,type,seed,0);
}

// Material coordinates belong to the visible branch. A moving leaf carries its
// highlight and its fold across the logical pixel grid with it.
float PDTreeLeafRelief(float2 surface,float kind,float seed)
{
    if(kind==1)
    {
        // Oblique needles leave a central spine along each tier.
        float rib=frac((abs(surface.x)+abs(surface.y)*1.55)*.275+seed);
        return (smoothstep(.48,.81,rib)-.42)*.19+(1-smoothstep(.22,1.05,abs(surface.y)))*.08;
    }
    if(kind==3)
    {
        float leaflet=frac(surface.x*6.5-abs(surface.y)*.55+step(0,surface.y)*.38);
        float midrib=1-smoothstep(.07,.31,abs(surface.y));
        return midrib*.19+(smoothstep(.50,.82,leaflet)-.5)*.15;
    }
    if(kind==5)
    {
        // Long hanging sprays are carried by their own branch.
        float strand=frac(surface.x*.34+surface.y*.13+seed*2);
        return (smoothstep(.44,.82,strand)-.5)*.20;
    }
    float2 scale=kind==2?float2(.39,.52):float2(.29,.40);
    if(kind==4)scale=float2(.25,.53);
    if(kind==7)scale=float2(.23,.32);
    float2 tile=surface*scale+seed*float2(2.7,4.1);
    tile.x+=floor(tile.y)*.5;
    float2 leaf=frac(tile)-.5;
    float arch=abs(leaf.x)*1.15+abs(leaf.y+.10)*.83;
    return (.48-smoothstep(.25,.62,arch))*.23;
}

float3 PDTreeFoliage(float3 normal,float3 surface,float type,float seed,float3 sun,float2 edge)
{
    float kind=floor(type+.5);
    float daylight=smoothstep(-.06,.15,sun.z);
    float3 key=normalize(lerp(float3(-.3,.4,1),sun,daylight));
    float3 facet=normalize(floor(normal*4+.5)/4+float3(0,0,.001));
    float diffuse=dot(facet,key)*.72+facet.z*.10+.12;
    float tone=floor(saturate(diffuse+surface.z+PDTreeLeafRelief(surface.xy,kind,seed))*5+.18)/5;
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
    foliage*=1-crease*.24;
    float rim=1-step(.65,edge.x);
    return lerp(foliage,dark*.85,rim*.65);
}

#endif
