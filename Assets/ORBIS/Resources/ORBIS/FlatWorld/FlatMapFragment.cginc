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
    float naturalView=max(_Natural,_Political);
    float terrainView=max(naturalView,_DrawHeight);
    float2 colorUV=lerp(uv,FlatBlendUV(cell),_SmoothTerrain*terrainView);
    float3 col=tex2D(_MainTex,colorUV).rgb;
    float4 own=tex2D(_OwnerTex,uv),info=tex2D(_InfoTex,uv)*255;
    float4 hydro=tex2Dlod(_HydroA,float4(uv,0,0)),mixing=tex2Dlod(_HydroB,float4(uv,0,0)),geo=tex2Dlod(_GeoTex,float4(uv,0,0));
    float4 field=FlatField(cell);
    float edgeDistance;float2 riverFlow;
    float aa=clamp(.65/max(1,_PixelsPerTile),.008,.28);
    float waterMask=FlatWaterMask(cell,uv,hydro,mixing,field,aa,edgeDistance,riverFlow);
    float polarIce=FlatPolarIceMask(i.uv,waterMask,0)*naturalView;
    float landSnow=FlatLandSnow(cell,_InfoTex)*(1-waterMask)*naturalView;
    float terrainRelief=1;
    float3 illumination=lerp(float3(1,1,1),FlatDayLight(FlatRadial(i.uv),i.uv),naturalView);
    float2 terrainGradient=0;
    if(terrainView>.5)
    {
     terrainGradient=FlatFieldGradient(cell,0);
     float3 localSun=FlatGeographicLight(i.uv,normalize(_FlatSun+float3(0,1e-7,0)));
     // A soft north-west sky fill keeps an overhead map readable at solar noon.
     float3 terrainLight=float3(-.45,.6,.75)+localSun*.30*smoothstep(0,.3,localSun.z);
     if(waterMask<.999||_DrawHeight>.5)terrainRelief=FlatTerrainRelief(cell,field.r,terrainGradient,terrainLight);
     landSnow=FlatTerrainSnow(landSnow,cell,terrainGradient,_PixelsPerTile);
    }
    float frozen=max(polarIce,landSnow);
    if(naturalView>.5)
    {
     float2 ecologyCell=FlatEcotoneCell(cell,field.g);
     col=tex2D(_MainTex,FlatBlendUV(ecologyCell)).rgb;
     col=FlatEcotoneColor(col,cell,field.a,tex2Dlod(_GeoTex,float4(cell*_MapSize.zw,0,0)).b);
     col=lerp(col,FlatSeasonGround(col,cell,field.a),1-waterMask);
     col=lerp(FlatVegetation(col,cell,field.a,_PixelsPerTile),col,_DetailWeight);
     col=FlatTerrainFine(col,cell,_PixelsPerTile,terrainGradient);
     if(_DetailWeight>0)col=lerp(col,PixelGround(col,cell,field,waterMask,terrainGradient,FlatEcologyData(uv,info)),_DetailWeight);
    }
    if(_DrawHeight>.5)col=FlatElevationColor(field.r);
    col*=terrainRelief;
    if(_WaterView>.5)
    {
     float4 areaOptics=FlatAreaWaterOptics(cell,_InfoTex,float4(field.b,mixing.g,mixing.b,floor(info.g+.5)));
     float waterKind=areaOptics.a;
     field.b=areaOptics.r;mixing.gb=areaOptics.gb;
     if(_RiverAtlasReady>.5)
     {
      float4 river=FlatRiverAtlasSample(cell);
      float halfWidth=length(river.ba);
      hydro.gb=tex2D(_HydroA,i.uv).gb;hydro.a=0;
      if(field.g<0&&river.r< -field.g&&halfWidth>.0001)
      {
       hydro=float4(river.g,river.ba/max(.0001,halfWidth),halfWidth);
       mixing.g=FlatRiverAtlasSediment(cell);mixing.b=1;waterKind=5;
      }
     }
     else
     {
      if(hydro.a>.01)hydro.gb=riverFlow;else hydro.gb=tex2D(_HydroA,i.uv).gb;
      if(hydro.a>.01&&field.g<0)waterKind=5;
     }
     float bankWidth=hydro.a>.0001?clamp(hydro.a*.40,.02,.075):.16;
     float bankMask=(1-waterMask)*(1-smoothstep(0,bankWidth+aa,edgeDistance))*_ShowBanks;
     float3 bankColor=FlatMaterialColor(floor(geo.g+.5));
     bankColor=lerp(bankColor,FlatColor(float3(.83,.75,.55)),step(-1.4,field.g)*step(geo.g,.5));
     col=lerp(col,bankColor,bankMask*.58);
     col*=illumination;
     float3 bottom=FlatMaterialColor(floor(geo.g+.5))*.72;
     float3 waterColor=FlatColor(float3(.20,.43,.55))*illumination;
     // The uniform switch skips optics without exposing padded land under water.
     float3 waterLight=FlatGeographicLight(i.uv,normalize(_FlatSun+float3(0,1e-7,0)));
     float3 waterView=float3(0,0,1);
     #ifdef ORBIS_SCROLL_SURFACE
     // Transport the actual camera direction through the same sphere/plane
     // rotation as the terrain. Grazing globe water must not use a zenith view.
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
     waterView=lerp(sphereView,planeView,_ScrollMorph);
     waterView=normalize(float3(waterView.xy,max(.02,waterView.z)));
     #endif
     if(_ShowWater>.5)
     {
      FlatWaterSurfaceData waterSurface;
      waterColor=FlatWaterShade(cell,hydro,mixing,field,bottom,illumination,waterView,waterLight,_WaveAmount*(1-polarIce),waterKind,edgeDistance,waterSurface);
      // Keep the normal derivatives above uniform across shorelines. Only the
      // reflection lookup can be skipped for completely dry receivers.
      [branch] if(waterMask>.001)
      {
       float waterHeight=max(0,field.r+field.b);
       #ifdef ORBIS_SCROLL_SURFACE
       if(_ScrollMorph>.999&&_ScrollRelief.x>.05)
        waterHeight=max(0,dot(world-_ScrollNormal,_ScrollNormal)/max(1e-7,_ScrollHeightScale*_ScrollRelief.x));
       #endif
       float reflectionDetail=_DetailWeight*naturalView*(1-polarIce)*waterMask;
       waterColor=FlatReflectWater(waterColor,cell,field,waterHeight,waterView,waterLight,illumination,waterSurface,_FlatClouds*_SkyReady,reflectionDetail);
      }
     }
     col=lerp(col,waterColor,waterMask);
    }
    else col*=illumination;
    if(frozen>.001)col=lerp(col,FlatPolarIceColor(i.uv,waterMask)*illumination*lerp(terrainRelief,1,waterMask),frozen);
    float major;float contour=FlatContour(field.r,_ContourInterval,_PixelsPerTile,major);
    float contourStrength=max(_ShowContours,_DrawHeight)*smoothstep(3,5,_PixelsPerTile)*terrainView;
    float gentleSlope=FlatGentleContourWeight(cell,terrainGradient);
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
     if(structure>.5&&_PixelsPerTile>=4)
     {
      float2 q=abs(f-.5);float mark=0;
      if(structure<1.5)mark=(1-step(.22,q.x))*(1-step(.22,q.y));
      else if(structure<2.5)mark=(1-step(.34,q.x))*(1-step(.30,q.y))*step(.45,frac(f.y*5));
      else if(structure<3.5)mark=1-step(.075,q.x);
      else mark=(1-step(.16,q.x))*(1-step(.42,q.y));
      col=lerp(col,FlatColor(structure<1.5?float3(.96,.83,.53):float3(.73,.66,.46)),mark*.9*(1-_DetailWeight));
     }
     // Each prop quantizes in its latitude-corrected art frame. Quantizing the
     // geographic receiver first discards most crown rows near the poles.
     if(_DetailWeight>0)
     {
      col=lerp(col,PixelAmbientLife(col,cell,field,waterMask,info,illumination),_DetailWeight*(1-frozen));
      col=lerp(col,PixelProps(col,cell,field,waterMask,structure,illumination),_DetailWeight*(1-frozen));
     }
     float night=1-FlatDayWeight(FlatRadial(i.uv));
     float town=1-step(.5,abs(structure-1));
     col+=town*exp(-dot(f-.5,f-.5)*14)*night*_FlatNightLights*FlatColor(float3(.82,.42,.13))*.75;
     col=lerp(col,FlatSkyComposite(col,i.uv,cloud,_PixelsPerTile,weather,illumination),skyWeight);
    }
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
    #if defined(ORBIS_SCROLL_SURFACE) && defined(UNITY_COLORSPACE_GAMMA)
    // Native volume/display always consumes linear scene radiance, including its flat endpoint.
    col=GammaToLinearSpace(max(0,col));
    #endif
    return float4(max(0,col),1);
   }
