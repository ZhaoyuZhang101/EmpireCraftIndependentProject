Shader "Hidden/ORBIS/VegetationDiversityProbe"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _MapSize;
            #include "../Resources/ORBIS/FlatWorld/FlatTreePlacement.cginc"
            float PDBox(float2 p,float2 lo,float2 hi)
            {
                return step(lo.x,p.x)*step(lo.y,p.y)*(1-step(hi.x,p.x))*(1-step(hi.y,p.y));
            }
            #include "../Resources/ORBIS/FlatWorld/FlatVegetationArt.cginc"

            float2 _ProbeRoot;
            float2 _ProbePoint;
            float _ProbeMode;
            float _ProbeType;
            float _ProbeSeed;
            float _ProbeLatitude;
            float _ProbeShore;
            float _ProbeFoliage;
            float3 _ProbeSun;
            float4 _ProbeEcology;
            float4 _ProbeInfo;

            float4 frag(v2f_img input):SV_Target
            {
                if(_ProbeMode>8.5)
                {
                    float2 pixel=floor((input.uv-.5)*64);
                    float2 edge;
                    float4 crown=PDTreeCrown(pixel,_ProbeType,_ProbeSeed,_ProbeFoliage,edge);
                    float shadow=PDTreeCastShadow(pixel,_ProbeSun,_ProbeType,_ProbeSeed,_ProbeFoliage);
                    return float4(crown.a,shadow,0,1);
                }
                if(_ProbeMode>7.5)
                {
                    float2 edge;
                    float4 crown=PDTreeCrown(_ProbePoint,_ProbeType,_ProbeSeed,edge);
                    return float4(crown.a,PDTreeCastShadow(_ProbePoint,_ProbeSun,_ProbeType,_ProbeSeed),edge);
                }
                if(_ProbeMode>6.5&&_ProbeMode<7.5)
                    return PDTreeTrunk(input.uv*float2(12,22)-float2(6,3),_ProbeType,_ProbeSeed).xxxx;
                if(_ProbeMode>3.5&&_ProbeMode<4.5)return PDTreePlacement(_ProbeRoot);
                if(_ProbeMode>2.5&&_ProbeMode<3.5)
                    return PDTreeSpecies(_ProbeEcology,_ProbeInfo,_ProbeShore).xxxx;

                float4 placement=PDTreePlacement(_ProbeRoot);
                float latitudeScale=max(.02,cos(_ProbeLatitude*.01745329252));
                float2 localPoint=placement.xy+(input.uv-.5)*float2(64,64*latitudeScale);
                localPoint.y=placement.y+(localPoint.y-placement.y)/latitudeScale;
                float2 pixel=floor(localPoint-placement.xy)/placement.z;
                if(_ProbeMode>.5&&_ProbeMode<1.5)
                    return PDTreeCastShadow(pixel,_ProbeSun,_ProbeType,_ProbeSeed).xxxx;

                float2 edge;float3 surface;
                float4 crown=PDTreeCrown(pixel,_ProbeType,_ProbeSeed,1,0,edge,surface);
                if(_ProbeMode>5.5)
                {
                    float within=PDTreeInDrawBounds(pixel);
                    return (crown.a*(1-within)).xxxx;
                }
                if(_ProbeMode>4.5)return crown.aaaa;
                if(_ProbeMode>1.5)return crown;
                float trunk=PDTreeTrunk(pixel,_ProbeType,_ProbeSeed);
                float3 color=PDTreeBark(pixel,_ProbeType,_ProbeSeed)*trunk;
                float3 foliage=PDTreeFoliage(crown.xyz,surface,_ProbeType,_ProbeSeed,_ProbeSun,edge);
                color=lerp(color,foliage,crown.a);
                return float4(color,saturate(crown.a+trunk));
            }
            ENDCG
        }
    }
}
