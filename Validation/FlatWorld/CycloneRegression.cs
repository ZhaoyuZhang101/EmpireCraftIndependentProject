using System;
using Orbis.Flat;

public static class CycloneRegression
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    public static void Run()
    {
        var world=new WorldData(64,32,"CYCLONE-CLIMATE",TerrainPreset.Continents);
        for(int i=0;i<world.Count;i++)
        {
            world.Water[i]=WaterKind.DeepOcean;
            world.CoastDistance[i]=0; // ocean cells use zero in the real world data
            for(int m=0;m<12;m++){world.Temperature[i*12+m]=16;world.Precipitation[i*12+m]=45;}
        }
        WarmWet(world,14,20,31,310);
        WarmWet(world,47,11,30,280);
        WarmWet(world,39,20,17,540); // rain alone cannot make a tropical cyclone
        WarmWet(world,29,11,32,30);  // warmth alone cannot make one either
        var north=FlatCycloneSelection.Find(world,6,true);
        var south=FlatCycloneSelection.Find(world,6,false);
        Check(north.Strength>.2f&&Math.Abs(north.X-14)<=1&&Math.Abs(north.Y-20)<=1,"north centre must follow warm wet ocean climate");
        Check(south.Strength>.2f&&Math.Abs(south.X-47)<=1&&Math.Abs(south.Y-11)<=1,"south centre must follow warm wet ocean climate");
        world.Water[20*64+14]=WaterKind.Land;
        north=FlatCycloneSelection.Find(world,6,true);
        Check(!north.Active,"land is not an ocean cyclone source");
        Console.WriteLine("PASS climate-selected tropical ocean cyclones, warm/wet gating and land exclusion");
    }
    static void WarmWet(WorldData world,int x,int y,float temp,float rain)
    {
        int i=y*world.Width+x;
        for(int m=0;m<12;m++){world.Temperature[i*12+m]=temp;world.Precipitation[i*12+m]=rain;}
    }
}
