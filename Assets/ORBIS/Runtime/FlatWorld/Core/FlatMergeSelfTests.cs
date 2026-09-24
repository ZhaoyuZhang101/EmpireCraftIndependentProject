using System;
namespace Orbis.Flat
{
    public static class FlatMergeSelfTests
    {
        public static void Run(Action<bool,string> check)
        {
            double[] days=new double[2];
            for(int mode=0;mode<2;mode++)
            {
                int index=mode;var clock=new FlatSolarClock();
                for(int i=0;i<600;i++)clock.Advance(1.0/60,mode==0?1:20,120,false,d=>days[index]+=d);
                days[mode]=clock.RenderDay(days[mode]);
            }
            check(Math.Abs(days[1]/days[0]-20)<1e-5,"Merged clock 20x ratio");
            double day=0;var stalled=new FlatSolarClock();Action<float> commit=d=>day+=d;
            stalled.Advance(7.25,20,30,false,commit);double before=stalled.RenderDay(day);
            check(Math.Abs(before-7.25*20/30)<1e-6,"Calendar debt retained");
            stalled.Advance(600,20,30,true,commit);check(stalled.RenderDay(day)==before,"Pause freezes calendar");
            stalled.Flush(commit);check(Math.Abs(day-before)<1e-7,"Save flush retains phase");
            foreach(int w in new[]{64,256,512,1024})
            {
                for(int lap=-20;lap<=20;lap++)check(FlatScrollMath.Tile(13.5+lap*w,12.5,w,w/2)==12*w+13,"Canonical tile across seam");
                check(FlatScrollMath.Tile(.5,-.5,w,w/2)==-1&&FlatScrollMath.Tile(.5,w/2,w,w/2)==-1,"No polar wrapping");
            }
            bool threw=false;try{FlatScrollMath.Wrap(1,double.NaN);}catch(ArgumentOutOfRangeException){threw=true;}
            check(threw,"Reject invalid wrap period");
            var world=new WorldData(64,32,"farm-index",TerrainPreset.Continents);
            for(int i=0;i<world.Count;i++)
            {
                world.Elevation[i]=100;world.Fertility[i]=.7f;
                for(int m=0;m<12;m++)world.Temperature[i*12+m]=20;
            }
            var sim=new FlatSimulation(world);int owner=sim.Found(0);
            check(owner>=0,"Farm fixture civilization");world.Nations[owner].Food=10000;
            int farm=1;check(sim.Build(farm,Structure.Farm),"Build farm");
            var clone=world.Clone();clone.Buildings[farm]=Structure.None;var without=new FlatSimulation(clone);
            sim.Step(.25f);without.Step(.25f);
            double expected=.7f*3*FMath.Clamp(23f/24,0,1)*.25f;
            check(Math.Abs(world.Nations[owner].Food-clone.Nations[owner].Food-expected)<1e-6,"Farm index preserves production");
            check(sim.Build(farm,Structure.Road),"Farm replaced by road");
            clone=world.Clone();var reindexed=new FlatSimulation(clone);
            sim.Step(.25f);reindexed.Step(.25f);
            check(Math.Abs(world.Nations[owner].Food-clone.Nations[owner].Food)<1e-8,"Farm removal matches rebuild index");
        }
    }
}
