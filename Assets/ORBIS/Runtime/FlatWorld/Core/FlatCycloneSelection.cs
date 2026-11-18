using System;

namespace Orbis.Flat
{
    // Monthly suitability diagnostic. Actual cyclone positions and intensities
    // come from the persistent tracks in FlatCycloneWeather.
    public struct FlatCyclone
    {
        public int X,Y;
        public float Strength;
        public bool Active { get { return Strength>.16f; } }
    }
    public static class FlatCycloneSelection
    {
        static float Saturate(float x){return FMath.Clamp(x,0,1);}
        static float Potential(float temperature,float precipitation)
        {return Saturate((temperature-23)/7)*Saturate((precipitation-105)/175);}
        public static FlatCyclone Find(WorldData world,int month,bool north)
        {
            if(world==null)throw new ArgumentNullException(nameof(world));
            month=((month%12)+12)%12;
            float best=.16f;int bestId=-1;
            for(int y=0;y<world.Height;y++)
            {
                float latitude=((y+.5f)/world.Height-.5f)*180;
                if((north?latitude:-latitude)<8||Math.Abs(latitude)>38)continue;
                float latitudeWeight=.75f+.25f*Saturate(1-Math.Abs(Math.Abs(latitude)-23)/18);
                for(int x=0;x<world.Width;x++)
                {
                    int i=y*world.Width+x;
                    if(!FlatTerrainAnalysis.Ocean(world.Water[i]))continue;
                    float rank=Potential(world.Temperature[i*12+month],world.Precipitation[i*12+month])*latitudeWeight;
                    if(rank>best)
                    {
                        // CoastDistance is zero across the ocean by construction;
                        // reject actual adjacent land instead of testing that field.
                        bool offshore=true;
                        for(int oy=-2;oy<=2&&offshore;oy++)for(int ox=-2;ox<=2;ox++)
                        {int neighbour=world.Index(x+ox,y+oy);if(neighbour>=0&&!FlatTerrainAnalysis.Ocean(world.Water[neighbour])){offshore=false;break;}}
                        if(offshore){best=rank;bestId=i;}
                    }
                }
            }
            if(bestId<0)return new FlatCyclone();
            return new FlatCyclone{X=bestId%world.Width,Y=bestId/world.Width,Strength=best};
        }
    }
}
