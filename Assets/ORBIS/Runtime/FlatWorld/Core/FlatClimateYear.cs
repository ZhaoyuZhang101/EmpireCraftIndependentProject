using System;

namespace Orbis.Flat
{
    // A seeded, low-frequency climate anomaly superimposed on the saved monthly
    // normals. The regional dipoles are continuous over both poles and the seam.
    public readonly struct FlatClimateYear
    {
        public readonly float HeatX, HeatY, HeatZ, MeanHeat;
        public readonly float RainX, RainY, RainZ;

        FlatClimateYear(float heatX, float heatY, float heatZ, float meanHeat,
                        float rainX, float rainY, float rainZ)
        {
            HeatX=heatX; HeatY=heatY; HeatZ=heatZ; MeanHeat=meanHeat;
            RainX=rainX; RainY=rainY; RainZ=rainZ;
        }

        public static FlatClimateYear At(uint seed, double day)
        {
            const double turn=Math.PI*2;
            double years=day/360.0;
            double phase=FMath.Unit(seed^0x87a091ceu)*turn;
            double other=FMath.Unit(seed^0x9e3779b9u)*turn;
            double axis=FMath.Unit(seed^0x761b9117u)*turn;
            float east=(float)Math.Cos(axis), prime=(float)Math.Sin(axis);
            float basin=(float)(.72*Math.Sin(years*turn/4.3+phase)+.28*Math.Sin(years*turn/6.8+other));
            float cross=(float)Math.Sin(years*turn/5.7+other);
            float polar=(float)Math.Sin(years*turn/7.1+phase-other);
            float global=(float)(.65*Math.Sin(years*turn/9.2+other)+.35*Math.Sin(years*turn/3.8+phase));
            // Rain is displaced between regions. Scaling local rainfall keeps dry
            // cells dry and permits either a wetter or drier year at a given site.
            return new FlatClimateYear(1.55f*basin*east-.65f*cross*prime, 1.15f*polar,
                1.55f*basin*prime+.65f*cross*east, .85f*global,
                -.30f*basin*east-.16f*cross*prime, .12f*polar, -.30f*basin*prime+.16f*cross*east);
        }

        public void Offsets(float x,float y,float z,out float degrees,out float rainFraction)
        {
            degrees=MeanHeat+HeatX*x+HeatY*y+HeatZ*z;
            rainFraction=RainX*x+RainY*y+RainZ*z;
        }

        public void AtTile(int tile,int width,int height,out float degrees,out float rainFraction)
        {
            double lon=((tile%width+.5)/width-.5)*2*Math.PI, lat=((tile/width+.5)/height-.5)*Math.PI;
            double cosLat=Math.Cos(lat);
            Offsets((float)(Math.Sin(lon)*cosLat),(float)Math.Sin(lat),(float)(Math.Cos(lon)*cosLat),out degrees,out rainFraction);
        }

        public void Weather(WorldData world,int tile,int month,out float temperature,out float precipitation)
        {
            AtTile(tile,world.Width,world.Height,out float degrees,out float rainFraction);
            int index=tile*12+month;
            temperature=world.Temperature[index]+degrees;
            precipitation=world.Precipitation[index]*(1+rainFraction);
        }
    }
}
