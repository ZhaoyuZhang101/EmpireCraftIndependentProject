   // Map readability (globals, 0 = off): minimum on-screen width of main
   // channels when zoomed out, and a clear-water tint for river channels.
   float _RiverTint;
#ifdef ORBIS_LOCAL_SURFACE
   float4 _LocalFocus,_LocalEditFootprint;
   float FlatFocusCorners(float2 pixels,float radius)
   {
    float2 p=abs(pixels);
    float arm=clamp(radius*.5,3,9);
    float end=saturate(min(p.x,p.y)-(radius-arm)+.5);
    return end*saturate(1.4-abs(max(p.x,p.y)-radius));
   }
   float FlatGridLine(float2 cell,float2 cellPerPixel,float spacing)
   {
    float2 distance=abs(frac(cell/spacing+.5)-.5)*spacing/cellPerPixel;
    float2 ink=saturate(1-distance)*smoothstep(3,7,spacing/cellPerPixel);
    return max(ink.x,ink.y);
   }
   float FlatFootprintInk(float2 cell,float2 cellPerPixel,float4 area)
   {
    float2 offset=cell-(area.xy+.5*area.zw);
    offset.x-=round(offset.x/_MapSize.x)*_MapSize.x;
    float2 inside=.5*area.zw-abs(offset);
    if(min(area.z,area.w)<=0||min(inside.x,inside.y)<0)return 0;
    float2 ink=saturate(1.35-inside/cellPerPixel)*smoothstep(.6,3,area.zw/cellPerPixel);
    return .12+.60*max(ink.x,ink.y);
   }
#endif
#if defined(ORBIS_PREVIEW)
   float4 frag(v2f i):SV_Target
   {
    #ifdef ORBIS_LOCAL_SURFACE
    float2 uv=LocalGeoUV(i.uv);
    #else
    float2 uv=MarkleyGeoUV(i.uv);
    #endif
    float3 colour=tex2D(_MainTex,uv).rgb;
    if(_Political>.5)
    {
     float4 owner=tex2D(_OwnerTex,uv);
     colour=lerp(colour,owner.rgb,.34*step(.001,owner.a));
    }
    if(_RiverOverviewReady>.5 && (_RiverMap>.5 || _WaterView>.5))
    {
     float2 dx=ddx(uv),dy=ddy(uv);dx.x-=round(dx.x);dy.x-=round(dy.x);
     float lod=log2(max(1,max(length(dx*_RiverOverview_TexelSize.zw),length(dy*_RiverOverview_TexelSize.zw))));
     float ink=FlatPresentationSample(_RiverOverview,uv,lod).r;
     colour=lerp(colour,FlatColor(float3(.15,.40,.56)),ink);
    }
    if(max(_Natural,_Political)>.5)colour*=FlatDayLight(FlatRadial(uv),uv);
    #ifdef UNITY_COLORSPACE_GAMMA
    colour=GammaToLinearSpace(max(0,colour));
    #endif
    return float4(colour,1);
   }
#else
   float4 frag(v2f i):SV_Target
   {
#ifdef ORBIS_SCROLL_SURFACE
    float2 chartCoordinate=i.uv;
#endif
#ifdef ORBIS_LOCAL_SURFACE
    i.uv=LocalGeoUV(i.uv);
#elif defined(ORBIS_SCROLL_SURFACE)
    i.uv=MarkleyGeoUV(i.uv);
#endif
#ifdef ORBIS_SCROLL_SURFACE
    float2 chartRightGeographic,chartUpGeographic;
    FlatChartGeographicBasis(i.uv,chartCoordinate,chartRightGeographic,chartUpGeographic);
#endif
    float2 cell=float2(frac(i.uv.x),clamp(i.uv.y,0,1-_MapSize.w*.0001))*_MapSize.xy,id=floor(cell),f=frac(cell),uv=(id+.5)*_MapSize.zw;
#ifdef ORBIS_LOCAL_SURFACE
    float2 cellDx,cellDy;FlatCellScreenDerivatives(cell,cellDx,cellDy);
    float2 cellPerPixel=max(1e-5,sqrt(cellDx*cellDx+cellDy*cellDy));
#endif
    float naturalView=max(_Natural,_Political);
    float terrainView=max(naturalView,_DrawHeight);
    float2 colorUV=lerp(uv,FlatBlendUV(cell),_SmoothTerrain*terrainView);
    float3 col=tex2D(_MainTex,colorUV).rgb;
    float4 own=tex2D(_OwnerTex,uv),info=tex2D(_InfoTex,uv)*255;
    float4 hydro=tex2Dlod(_HydroA,float4(uv,0,0)),mixing=tex2Dlod(_HydroB,float4(uv,0,0)),geo=tex2Dlod(_GeoTex,float4(uv,0,0));
    float4 field=FlatField(cell);
    float gridBed=field.r;
    float2 terrainGradient=0,surveyCoast=0;
    float surveyWeight=0,surveyLake=0,surveyStrait=0;
    if(terrainView>.5||_WaterView>.5||_RiverMap>.5)
    {
     if(terrainView>.5)terrainGradient=FlatFieldGradient(cell,0);
     surveyWeight=FlatEarthRelief(i.uv,field.r,terrainGradient,surveyCoast);
     surveyStrait=FlatSurveyBasinShore(i.uv,cell,_InfoTex,info.g,field.g,surveyCoast);
     surveyLake=FlatEarthLake(i.uv,info.g,surveyStrait,field.g);
    }
    float edgeDistance;
    float aa=FlatEarthShoreAA(i.uv,clamp(.65/max(1,_PixelsPerTile),.008,.28),surveyStrait);
    float2 riverBank;
    float4 river=FlatRiverReceiver(cell,uv,hydro,mixing,field,riverBank);
    float overviewInk=0,closeRiver=0;
    if(_WaterView>.5||_RiverMap>.5)overviewInk=FlatRiverMapInk(cell,river,closeRiver);
    float riverRadius=length(river.ba);
    river=FlatRiverDisplaySample(river,_PixelsPerTile);
    float waterMask=FlatDisplayedWaterMask(river.r,field.g,aa,_PixelsPerTile,edgeDistance);
    float polarIce=FlatPolarIceMask(i.uv,waterMask,0)*naturalView;
    float landSnow=FlatLandSnow(cell,_InfoTex)*(1-waterMask)*naturalView;
    float4 season=float4(1,1,0,.5);
    if(naturalView>.5)season=FlatSurfaceSeason(cell,FlatRadial(i.uv));
    float snowCover=max(landSnow,season.b*(1-waterMask));
    float snowSurface=snowCover;
    float3 localSun=FlatGeographicLight(i.uv,normalize(_FlatSun+float3(0,1e-7,0)));
    float3 surfaceView=float3(0,0,1);
    #ifdef ORBIS_SCROLL_SURFACE
    // Transport the eye through the same sphere/plane rotation as the terrain.
    float3 world=i.world.xyz/max(1e-6,i.world.w);
    float3 cameraRay=normalize(_WorldSpaceCameraPos-world);
    float s=sin(_ScrollSpin),c=cos(_ScrollSpin);
    float3 planetRay=float3(c*cameraRay.x-s*cameraRay.z,cameraRay.y,-s*cameraRay.x-c*cameraRay.z);
    float3 sphereView=FlatGeographicLight(i.uv,planetRay);
    float3 planeView=float3(dot(cameraRay,_ScrollRight),dot(cameraRay,_ScrollUp),dot(cameraRay,_ScrollNormal));
    float2 projected=float2(dot(world-_ScrollNormal,_ScrollRight),dot(world-_ScrollNormal,_ScrollUp));
    float3 reliefView=normalize(float3(-projected/.18-float2(0,_ScrollRelief.z),1));
    planeView=normalize(lerp(planeView,reliefView,_ScrollRelief.x));
    planeView.xy=chartRightGeographic*planeView.x+chartUpGeographic*planeView.y;
    surfaceView=lerp(sphereView,planeView,_ScrollMorph);
    surfaceView=normalize(float3(surfaceView.xy,max(.02,surfaceView.z)));
    #endif
    float4 substrate=float4(0,0,0,geo.b);
    PDPlantView plantView=PDGroundView();
    #ifdef ORBIS_LOCAL_PLANTS
    if(naturalView>.5&&_DetailWeight>0)plantView=PDLocalPlantView(projected,chartCoordinate);
    #elif defined(ORBIS_ATLAS_PLANTS)
    if(naturalView>.5&&_DetailWeight>0)plantView=PDAtlasPlantView(projected,chartCoordinate);
    #endif
    if(naturalView>.5)substrate=FlatNaturalGround(_GeoTex,cell,geo,_PixelsPerTile,waterMask);
    float surfaceWetness=lerp(substrate.a,season.a,saturate(_VegetationSeasonReady));
    float4 terrainLight=0,terrainRelief=float4(1,1,1,0);
    float3 illumination=lerp(float3(1,1,1),FlatDayLight(FlatRadial(i.uv),i.uv),naturalView);
    if(terrainView>.5)
    {
     float4 localMoon=float4(FlatGeographicLight(i.uv,_OrbisMoon.xyz),_OrbisMoon.w*_OrbisNightParams.x);
     terrainLight=FlatTerrainLight(localSun,localMoon,_FlatDayNight*naturalView);
     if(waterMask<.999||_DrawHeight>.5)
      terrainRelief=FlatTerrainRelief(cell,gridBed,terrainGradient,terrainLight,surfaceView,FlatWetMineral(substrate.z,surfaceWetness,field.a),surveyWeight);
     snowSurface=FlatTerrainSnow(snowCover,cell,terrainGradient,_PixelsPerTile);
    }
    float frozen=max(polarIce,snowSurface);
    // A seasonal snowpack covers the ground under trees and buildings.
    float permanentIce=max(polarIce,landSnow*snowSurface/max(.00001,snowCover));
    if(naturalView>.5)
    {
     col=FlatEcotoneAlbedo(_MainTex,cell,field.g,1);
     col=FlatEcotoneColor(col,cell,field.a,substrate.a);
     col=lerp(col,FlatSeasonGround(col,field.a,season),1-waterMask);
     if(_DetailWeight<1)col=lerp(FlatVegetation(col,cell,field.a,_PixelsPerTile,plantView),col,_DetailWeight);
     col=FlatTerrainFine(col,cell,_PixelsPerTile*(1-step(.999,waterMask)),terrainGradient,field,substrate.xy,surfaceWetness,terrainLight);
     if(_DetailWeight>0)col=lerp(col,PixelGround(col,cell,field,waterMask,terrainGradient,FlatEcologyData(uv,info),plantView),_DetailWeight);
    }
    if(_DrawHeight>.5)
    {
     col=FlatElevationColor(field.r);
     terrainRelief.rgb=dot(terrainRelief.rgb,float3(.2126,.7152,.0722));
    }
    col=lerp(col*terrainRelief.rgb,FlatColor(float3(.84,.87,.90)),terrainRelief.a);
    if(_WaterView>.5)
    {
     float wetSupport;
     float4 areaOptics=FlatAreaWaterOptics(cell,_InfoTex,float4(field.b,mixing.g,mixing.b,floor(info.g+.5)),wetSupport);
     if(surveyWeight>0&&wetSupport>0)areaOptics.r=max(.15,areaOptics.r+(gridBed-field.r));
     float surveyDepth=FlatEarthMarineOptics(i.uv,surveyCoast,field.r,surveyStrait,areaOptics,wetSupport);
     FlatEarthLakeOptics(surveyLake,field.g,areaOptics,wetSupport);
     float marineSupport=wetSupport*saturate(4-areaOptics.a);
     float beachScale=FlatEarthBeachScale(surveyStrait*(1-surveyLake));
     FlatCoastalSediment coast=FlatCoastAt(cell,field,areaOptics,wetSupport,edgeDistance,frozen,_WaterClock,beachScale);
     coast.surveyDepthWeight=surveyDepth*(1-surveyLake);
     FlatRiverSection channel=FlatRiverSectionAt(cell,river,field,riverBank);
     float sediment=FlatReachSediment(cell,channel,field.g,mixing.g);
     field.b=areaOptics.r;mixing.gb=areaOptics.gb;
     mixing.g=lerp(mixing.g,sediment,channel.influence);
     // The Water overlay has a solid dry legend colour. Restore it where a
     // surveyed bank lies inside a blue simulation cell, before shading water.
     col=lerp(col,FlatColor(float3(118,123,113)/255),surveyStrait*(1-naturalView)*(1-waterMask));
     col=FlatWaterBanks(cell,col,terrainRelief.rgb,channel,sediment,field.g,(1-waterMask)*_ShowBanks,aa,1/max(1,_PixelsPerTile),beachScale);
     col=lerp(col,coast.albedo*terrainRelief.rgb,coast.coverage*_ShowBanks*naturalView);
     col*=illumination;
     float3 bottom=FlatMaterialColor(floor(geo.g+.5))*.72;
     float3 waterColor=FlatColor(float3(.20,.43,.55))*illumination;
     // The uniform switch skips optics without exposing padded land under water.
     float3 waterLight=localSun,waterView=surfaceView;
     if(_ShowWater>.5)
     {
      FlatWaterSurfaceData waterSurface;
      waterColor=FlatWaterShade(cell,mixing,field,bottom,illumination,waterView,waterLight,_WaveAmount*(1-polarIce),areaOptics.a,coast,channel,waterSurface);
      // Emphasize channels only while their physical width is unresolved.
      // The optical weight also fades across the submerged bank at a mouth.
      float riverPixels=2*riverRadius*_PixelsPerTile;
      float exposedReach=max(1-smoothstep(-max(.04,aa),0,field.g),marineSupport);
      float tint=saturate(_RiverTint)*channel.influence*exposedReach*(1-smoothstep(1.5,5,riverPixels));
      waterColor=lerp(waterColor,FlatColor(float3(.17,.43,.62))*illumination,tint);
      // Keep the normal derivatives above uniform across shorelines. Only the
      // reflection lookup can be skipped for completely dry receivers.
      [branch] if(waterMask>.001)
      {
       // An optical bed below the coarse terrain still reflects at sea level.
       float waterHeight=lerp(field.r+field.b,0,coast.surveyDepthWeight);
       #ifdef ORBIS_SCROLL_SURFACE
       if(_ScrollMorph>.999&&_ScrollRelief.x>.05)
        waterHeight=dot(world-_ScrollNormal,_ScrollNormal)/max(1e-9,_ScrollHeightScale*_ScrollRelief.w);
       #endif
       float reflectionDetail=_DetailWeight*naturalView*(1-polarIce)*waterMask;
       waterColor=FlatReflectWater(waterColor,cell,field,waterHeight,waterView,waterLight,illumination,waterSurface,_FlatClouds*_SkyReady,reflectionDetail);
      }
     }
     col=lerp(col,waterColor,waterMask);
     // Keep unresolved channels visible through the same daylight and cloud pass.
     float exposed=max(1-smoothstep(-.04,.08,field.g),channel.influence*marineSupport)*(1-polarIce);
     col=lerp(col,FlatColor(float3(.15,.40,.56))*illumination,overviewInk*(1-closeRiver)*exposed*.79);
    }
    else col*=illumination;
    if(_RiverMap>.5)col=FlatRiverMapColor(cell,_InfoTex,info.g,field,aa,overviewInk,surveyLake);
    if(frozen>.001)col=lerp(col,FlatPolarIceColor(i.uv,waterMask)*illumination*lerp(terrainRelief.rgb,1,waterMask),frozen);
    float major;float contour=FlatContour(field.r,_ContourInterval,_PixelsPerTile,major);
    float contourStrength=max(_ShowContours,_DrawHeight)*smoothstep(3,5,_PixelsPerTile)*terrainView;
    float gentleSlope=FlatGentleContourWeight(terrainGradient);
    float landContour=contour*lerp(1-waterMask,1,_DrawHeight);
    landContour*=lerp(gentleSlope*lerp(1,.72,_DetailWeight),1,_DrawHeight);
    col=lerp(col,FlatColor(float3(.31,.30,.24))*illumination,landContour*contourStrength*lerp(.23,.42,major));
    float depthMajor;float depthContour=FlatContour(-field.b,_ContourInterval,_PixelsPerTile,depthMajor);
    float depthContourStrength=waterMask*step(.01,field.g)*(1-polarIce)*_Bathymetry*(1-_DrawHeight)*gentleSlope;
    col=lerp(col,FlatColor(float3(.075,.27,.36))*illumination,depthContour*depthContourStrength*contourStrength*lerp(.12,.23,depthMajor));
    if(_Political>.5)
    {
     float4 east=tex2D(_OwnerTex,uv+float2(_MapSize.z,0)),north=tex2D(_OwnerTex,uv+float2(0,_MapSize.w));
     float eastEdge=step(.002,abs(own.a-east.a)),northEdge=step(.002,abs(own.a-north.a));
     float edgeWidth=min(.22,1/max(1,_PixelsPerTile));
     float border=max(eastEdge*step(1-edgeWidth,f.x),northEdge*step(1-edgeWidth,f.y));
     col=lerp(col,own.rgb*illumination,.34*step(.001,own.a)*(1-waterMask*.8));
     col=lerp(col,FlatColor(float3(.87,.79,.57)),border*.70*step(.001,own.a));
    }

    if(naturalView>.5)
    {
     float skyWeight=1;
     #ifdef ORBIS_SCROLL_SURFACE
     skyWeight=smoothstep(.08,.50,_ScrollMorph);
     #endif
     float4 cloud=FlatCloudColumn(i.uv);
     FlatPrecipitation weather=FlatSamplePrecipitation(i.uv,cell,cloud,_PixelsPerTile,skyWeight);
     col=lerp(col,FlatRainOnSurface(col,weather,waterMask,frozen,illumination),skyWeight);
     float structure=floor(info.b+.5);
     float2 structureOffset=f-.5;
     #ifdef ORBIS_LOCAL_SURFACE
     if(structure>.5&&structure<2.5)
      structureOffset.y/=lerp(1,FlatArtworkLatitudeScale(uv.y),_LocalPhysicalArtwork);
     #endif
     if(structure>.5&&_PixelsPerTile>=4)
     {
      float2 q=abs(structureOffset);float mark=0;
      if(structure<1.5)mark=(1-step(.22,q.x))*(1-step(.22,q.y));
      else if(structure<2.5)mark=(1-step(.34,q.x))*(1-step(.30,q.y))*step(.45,frac((structureOffset.y+.5)*5));
      else if(structure<3.5)mark=1-step(.075,q.x);
      else mark=(1-step(.16,q.x))*(1-step(.42,q.y));
      col=lerp(col,FlatColor(structure<1.5?float3(.96,.83,.53):float3(.73,.66,.46)),mark*.9*(1-_DetailWeight));
     }
     // Each prop quantizes in its latitude-corrected art frame. Quantizing the
     // geographic receiver first discards most crown rows near the poles.
     if(_DetailWeight>0)
     {
      col=lerp(col,PixelAmbientLife(col,cell,field,waterMask,info,illumination),_DetailWeight*(1-permanentIce));
      col=lerp(col,PixelProps(col,cell,field,waterMask,structure,illumination,plantView),_DetailWeight*(1-permanentIce));
     }
     float night=1-FlatDayWeight(FlatRadial(i.uv));
     float town=1-step(.5,abs(structure-1));
     col+=town*exp(-dot(structureOffset,structureOffset)*14)*night*_FlatNightLights*FlatColor(float3(.82,.42,.13))*.75;
     col=lerp(col,FlatSkyComposite(col,i.uv,cloud,_PixelsPerTile,weather,illumination),skyWeight);
    }
#ifdef ORBIS_LOCAL_SURFACE
    if(_ScrollMorph>=1)
    {
     if(_Grid>.5)
     {
      col=lerp(col,FlatColor(float3(.20,.24,.21)),FlatGridLine(cell,cellPerPixel,4)*.24*(1-waterMask));
      col*=1-FlatGridLine(cell,cellPerPixel,1)*.10*(1-waterMask);
     }
     col=lerp(col,FlatColor(float3(.94,.84,.53)),FlatFootprintInk(cell,cellPerPixel,_LocalEditFootprint));
     if(_LocalFocus.w>0)
     {
      // Projected world coordinates are affine in screen pixels at the local endpoint,
      // even on a raised triangle or across a date-line/pole UV discontinuity.
      float2 delta=(projected-_LocalFocus.xy)*_LocalFocus.z;
      float corners=FlatFocusCorners(delta,_LocalFocus.w);
      col=lerp(col,FlatColor(float3(1,.91,.61)),corners*.98);
     }
    }
    else
#endif
    {
    if(_Grid>.5&&_PixelsPerTile>=4)
    {
     float2 z=frac(cell/4);float zoneWidth=min(.035,1/max(4,_PixelsPerTile*4));
     float zoneMask=max(1-step(zoneWidth,z.x),1-step(zoneWidth,z.y));
     col=lerp(col,FlatColor(float3(.20,.24,.21)),zoneMask*.24*(1-waterMask));
     if(_PixelsPerTile>=16){float tileMask=max(1-step(.5/_PixelsPerTile,f.x),1-step(.5/_PixelsPerTile,f.y));col*=1-tileMask*.10*(1-waterMask);}
    }
    float selected=1-step(.5,max(abs(id.x-_Selected.x),abs(id.y-_Selected.y)));
    float selectionWidth=min(.20,1.5/max(1,_PixelsPerTile));
    float rim=1-step(selectionWidth,min(min(f.x,f.y),min(1-f.x,1-f.y)));
    col=lerp(col,FlatColor(float3(1,.91,.61)),selected*rim*.95);
    float2 selectedZone=cell-_Selected.zw;
    float inZone=step(0,selectedZone.x)*step(0,selectedZone.y)*step(selectedZone.x,4)*step(selectedZone.y,4);
    float zoneRim=1-step(selectionWidth,min(min(selectedZone.x,selectedZone.y),min(4-selectedZone.x,4-selectedZone.y)));
    col=lerp(col,FlatColor(float3(.94,.84,.53)),inZone*zoneRim*.7);
    }
    #if defined(ORBIS_SCROLL_SURFACE) && defined(UNITY_COLORSPACE_GAMMA)
    // Native volume/display always consumes linear scene radiance, including its flat endpoint.
    col=GammaToLinearSpace(max(0,col));
    #endif
    return float4(max(0,col),1);
   }
#endif
