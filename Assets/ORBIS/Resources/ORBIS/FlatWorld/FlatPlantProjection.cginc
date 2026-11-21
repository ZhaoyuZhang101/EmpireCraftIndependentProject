#ifndef ORBIS_FLAT_PLANT_PROJECTION
#define ORBIS_FLAT_PLANT_PROJECTION
struct PDPlantView
{
 float2 projected;
 float weight,chartPerPixel;
 #ifdef ORBIS_ATLAS_PLANTS
 float2 chartUV;
 float2 cellsRight,cellsUp;
 #endif
};
PDPlantView PDGroundView()
{
 PDPlantView v=(PDPlantView)0;return v;
}

#if defined(ORBIS_LOCAL_PLANTS) || defined(ORBIS_ATLAS_PLANTS)
UNITY_DECLARE_TEX2D_NOSAMPLER(_PlantHeightTex);
float4 _PlantLattice; // sectors, vertex rows, inverse dimensions
#endif
#ifdef ORBIS_LOCAL_PLANTS
float PDVertexHeight(float sector,float ring)
{
 return FlatPresentationSample(_PlantHeightTex,(float2(sector,ring)+.5)*_PlantLattice.zw,0).r;
}
float PDRenderedRootHeight(float2 chart)
{
 // Same two triangles as FlatGlobeGeometry.TryLatticeTriangle. The vertex
 // shader's clip-W makes height affine in the undisplaced chart coordinates.
 float angle=frac(OrbisGeographicAtan2(chart.y,chart.x)/6.283185307)*_PlantLattice.x;
 float sector=floor(angle),arc=6.283185307*_PlantLattice.z;
 float radial=length(chart)*(_PlantLattice.y-1)/_LocalRadius;
 float2 uv=sin(float2(1-frac(angle),frac(angle))*arc)*(radial/sin(arc));
 float distance=min(uv.x+uv.y,_PlantLattice.y-1);
 float ring=min(floor(distance),_PlantLattice.y-2),outer=distance-ring;
 bool lower=ring>0&&uv.y<=ring*(1-outer);
 float innerLeft=lower?uv.y/max(1,ring):1-outer;
 float outerRight=lower?outer:saturate(uv.x/(ring+1));
 float edgeHeight=innerLeft*PDVertexHeight(sector+1,ring)+outerRight*PDVertexHeight(sector,ring+1);
 float last=lower?PDVertexHeight(sector,ring):PDVertexHeight(sector+1,ring+1);
 return edgeHeight+(1-innerLeft-outerRight)*last;
}
PDPlantView PDLocalPlantView(float2 projected,float2 chart)
{
 PDPlantView view=PDGroundView();
 if(_PlantLattice.x<=0||_ScrollRelief.w<=0)return view;
 view.weight=smoothstep(.75,1,_ScrollMorph);
 view.projected=projected;
 if(_ScrollMorph<1)view.projected=FlatReliefPosition(chart,PDRenderedRootHeight(chart)).xy;
 view.chartPerPixel=max(1e-9,max(length(ddx(view.projected)),length(ddy(view.projected))));
 return view;
}
#endif

#ifdef ORBIS_ATLAS_PLANTS
float2 PDCellDelta(float2 uv,float2 reference)
{
 float2 delta=uv-reference;
 delta.x-=round(delta.x);
 return delta*_MapSize.xy;
}
float PDAtlasVertex(float2 index)
{
 float2 uv=MarkleyFold(index/(_PlantLattice.xy-1));
 return FlatPresentationSample(_PlantHeightTex,(uv*(_PlantLattice.xy-1)+.5)*_PlantLattice.zw,0).r;
}
float PDAtlasRootHeight(float2 chart)
{
 float2 p=chart*(_PlantLattice.xy-1),base=floor(p),t=frac(p);
 float h10=PDAtlasVertex(base+float2(1,0)),h01=PDAtlasVertex(base+float2(0,1));
 if(t.x+t.y<=1)return PDAtlasVertex(base)*(1-t.x-t.y)+h10*t.x+h01*t.y;
 return h10*(1-t.y)+h01*(1-t.x)+PDAtlasVertex(base+1)*(t.x+t.y-1);
}
PDPlantView PDAtlasPlantView(float2 projected,float2 chart)
{
 PDPlantView view=PDGroundView();
 if(_PlantLattice.x<=0||_ScrollRelief.w<=0)return view;
 float2 units=_ProjectionSize.xy*(6.283185307*_MapSize.z);
 float2 stepUV=.20*_MarkleyTableSize.zw,geo=MarkleyGeoUV(chart);
 view.cellsRight=PDCellDelta(MarkleyGeoUV(chart+float2(stepUV.x,0)),geo)/(stepUV.x*units.x);
 view.cellsUp=PDCellDelta(MarkleyGeoUV(chart+float2(0,stepUV.y)),geo)/(stepUV.y*units.y);
 float determinant=view.cellsRight.x*view.cellsUp.y-view.cellsRight.y*view.cellsUp.x;
 if(abs(determinant)<1e-5)return view;
 view.chartUV=chart;view.weight=smoothstep(.75,1,_ScrollMorph);
 view.projected=projected;
 if(_ScrollMorph<1)view.projected=FlatReliefPosition((chart*_ProjectionSize.xy-_ScrollPan.xy)*(6.283185307*_MapSize.z),PDAtlasRootHeight(chart)).xy;
 view.chartPerPixel=max(1e-9,max(length(ddx(view.projected)),length(ddy(view.projected))));
 return view;
}
float2 PDChartOffset(PDPlantView view,float2 cells)
{
 float2 x=view.cellsRight,y=view.cellsUp;
 return float2(y.y*cells.x-y.x*cells.y,x.x*cells.y-x.y*cells.x)/(x.x*y.y-x.y*y.x);
}
#endif

float PDPlantArtSize(PDPlantView view,float latitudeScale)
{
 #ifdef ORBIS_ATLAS_PLANTS
 if(view.weight>0)return max(1e-10,length(PDChartOffset(view,float2(1./32,0))));
 #endif
 return 6.283185307*_MapSize.z*latitudeScale/32*(1+.25*dot(view.projected,view.projected));
}

float2 PDPlantPixel(PDPlantView view,float2 cell,float2 root,float latitudeScale)
{
 float2 ground=(cell-root)*float2(32,32/latitudeScale);
 #ifdef ORBIS_LOCAL_PLANTS
 if(view.weight>0)
 {
  float2 angles=(root*_MapSize.zw-.5)*float2(6.283185307,3.141592654);
  float2 s,c;sincos(angles,s,c);
  float3 radial=float3(s.x*c.y,s.y,c.x*c.y),east=float3(c.x,0,-s.x);
  float scale=2/max(.01,1+dot(radial,_LocalCenter));
  float2 chart=float2(dot(radial,_LocalEast),dot(radial,_LocalNorth))*scale;
  float height=PDRenderedRootHeight(chart);
  float2 rootPosition=FlatReliefPosition(chart,height).xy;
  // The stereographic chart is conformal. Transport its east/north frame at
  // the root, then apply the root's perspective magnification to both axes.
  float2 right=float2(dot(east,_LocalEast),dot(east,_LocalNorth))-.5*chart*dot(east,_LocalCenter);
  float2 offset=(view.projected-rootPosition)*(32*FlatReliefClipWeight(height,1)/
                (scale*max(.000001,c.y)*6.283185307*_MapSize.z));
  float2 rigid=float2(dot(offset,right),dot(offset,float2(-right.y,right.x)));
  ground=lerp(ground,rigid,view.weight);
 }
 #elif defined(ORBIS_ATLAS_PLANTS)
 if(view.weight>0)
 {
  float unit=6.283185307*_MapSize.z;
  float2 units=_ProjectionSize.xy*unit;
  float2 chart=view.chartUV+PDChartOffset(view,root-cell)/units;
  // Refine the source on the same continuous Markley sheet as the receiver.
  chart-=PDChartOffset(view,PDCellDelta(MarkleyGeoUV(chart),root*_MapSize.zw))/units;
  float height=PDAtlasRootHeight(chart);
  float2 origin=FlatReliefPosition((chart*_ProjectionSize.xy-_ScrollPan.xy)*unit,height).xy;
  float2 offset=(view.projected-origin)*FlatReliefClipWeight(height,1);
  float2 rigid=(view.cellsRight*offset.x+view.cellsUp*offset.y)*float2(32,32/latitudeScale);
  ground=lerp(ground,rigid,view.weight);
 }
 #endif
 return ground;
}
#endif
