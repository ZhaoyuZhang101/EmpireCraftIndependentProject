#ifndef ORBIS_FLAT_COASTAL_SEDIMENT
#define ORBIS_FLAT_COASTAL_SEDIMENT

struct FlatCoastalSediment
{
    float shore;       // Positive offshore, measured in equatorial cell widths.
    float coverage;
    float mud;
    float bar;
    float3 albedo;
    float3 seaward;
};

float FlatMetricShore(float2 cell,float shore,out float2 seaward)
{
    float2 gradient=FlatTerrainMetricGradient(cell,FlatHeightGradient(shore,cell));
    float magnitude=length(gradient);
    seaward=gradient/max(.001,magnitude);
    return shore/max(.32,magnitude);
}

// A broad opening in front of a shore admits swell. A nearby opposing bank or
// headland shelters it. Both flanks are sampled in geographic east/north units.
float FlatCoastShelter(float2 cell,float shore,float2 seaward,float2 cellMetric)
{
    float2 across=float2(-seaward.y,seaward.x);
    float2 ahead=cell+(1.35-clamp(shore,-.8,.8))*seaward/cellMetric;
    float2 offset=across*1.65/cellMetric;
    float left=FlatVisualSample(float4((ahead-offset)*_MapSize.zw,0,0)).g;
    float right=FlatVisualSample(float4((ahead+offset)*_MapSize.zw,0,0)).g;
    float centre=FlatVisualSample(float4(ahead*_MapSize.zw,0,0)).g;
    float3 open=smoothstep(-.35,1.05,float3(left,centre,right));
    return 1-dot(open,float3(.30,.40,.30));
}

float FlatMudFraction(float grade,float shelter,float wetness,float fresh,float sediment)
{
    float gentle=1-smoothstep(145,420,grade);
    float deposition=smoothstep(.06,.63,fresh)*.68+smoothstep(.018,.25,sediment)*.32;
    float sheltered=shelter*(.38+.42*saturate(wetness));
    return gentle*smoothstep(.24,.77,deposition+sheltered);
}

FlatCoastalSediment FlatCoastArtwork(float3 p,float3 seaward,float shore,float grade,float mud,
                                      float footprint,float clock)
{
    FlatCoastalSediment coast=(FlatCoastalSediment)0;
    coast.shore=shore;
    coast.mud=mud;
    coast.seaward=seaward;
    // The same tide wets a wider area on a gentle slope. Adjacent patches of
    // deposited sediment break up the back of the beach along the shore.
    float patch=FlatDetailNoise(p*1.8+32);
    float profile=lerp(1,.38,smoothstep(15,210,grade));
    float deposit=smoothstep(.22,.78,patch);
    float dryWidth=lerp(.49,.60,mud)*profile*lerp(.57,1,deposit);
    float wetWidth=lerp(.78,.94,mud)*lerp(.67,1,profile)*lerp(.75,1,deposit);
    coast.coverage=smoothstep(-dryWidth,-dryWidth*.34,shore)*(1-smoothstep(wetWidth*.52,wetWidth,shore));
    coast.coverage*=1-smoothstep(200,590,grade);
    // The two landforms have independent textures. Crests follow the strand,
    // whereas the fine channels stretch down the mudflat towards open water.
    float grain=FlatDetailNoise(p*28+7)-.5;
    float sandDetail=1-smoothstep(.035,.11,footprint);
    float ripple=sin(shore*65+patch*7)*(.55+.45*sin(shore*29+patch*3));
    float3 drained=p-seaward*shore*.68;
    float trunk=abs(FlatDetailNoise(drained*4.7+25)-.51);
    float branch=abs(FlatDetailNoise(drained*9.5+13)-.48);
    float channel=(1-smoothstep(.023,.104,trunk))+
        .51*(1-smoothstep(.016,.069,branch))*(1-smoothstep(.09,.25,trunk));
    channel=saturate(channel)*(1-smoothstep(.065,.20,footprint));
    float tide=.5+.5*sin(clock*.043+dot(p,float3(.054,.081,-.065)));
    float wash=lerp(.045,.18,mud)*tide;
    float wet=smoothstep(-lerp(.13,.23,mud),.025,shore+wash+(patch-.5)*.062);
    float3 sand=lerp(float3(.84,.76,.59),float3(.56,.54,.45),wet);
    sand*=1+(patch-.5)*.10+(grain*.15+ripple*.055)*sandDetail;
    float3 silt=lerp(float3(.55,.49,.39),float3(.38,.39,.35),wet);
    silt*=1+(patch-.5)*.16+grain*.055*sandDetail;
    silt=lerp(silt,float3(.28,.34,.32),channel*(.57+wet*.26));
    coast.albedo=FlatColor(lerp(sand,silt,mud));
    float crest=1-smoothstep(.055,.19,abs(shore-(.39+(patch-.5)*.27)));
    coast.bar=crest*(1-mud)*(1-smoothstep(.15,.45,footprint));
    return coast;
}

FlatCoastalSediment FlatCoastAt(float2 cell,float4 field,float4 basin,float wetSupport,
                                float edge,float frozen,float clock)
{
    float2 seaward;
    float shore=FlatMetricShore(cell,field.g,seaward);
    float grade=length(FlatTerrainMetricGradient(cell,FlatHeightGradient(field.r,cell)))*(_MapSize.x/512);
    float3 p=FlatDetailPosition(cell);
    float footprint=max(length(ddx(p)),length(ddy(p)));
    FlatCoastalSediment coast=(FlatCoastalSediment)0;
    coast.shore=shore;
    // Kinds 1..3 are marine, 4 is a lake and 5 is a river. A river may cross
    // the dry side of a sea cell; compare its actual edge with the sea shore.
    float sea=saturate(basin.a)*saturate(4-basin.a)*smoothstep(0,.045,wetSupport);
    sea*=smoothstep(-.10,0,edge+field.g)*(1-frozen);
    sea*=1-smoothstep(1.10,1.60,abs(field.g));
    if(_FieldReady<.5||sea<.001||grade>=590||shore<=-.60||shore>=.94)return coast;
    float2 uv=cell*_MapSize.zw;
    float lon=(uv.x-.5)*6.283185307,lat=(uv.y-.5)*3.141592654;
    float2 metric=float2(max(.08,cos(lat)),_MapSize.x/(2*_MapSize.y));
    float3 east=float3(cos(lon),0,-sin(lon));
    float3 north=float3(-sin(lat)*sin(lon),cos(lat),-sin(lat)*cos(lon));
    float shelter=FlatCoastShelter(cell,shore,seaward,metric);
    float wetness=tex2Dlod(_GeoTex,float4(uv,0,0)).b;
    float mud=FlatMudFraction(grade,shelter,wetness,basin.b,basin.g);
    coast=FlatCoastArtwork(p,east*seaward.x+north*seaward.y,shore,grade,mud,footprint,clock);
    coast.coverage*=sea*(1-smoothstep(22,115,max(0,field.r)));
    return coast;
}

// Use the neighbouring measured depths for the bed normal and relative crest
// height. A small sky fill preserves orientation at noon and in deep chart water.
float FlatSeabedLight(float depth,float4 neighbours,float3 sun)
{
    float2 slope=.5*float2(neighbours.y-neighbours.x,neighbours.w-neighbours.z);
    slope*=(_MapSize.x/512)/(40+depth*.14);
    float3 normal=normalize(float3(slope,1));
    float3 sky=normalize(float3(-.48,.44,.80));
    float daylight=smoothstep(.02,.42,sun.z);
    float light=(dot(normal,sky)-sky.z)*.30+(dot(normal,sun)-sun.z)*(.59*daylight);
    float form=clamp((dot(neighbours,float4(.25,.25,.25,.25))-depth)/(70+depth*.23),-1,1);
    return 1+saturate(_Relief)*clamp(light+form*.10,-.34,.27);
}

float FlatSampleSeabed(float2 cell,float3 sun,out float bedDepth)
{
    float2 uv=cell*_MapSize.zw;
    bedDepth=max(0,FlatVisualSample(float4(uv,0,0)).b);
    if(_Relief<.01)return 1;
    float4 west,east,south,north;FlatGroundSamples(uv,1,west,east,south,north);
    return FlatSeabedLight(bedDepth,max(0,float4(west.b,east.b,south.b,north.b)),sun);
}
#endif
