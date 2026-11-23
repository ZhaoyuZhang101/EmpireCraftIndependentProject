using System;

namespace Orbis.Flat
{
    public struct FlatLightningFlash
    {
        public float X,Y,Z,Strength;
    }

    /// <summary>Bounded transient visual flashes selected from existing monthly climate.</summary>
    public sealed class FlatLightningVisuals
    {
        public readonly FlatLightningFlash[] Flashes=new FlatLightningFlash[4];
        readonly WorldData world;
        readonly Random random;
        readonly float[] ages=new float[4],powers=new float[4];
        float cooldown=.75f;
        int cursor;

        public FlatLightningVisuals(WorldData source)
        {
            world=source??throw new ArgumentNullException(nameof(source));
            random=new Random(unchecked((int)(FMath.Seed(source.Seed)^0x5a72e1b9u)));
        }
        public static float Envelope(float age)
        {
            float first=FMath.Clamp(1-Math.Abs(age-.035f)/.035f,0,1);
            float second=.55f*FMath.Clamp(1-Math.Abs(age-.14f)/.045f,0,1);
            return Math.Max(first,second);
        }
        static float EnvelopePeak(float start,float end)
        {
            float peak=Math.Max(Envelope(start),Envelope(end));
            if(start<=.035f&&end>=.035f)peak=1;
            if(start<=.14f&&end>=.14f)peak=Math.Max(peak,.55f);
            return peak;
        }
        public void Tick(float delta,bool enabled,float focusX,float focusY,bool local)
        {
            if(!enabled)
            {
                Array.Clear(Flashes,0,Flashes.Length);Array.Clear(powers,0,powers.Length);cooldown=.75f;return;
            }
            delta=FMath.Clamp(delta,0,.1f);
            for(int i=0;i<Flashes.Length;i++)
            {
                float previous=ages[i];ages[i]+=delta;var flash=Flashes[i];
                flash.Strength=powers[i]*EnvelopePeak(previous,ages[i]);Flashes[i]=flash;
            }
            cooldown-=delta;if(cooldown>0)return;
            cooldown=1.8f+(float)random.NextDouble()*3.6f;
            int chosen=-1;float best=0;
            var season=new FlatMonthlyForcing(world.Day);
            var climate=FlatClimateYear.At(FMath.Seed(world.Seed),world.Day);
            var moisture=world.Moisture;
            var currents=moisture?.OceanCurrents;
            var exo=moisture?.ExoPlaSim;
            int month0=FMath.Clamp((int)Math.Floor((world.Day%360+360)%360/30.0),0,11);
            for(int attempt=0;attempt<32;attempt++)
            {
                int x=local?FMath.Wrap((int)Math.Floor(focusX)+random.Next(-6,7),world.Width):random.Next(world.Width);
                int y=local?FMath.Clamp((int)Math.Floor(focusY)+random.Next(-6,7),0,world.Height-1):random.Next(world.Height);
                int cell=y*world.Width+x;
                float rain=season.Sample(world.Precipitation,cell*12,ClimateRange.Nonnegative);
                float temperature=season.Sample(world.Temperature,cell*12);
                climate.AtTile(cell,world.Width,world.Height,out float degrees,out float regionalRain);
                rain*=1+regionalRain;temperature+=degrees;
                bool isOcean=FlatTerrainAnalysis.Ocean(world.Water[cell]);
                // NASA LIS/OTD physics: ~88% of lightning occurs over heated land, islands & coastal sea-breeze convergence
                // where strong mixed-phase updrafts (>6 m/s) separate ice/graupel charge. Open-ocean trade showers &
                // equatorial Ekman cold-tongue upwelling slots lack ice-phase charge separation unless driven by warm-pool squalls.
                float phaseWeight=1f;
                if(isOcean)
                {
                    if(rain<155f||temperature<12f)continue;
                    float u=(x+.5f)/world.Width,v=(y+.5f)/world.Height;
                    float latDeg=(v-.5f)*180f;
                    float sstAnom=currents!=null?currents.Sample(currents.SeaAnomaly,u,v):0f;
                    float breeze=exo!=null?exo.SampleStatic(exo.MaritimeSeaBreezeGuard,u,v):0f;
                    bool eqColdSlot=Math.Abs(latDeg)<4.5f&&sstAnom<0.22f&&breeze<0.15f;
                    if(eqColdSlot&&rain<245f)continue;
                    float warmPool=FMath.Clamp((sstAnom-0.10f)/0.85f,0f,1f);
                    phaseWeight=FMath.Lerp(0.20f,0.52f,Math.Max(warmPool,breeze))*(eqColdSlot?0.35f:1f);
                }
                else if(rain<90f||temperature<2f)continue;
                else if(exo!=null)
                {
                    float u=(x+.5f)/world.Width,v=(y+.5f)/world.Height;
                    float cape=exo.SampleMonthly(exo.CapeJkg,u,v,month0);
                    phaseWeight=0.88f+0.24f*FMath.Clamp(cape/2000f,0f,1f);
                }
                float score=rain*phaseWeight*(.7f+(float)random.NextDouble()*.3f);
                if(score>best){best=score;chosen=cell;}
            }
            if(chosen<0)return;
            double longitude=((chosen%world.Width+.15+random.NextDouble()*.7)/world.Width-.5)*Math.PI*2;
            double latitude=((chosen/world.Width+.15+random.NextDouble()*.7)/world.Height-.5)*Math.PI;
            float cosine=(float)Math.Cos(latitude);int slot=cursor++%Flashes.Length;
            Flashes[slot]=new FlatLightningFlash{X=(float)Math.Sin(longitude)*cosine,Y=(float)Math.Sin(latitude),Z=(float)Math.Cos(longitude)*cosine};
            ages[slot]=0;powers[slot]=.55f+.45f*FMath.Clamp(best/240,0,1);
        }
    }
}
