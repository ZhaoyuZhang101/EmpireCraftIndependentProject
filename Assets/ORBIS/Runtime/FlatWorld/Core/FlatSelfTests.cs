using System;
using System.Threading;
using System.Diagnostics;
namespace Orbis.Flat
{
    public static class FlatSelfTests
    {
        public static int Assertions { get; private set; }
        public static string LastOperation { get; private set; }
        static void Check(bool test,string message)
        {Assertions++;if(!test)throw new InvalidOperationException("FlatWorld test: "+message);}
        static void ClimateCase(string name,Koppen expected,float[] t,float[] p)
        {Check(KoppenClassifier.Classify(t,p)==expected,"Koppen "+name+" expected "+expected+" got "+KoppenClassifier.Classify(t,p));}
        public static string Run()
        {
            Assertions=0;FlatMergeSelfTests.Run(Check);var watch=Stopwatch.StartNew();LastOperation="Height presets";
            Check(HeightBands.Upper.Length==20,"Exactly twenty land bands");
            Check(HeightBands.Level(0)==0&&HeightBands.Level(-200)==0,"Water is not a land altitude band");
            for(int level=1;level<=20;level++)
            {
                int top=HeightBands.Upper[level-1],bottom=level==1?0:HeightBands.Upper[level-2];
                Check(HeightBands.Level(top)==level,"Inclusive upper bound");Check(HeightBands.Level(bottom+.01f)==level,"Exclusive lower bound");
                Check(HeightBands.Quantize(top)==top,"Preset identity");
                if(level<=10)Check(top-bottom==100,"First ten 100m increments");
                if(level>=12)Check(top-bottom>HeightBands.Upper[level-2]-HeightBands.Upper[level-3],"Higher bands widen progressively");
            }
            Check(HeightBands.Preset(20)==4000,"4000m cap");Check(HeightBands.Quantize(99999)==4000&&HeightBands.Quantize(-99999)==-4000,"Bound generated elevations");
            Check(HeightBands.Ocean(-1001)==WaterKind.DeepOcean&&HeightBands.Ocean(-1000)==WaterKind.MiddleOcean&&HeightBands.Ocean(-200)==WaterKind.ShallowOcean,"Water depth thresholds");
            LastOperation="Koppen 30 classes and boundaries";
            ClimateCase("Af",Koppen.Af,new float[]{26f,26f,26f,26f,26f,26f,26f,26f,26f,26f,26f,26f},new float[]{100f,100f,100f,100f,100f,100f,100f,100f,100f,100f,100f,100f});
            ClimateCase("Am",Koppen.Am,new float[]{26f,26f,26f,26f,26f,26f,26f,26f,26f,26f,26f,26f},new float[]{40f,200f,200f,200f,200f,200f,200f,200f,200f,200f,200f,200f});
            ClimateCase("Aw",Koppen.Aw,new float[]{26f,26f,26f,26f,26f,26f,26f,26f,26f,26f,26f,26f},new float[]{5f,100f,100f,100f,100f,100f,100f,100f,100f,100f,100f,100f});
            ClimateCase("BWh",Koppen.BWh,new float[]{26f,26f,26f,26f,26f,26f,26f,26f,26f,26f,26f,26f},new float[]{2f,2f,2f,2f,2f,2f,2f,2f,2f,2f,2f,2f});
            ClimateCase("BWk",Koppen.BWk,new float[]{8f,8f,8f,8f,8f,8f,8f,8f,8f,8f,8f,8f},new float[]{2f,2f,2f,2f,2f,2f,2f,2f,2f,2f,2f,2f});
            ClimateCase("BSh",Koppen.BSh,new float[]{24f,24f,24f,24f,24f,24f,24f,24f,24f,24f,24f,24f},new float[]{40f,40f,40f,40f,40f,40f,40f,40f,40f,40f,40f,40f});
            ClimateCase("BSk",Koppen.BSk,new float[]{12f,12f,12f,12f,12f,12f,12f,12f,12f,12f,12f,12f},new float[]{20f,20f,20f,20f,20f,20f,20f,20f,20f,20f,20f,20f});
            ClimateCase("Csa",Koppen.Csa,new float[]{8f,9f,12f,16f,20f,24f,26f,25f,21f,16f,12f,9f},new float[]{100f,100f,100f,10f,10f,10f,10f,10f,10f,100f,100f,100f});
            ClimateCase("Csb",Koppen.Csb,new float[]{5f,6f,9f,12f,16f,19f,20f,19f,16f,12f,8f,6f},new float[]{100f,100f,100f,10f,10f,10f,10f,10f,10f,100f,100f,100f});
            ClimateCase("Csc",Koppen.Csc,new float[]{2f,3f,4f,6f,8f,11f,13f,11f,9f,6f,4f,2f},new float[]{100f,100f,100f,10f,10f,10f,10f,10f,10f,100f,100f,100f});
            ClimateCase("Cwa",Koppen.Cwa,new float[]{8f,9f,12f,16f,20f,24f,26f,25f,21f,16f,12f,9f},new float[]{5f,5f,5f,150f,150f,150f,150f,150f,150f,5f,5f,5f});
            ClimateCase("Cwb",Koppen.Cwb,new float[]{5f,6f,9f,12f,16f,19f,20f,19f,16f,12f,8f,6f},new float[]{5f,5f,5f,150f,150f,150f,150f,150f,150f,5f,5f,5f});
            ClimateCase("Cwc",Koppen.Cwc,new float[]{2f,3f,4f,6f,8f,11f,13f,11f,9f,6f,4f,2f},new float[]{5f,5f,5f,150f,150f,150f,150f,150f,150f,5f,5f,5f});
            ClimateCase("Cfa",Koppen.Cfa,new float[]{8f,9f,12f,16f,20f,24f,26f,25f,21f,16f,12f,9f},new float[]{80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f});
            ClimateCase("Cfb",Koppen.Cfb,new float[]{5f,6f,9f,12f,16f,19f,20f,19f,16f,12f,8f,6f},new float[]{80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f});
            ClimateCase("Cfc",Koppen.Cfc,new float[]{2f,3f,4f,6f,8f,11f,13f,11f,9f,6f,4f,2f},new float[]{80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f});
            ClimateCase("Dsa",Koppen.Dsa,new float[]{-10f,-8f,-2f,7f,14f,21f,24f,22f,16f,8f,0f,-6f},new float[]{100f,100f,100f,10f,10f,10f,10f,10f,10f,100f,100f,100f});
            ClimateCase("Dsb",Koppen.Dsb,new float[]{-14f,-12f,-6f,2f,10f,16f,20f,17f,11f,3f,-4f,-10f},new float[]{100f,100f,100f,10f,10f,10f,10f,10f,10f,100f,100f,100f});
            ClimateCase("Dsc",Koppen.Dsc,new float[]{-25f,-22f,-17f,-7f,2f,9f,15f,11f,4f,-4f,-14f,-21f},new float[]{100f,100f,100f,10f,10f,10f,10f,10f,10f,100f,100f,100f});
            ClimateCase("Dsd",Koppen.Dsd,new float[]{-42f,-40f,-25f,-12f,0f,8f,15f,11f,2f,-12f,-28f,-39f},new float[]{100f,100f,100f,10f,10f,10f,10f,10f,10f,100f,100f,100f});
            ClimateCase("Dwa",Koppen.Dwa,new float[]{-10f,-8f,-2f,7f,14f,21f,24f,22f,16f,8f,0f,-6f},new float[]{5f,5f,5f,150f,150f,150f,150f,150f,150f,5f,5f,5f});
            ClimateCase("Dwb",Koppen.Dwb,new float[]{-14f,-12f,-6f,2f,10f,16f,20f,17f,11f,3f,-4f,-10f},new float[]{5f,5f,5f,150f,150f,150f,150f,150f,150f,5f,5f,5f});
            ClimateCase("Dwc",Koppen.Dwc,new float[]{-25f,-22f,-17f,-7f,2f,9f,15f,11f,4f,-4f,-14f,-21f},new float[]{5f,5f,5f,150f,150f,150f,150f,150f,150f,5f,5f,5f});
            ClimateCase("Dwd",Koppen.Dwd,new float[]{-42f,-40f,-25f,-12f,0f,8f,15f,11f,2f,-12f,-28f,-39f},new float[]{5f,5f,5f,150f,150f,150f,150f,150f,150f,5f,5f,5f});
            ClimateCase("Dfa",Koppen.Dfa,new float[]{-10f,-8f,-2f,7f,14f,21f,24f,22f,16f,8f,0f,-6f},new float[]{80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f});
            ClimateCase("Dfb",Koppen.Dfb,new float[]{-14f,-12f,-6f,2f,10f,16f,20f,17f,11f,3f,-4f,-10f},new float[]{80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f});
            ClimateCase("Dfc",Koppen.Dfc,new float[]{-25f,-22f,-17f,-7f,2f,9f,15f,11f,4f,-4f,-14f,-21f},new float[]{80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f});
            ClimateCase("Dfd",Koppen.Dfd,new float[]{-42f,-40f,-25f,-12f,0f,8f,15f,11f,2f,-12f,-28f,-39f},new float[]{80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f});
            ClimateCase("ET",Koppen.ET,new float[]{-12f,-12f,-9f,-5f,0f,4f,8f,5f,1f,-5f,-10f,-12f},new float[]{40f,40f,40f,40f,40f,40f,40f,40f,40f,40f,40f,40f});
            ClimateCase("EF",Koppen.EF,new float[]{-10f,-10f,-10f,-10f,-10f,-10f,-10f,-10f,-10f,-10f,-10f,-10f},new float[]{20f,20f,20f,20f,20f,20f,20f,20f,20f,20f,20f,20f});
            ClimateCase("Af_southern_shift",Koppen.Af,new float[]{26f,26f,26f,26f,26f,26f,26f,26f,26f,26f,26f,26f},new float[]{100f,100f,100f,100f,100f,100f,100f,100f,100f,100f,100f,100f});
            ClimateCase("Am_southern_shift",Koppen.Am,new float[]{26f,26f,26f,26f,26f,26f,26f,26f,26f,26f,26f,26f},new float[]{200f,200f,200f,200f,200f,200f,40f,200f,200f,200f,200f,200f});
            ClimateCase("Aw_southern_shift",Koppen.Aw,new float[]{26f,26f,26f,26f,26f,26f,26f,26f,26f,26f,26f,26f},new float[]{100f,100f,100f,100f,100f,100f,5f,100f,100f,100f,100f,100f});
            ClimateCase("BWh_southern_shift",Koppen.BWh,new float[]{26f,26f,26f,26f,26f,26f,26f,26f,26f,26f,26f,26f},new float[]{2f,2f,2f,2f,2f,2f,2f,2f,2f,2f,2f,2f});
            ClimateCase("BWk_southern_shift",Koppen.BWk,new float[]{8f,8f,8f,8f,8f,8f,8f,8f,8f,8f,8f,8f},new float[]{2f,2f,2f,2f,2f,2f,2f,2f,2f,2f,2f,2f});
            ClimateCase("BSh_southern_shift",Koppen.BSh,new float[]{24f,24f,24f,24f,24f,24f,24f,24f,24f,24f,24f,24f},new float[]{40f,40f,40f,40f,40f,40f,40f,40f,40f,40f,40f,40f});
            ClimateCase("BSk_southern_shift",Koppen.BSk,new float[]{12f,12f,12f,12f,12f,12f,12f,12f,12f,12f,12f,12f},new float[]{20f,20f,20f,20f,20f,20f,20f,20f,20f,20f,20f,20f});
            ClimateCase("Csa_southern_shift",Koppen.Csa,new float[]{26f,25f,21f,16f,12f,9f,8f,9f,12f,16f,20f,24f},new float[]{10f,10f,10f,100f,100f,100f,100f,100f,100f,10f,10f,10f});
            ClimateCase("Csb_southern_shift",Koppen.Csb,new float[]{20f,19f,16f,12f,8f,6f,5f,6f,9f,12f,16f,19f},new float[]{10f,10f,10f,100f,100f,100f,100f,100f,100f,10f,10f,10f});
            ClimateCase("Csc_southern_shift",Koppen.Csc,new float[]{13f,11f,9f,6f,4f,2f,2f,3f,4f,6f,8f,11f},new float[]{10f,10f,10f,100f,100f,100f,100f,100f,100f,10f,10f,10f});
            ClimateCase("Cwa_southern_shift",Koppen.Cwa,new float[]{26f,25f,21f,16f,12f,9f,8f,9f,12f,16f,20f,24f},new float[]{150f,150f,150f,5f,5f,5f,5f,5f,5f,150f,150f,150f});
            ClimateCase("Cwb_southern_shift",Koppen.Cwb,new float[]{20f,19f,16f,12f,8f,6f,5f,6f,9f,12f,16f,19f},new float[]{150f,150f,150f,5f,5f,5f,5f,5f,5f,150f,150f,150f});
            ClimateCase("Cwc_southern_shift",Koppen.Cwc,new float[]{13f,11f,9f,6f,4f,2f,2f,3f,4f,6f,8f,11f},new float[]{150f,150f,150f,5f,5f,5f,5f,5f,5f,150f,150f,150f});
            ClimateCase("Cfa_southern_shift",Koppen.Cfa,new float[]{26f,25f,21f,16f,12f,9f,8f,9f,12f,16f,20f,24f},new float[]{80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f});
            ClimateCase("Cfb_southern_shift",Koppen.Cfb,new float[]{20f,19f,16f,12f,8f,6f,5f,6f,9f,12f,16f,19f},new float[]{80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f});
            ClimateCase("Cfc_southern_shift",Koppen.Cfc,new float[]{13f,11f,9f,6f,4f,2f,2f,3f,4f,6f,8f,11f},new float[]{80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f});
            ClimateCase("Dsa_southern_shift",Koppen.Dsa,new float[]{24f,22f,16f,8f,0f,-6f,-10f,-8f,-2f,7f,14f,21f},new float[]{10f,10f,10f,100f,100f,100f,100f,100f,100f,10f,10f,10f});
            ClimateCase("Dsb_southern_shift",Koppen.Dsb,new float[]{20f,17f,11f,3f,-4f,-10f,-14f,-12f,-6f,2f,10f,16f},new float[]{10f,10f,10f,100f,100f,100f,100f,100f,100f,10f,10f,10f});
            ClimateCase("Dsc_southern_shift",Koppen.Dsc,new float[]{15f,11f,4f,-4f,-14f,-21f,-25f,-22f,-17f,-7f,2f,9f},new float[]{10f,10f,10f,100f,100f,100f,100f,100f,100f,10f,10f,10f});
            ClimateCase("Dsd_southern_shift",Koppen.Dsd,new float[]{15f,11f,2f,-12f,-28f,-39f,-42f,-40f,-25f,-12f,0f,8f},new float[]{10f,10f,10f,100f,100f,100f,100f,100f,100f,10f,10f,10f});
            ClimateCase("Dwa_southern_shift",Koppen.Dwa,new float[]{24f,22f,16f,8f,0f,-6f,-10f,-8f,-2f,7f,14f,21f},new float[]{150f,150f,150f,5f,5f,5f,5f,5f,5f,150f,150f,150f});
            ClimateCase("Dwb_southern_shift",Koppen.Dwb,new float[]{20f,17f,11f,3f,-4f,-10f,-14f,-12f,-6f,2f,10f,16f},new float[]{150f,150f,150f,5f,5f,5f,5f,5f,5f,150f,150f,150f});
            ClimateCase("Dwc_southern_shift",Koppen.Dwc,new float[]{15f,11f,4f,-4f,-14f,-21f,-25f,-22f,-17f,-7f,2f,9f},new float[]{150f,150f,150f,5f,5f,5f,5f,5f,5f,150f,150f,150f});
            ClimateCase("Dwd_southern_shift",Koppen.Dwd,new float[]{15f,11f,2f,-12f,-28f,-39f,-42f,-40f,-25f,-12f,0f,8f},new float[]{150f,150f,150f,5f,5f,5f,5f,5f,5f,150f,150f,150f});
            ClimateCase("Dfa_southern_shift",Koppen.Dfa,new float[]{24f,22f,16f,8f,0f,-6f,-10f,-8f,-2f,7f,14f,21f},new float[]{80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f});
            ClimateCase("Dfb_southern_shift",Koppen.Dfb,new float[]{20f,17f,11f,3f,-4f,-10f,-14f,-12f,-6f,2f,10f,16f},new float[]{80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f});
            ClimateCase("Dfc_southern_shift",Koppen.Dfc,new float[]{15f,11f,4f,-4f,-14f,-21f,-25f,-22f,-17f,-7f,2f,9f},new float[]{80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f});
            ClimateCase("Dfd_southern_shift",Koppen.Dfd,new float[]{15f,11f,2f,-12f,-28f,-39f,-42f,-40f,-25f,-12f,0f,8f},new float[]{80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f});
            ClimateCase("ET_southern_shift",Koppen.ET,new float[]{8f,5f,1f,-5f,-10f,-12f,-12f,-12f,-9f,-5f,0f,4f},new float[]{40f,40f,40f,40f,40f,40f,40f,40f,40f,40f,40f,40f});
            ClimateCase("EF_southern_shift",Koppen.EF,new float[]{-10f,-10f,-10f,-10f,-10f,-10f,-10f,-10f,-10f,-10f,-10f,-10f},new float[]{20f,20f,20f,20f,20f,20f,20f,20f,20f,20f,20f,20f});
            ClimateCase("cold_exact_18",Koppen.Af,new float[]{18f,18f,18f,18f,18f,18f,18f,18f,18f,18f,18f,18f},new float[]{60f,60f,60f,60f,60f,60f,60f,60f,60f,60f,60f,60f});
            ClimateCase("hot_exact_10",Koppen.ET,new float[]{10f,10f,10f,10f,10f,10f,10f,10f,10f,10f,10f,10f},new float[]{80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f,80f});
            ClimateCase("hot_exact_0",Koppen.EF,new float[]{0f,0f,0f,0f,0f,0f,0f,0f,0f,0f,0f,0f},new float[]{40f,40f,40f,40f,40f,40f,40f,40f,40f,40f,40f,40f});
            ClimateCase("zero_rain_frozen",Koppen.EF,new float[]{-40f,-40f,-40f,-40f,-40f,-40f,-40f,-40f,-40f,-40f,-40f,-40f},new float[]{0f,0f,0f,0f,0f,0f,0f,0f,0f,0f,0f,0f});
            ClimateCase("zero_rain_hot",Koppen.BWh,new float[]{28f,28f,28f,28f,28f,28f,28f,28f,28f,28f,28f,28f},new float[]{0f,0f,0f,0f,0f,0f,0f,0f,0f,0f,0f,0f});
            bool rejected=false;try{KoppenClassifier.Classify(new[]{float.NaN},new float[12]);}catch(ArgumentException){rejected=true;}Check(rejected,"Reject invalid climate input");
            LastOperation="Rectangular tile/zone topology";
            var grid=new WorldData(64,32,"TEST",TerrainPreset.Continents);
            Check(grid.Count==2048&&grid.ZoneCount==128,"Independent tile count, not render subplot count");
            for(int z=0;z<grid.ZoneCount;z++)for(int p=0;p<16;p++)Check(grid.ZoneOf(grid.ZoneTile(z,p))==z,"Exactly 16 authoritative cells per zone");
            Check(grid.Neighbor(0,0)==63&&grid.Neighbor(63,1)==0,"Longitude wrap");
            Check(grid.Neighbor(0,2)==-1&&grid.Neighbor(grid.Count-1,3)==-1,"No north/south teleport");
            LastOperation="Seeded generation / hydrology / save roundtrip";
            foreach(TerrainPreset preset in Enum.GetValues(typeof(TerrainPreset)))
            {
                var a=FlatGenerator.Generate(64,32,"VALIDATE-2D",preset,CancellationToken.None);
                var b=FlatGenerator.Generate(64,32,"VALIDATE-2D",preset,CancellationToken.None);
                FlatSave.Validate(a);FlatSave.Validate(b);int[] rank=new int[a.Count];
                for(int j=0;j<a.Count;j++)rank[a.DrainOrder[j]]=j;
                double input=0,output=0;
                for(int i=0;i<a.Count;i++)
                {
                    Check(a.Elevation[i]==b.Elevation[i]&&a.Climates[i]==b.Climates[i],"Same seed has same terrain and climate");
                    for(int m=0;m<12;m++)Check(a.Temperature[i*12+m]==b.Temperature[i*12+m]&&a.Precipitation[i*12+m]==b.Precipitation[i*12+m],"Parallel generation deterministic within this runtime");
                    int parent=a.Downstream[i];if(parent>=0){Check(rank[parent]<rank[i],"Acyclic parent rank");Check(a.WaterLevel[parent]<=a.WaterLevel[i],"Non-uphill spill routing");}else output+=a.Runoff[i];
                    if(!(a.Water[i]>=WaterKind.DeepOcean&&a.Water[i]<=WaterKind.ShallowOcean)){float p=0;for(int m=0;m<12;m++)p+=a.Precipitation[i*12+m];input+=p/1200f;}
                }
                Check(Math.Abs(input-output)<Math.Max(.001,input*.0001),"Drainage conserves runoff accumulation");
                var sim=new FlatSimulation(a);sim.SeedNations(3);
                for(int day=0;day<60;day++)sim.Step(1);FlatSave.Validate(a);
                byte[] before=FlatSave.Encode(a),after=FlatSave.Encode(FlatSave.Decode(before));Check(before.Length==after.Length,"Save length roundtrip");
                for(int i=0;i<before.Length;i++)Check(before[i]==after[i],"Exact save payload roundtrip");
                before[before.Length-1]^=1;rejected=false;try{FlatSave.Decode(before);}catch(System.IO.InvalidDataException){rejected=true;}Check(rejected,"Corrupt save rejected");
            }
            LastOperation="Civilization tile ownership / population accounting";
            for(int i=0;i<grid.Count;i++)grid.Elevation[i]=100;
            FlatGenerator.Rebuild(grid,CancellationToken.None);var game=new FlatSimulation(grid);int center=grid.Index(30,16),nation=game.Found(center);
            Check(nation==0,"Found first civilization");Check(Math.Abs(game.TotalPopulation()-500)<.001,"500 people, not 16 copies of 500");
            int zone=grid.ZoneOf(center);for(int p=0;p<16;p++)Check(grid.Owner[grid.ZoneTile(zone,p)]==nation,"Transactional zone claim writes all sixteen cells");
            int target=grid.Index(32,16);bool annexed=game.Annex(target,nation);Check(annexed,"Adjacent land zone expands");Check(Math.Abs(game.TotalPopulation()-500)<.001,"Settlement transfer conserves population");
            int capital=grid.Nations[0].Capital;var path=FlatPathfinder.Find(grid,capital,target);Check(path.Length>0&&path[0]==capital&&path[path.Length-1]==target,"Path endpoints");
            double people=game.TotalPopulation();grid.Elevation[capital]=-4000;FlatGenerator.Rebuild(grid,CancellationToken.None);game.Reindex();
            Check(Math.Abs(game.TotalPopulation()-people)<.01,"Flood edit preserves population through refugee accounting");FlatSave.Validate(grid);
            LastOperation="Water / climate integration regressions";Assertions+=FlatWaterSelfTests.Run();
            LastOperation="Perf3.2 continuous surface regressions";Assertions+=FlatRenderSelfTests.Run();
            LastOperation="All core tests completed";watch.Stop();return "PASS / "+Assertions+" assertions / "+watch.ElapsedMilliseconds+" ms. This is core C#, not shader or visual acceptance.";
        }
    }
}
