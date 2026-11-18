#ifndef ORBIS_FLAT_GROUND_COVER
#define ORBIS_FLAT_GROUND_COVER

float4 PDGroundPlantLayout(float variation,float growth,float cover)
{
 bool wetland=cover>9.5&&cover<10.5;
 // Leave room for the entire bent silhouette inside an eight-pixel clump.
 float2 anchor=float2(3+(wetland?0:floor(variation*2)),1+frac(variation*7)*.25);
 return float4(anchor,1+floor((2+variation*2)*growth),2+floor((1+variation*3)*growth));
}

float3 PixelGround(float3 baseColor,float2 cell,float4 field,float waterMask,float2 heightGradient,float4 ecology)
{
 float2 id=floor(cell),uv=(id+.5)*_MapSize.zw;
 id.x-=floor(id.x/_MapSize.x)*_MapSize.x;
 float latitudeScale=FlatArtworkLatitudeScale(uv.y);
 float2 q=floor(float2(frac(cell.x)*32,16+(frac(cell.y)-.5)*32/latitudeScale));
 float3 artwork=FlatDetailPosition(cell);
 float grain=FlatDetailNoise(artwork*23);
 float grainFoot=max(length(ddx(artwork*23)),length(ddy(artwork*23)));
 float grainWeight=1-smoothstep(.45,1.4,grainFoot);
 float3 col=floor(baseColor*64+.5)/64*(1+(grain-.5)*.055*grainWeight);

 // Existing relief follows the actual elevation gradient and remains continuous
 // across tile edges. Ground artwork adds no elevation or simulation cells.
 float slope=length(heightGradient);
 float weathered=FlatDetailNoise(artwork*3.2+29);
 float face=smoothstep(180,650,slope)*smoothstep(.30,.70,weathered)*(1-smoothstep(.4,.85,field.a));
 face*=step(.01,field.r)*(1-waterMask)*step(.20,-field.g);
 float strata=step(.42,FlatDetailNoise(artwork*24));
 float3 rock=lerp(float3(.36,.35,.29),float3(.53,.50,.41),strata*.35+weathered*.65);
 float weathering=lerp(.5,.15+.85*smoothstep(.22,.75,grain),grainWeight);
 col=PDPaint(col,rock,face*weathering*.30);
 if(waterMask>.99)return col;

 float cover=floor(ecology.r+.5);
 if(cover<-.5)cover=field.a>.5?6:1;
 if(cover>12.5)return col;
 float wetness=saturate(ecology.b);
 float2 artDx,artDy;FlatCellScreenDerivatives(cell,artDx,artDy);
 artDx*=float2(32,32/latitudeScale);artDy*=float2(32,32/latitudeScale);
 float detail=1-smoothstep(.65,1.8,max(length(artDx),length(artDy)));
 detail*=(1-waterMask)*(1-face*.8)*smoothstep(.12,.45,-field.g);
 if(detail<.001)return col;
 float growth=FlatVegetationSeason(cell).x;

 float patch=FlatDetailNoise(artwork*3.7+91);
 float forest=step(4.5,cover)*step(cover,9.5);
 float moss=forest*smoothstep(.45,.85,wetness)*smoothstep(.38,.70,patch);
 float litter=forest*(1-moss)*smoothstep(.56,.76,grain);
 col=PDPaint(col,float3(.27,.39,.21),moss*.18*detail);
 col=PDPaint(col,lerp(float3(.37,.31,.20),float3(.49,.42,.25),grain),litter*.13*detail);

 // Small clumps use a local art frame while their distribution stays fixed in
 // geographic coordinates. Each clump has a separate position and palette.
 float2 cluster=floor(q/8),p=q-cluster*8;
 float seed=PDHash(id+cluster*.127+ecology.a*23);
 float variation=PDHash(id+cluster*.213+ecology.a*41+71);
 float2 anchor=float2(1+floor(variation*5),1+floor(frac(variation*7)*2));
 p-=anchor;
 float2 groundPoint=p;
 float4 plant=PDGroundPlantLayout(variation,growth,cover);
 float height=plant.z;
 p+=anchor-plant.xy+.5;
 float windHeight=cover>9.5&&cover<10.5?plant.w:height+1;
 float bend=saturate(p.y/max(1,windHeight));
 float2 wind=PDVegetationWind(id,variation);
 p-=float2(wind.x*.8,wind.y*.35)*bend*bend;
 float stems=PDBox(p,float2(0,0),float2(1,height));
 stems=max(stems,PDBox(p,float2(-1,height-2),float2(0,height)));
 stems=max(stems,PDBox(p,float2(1,1),float2(2,height-1)));
 float density=forest>.5?.24:.48;
 if(cover<.5)density=.10;
 else if(cover>1.5&&cover<4.5)density=.28;
 else if(cover>10.5)density=.28;
 density*=lerp(.35,1,smoothstep(.25,.68,patch));
 density*=lerp(.18,1,growth);
 float3 grass=lerp(float3(.35,.43,.20),float3(.24,.41,.23),wetness);
 if(cover<.5)grass=float3(.47,.43,.28);
 if(cover>1.5&&cover<4.5)grass=lerp(float3(.46,.43,.23),float3(.52,.51,.28),variation);
 if(cover>10.5)grass=lerp(float3(.41,.47,.31),float3(.55,.55,.38),variation);
 grass=lerp(float3(.48,.40,.24),grass,growth);
 col=PDPaint(col,grass*.90,stems*step(seed,density)*detail*.58);
 col=PDPaint(col,grass*1.13,PDBox(p,float2(0,height-1),float2(1,height))*step(seed,density)*detail);

 float meadow=(1-step(.5,abs(cover-1)))+(1-step(.5,abs(cover-11)));
 float flowerChoice=PDHash(id+cluster*.329+ecology.a*29+101);
 float flowers=meadow*step(flowerChoice,.13)*step(seed,density)*smoothstep(.22,.60,wetness)*growth*growth;
 float blossom=PDBox(p,float2(-1,height),float2(2,height+1));
 blossom=max(blossom,PDBox(p,float2(0,height-1),float2(1,height+2)));
 float3 petal=lerp(float3(.84,.79,.59),float3(.81,.65,.22),step(.33,variation));
 petal=lerp(petal,float3(.61,.51,.66),step(.72,variation));
 col=PDPaint(col,petal,blossom*flowers*detail*.9);

 if(cover>9.5&&cover<10.5)
 {
  float reedHeight=plant.w;
  float reeds=PDBox(p,float2(0,0),float2(1,reedHeight));
  reeds=max(reeds,PDBox(p,float2(2,0),float2(3,reedHeight-1)));
  reeds=max(reeds,PDBox(p,float2(-1,1),float2(0,reedHeight-2)));
  float3 reedColor=lerp(float3(.47,.40,.25),lerp(float3(.24,.35,.20),float3(.43,.48,.23),variation),growth);
  col=PDPaint(col,reedColor,reeds*step(seed,.80)*detail);
  float heads=PDBox(p,float2(0,reedHeight-1),float2(1,reedHeight+1));
  heads=max(heads,PDBox(p,float2(2,reedHeight-2),float2(3,reedHeight)));
  col=PDPaint(col,float3(.37,.29,.17),heads*step(seed,.80)*detail);
 }

 p=groundPoint;
 float stone=PDBox(p,float2(-1,0),float2(2,1));
 float stoneSide=PDBox(p,float2(0,-1),float2(2,0));
 float pebbles=step(.92,seed)*(1-forest*.65)*detail;
 col=PDPaint(col,float3(.34,.33,.28),stoneSide*pebbles);
 col=PDPaint(col,lerp(float3(.51,.49,.40),float3(.60,.57,.46),variation),stone*pebbles);
 return col;
}
#endif
