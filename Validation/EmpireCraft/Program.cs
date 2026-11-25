using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using EmpireCraft.Core;
using Orbis.Flat;

// EmpireCraft skeleton checks. Run from the repository root:
//   dotnet run --project Validation/EmpireCraft -c Release            (ops, determinism, saves, ported rules, data, books; 256 world)
//   dotnet run --project Validation/EmpireCraft -c Release -- --empire-smoke [width] [years] [outDir]
public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding=System.Text.Encoding.UTF8;
        try
        {
            if(args.Length>0&&args[0]=="--empire-smoke")
            {
                int width=args.Length>1?int.Parse(args[1]):1024,years=args.Length>2?int.Parse(args[2]):200;
                Smoke(width,years,args.Length>3?args[3]:null);
            }
            else if(args.Length>0&&args[0]=="--fingerprint")
                Fingerprint(args.Length>1?int.Parse(args[1]):256,args.Length>2?int.Parse(args[2]):75);
            else{Operations();Skeleton();RulesChecks.Rules();RulesChecks.MoreRules();RulesChecks.Generated();RulesChecks.Wars();RulesChecks.People();RulesChecks.Grid();RulesChecks.Scheduling();RulesChecks.Data();RulesChecks.Accounts();RulesChecks.Runner();}
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }

    // Hash of the saved world after a fixed run. A refactor that must not change behaviour
    // prints the same line before and after (compare on one machine and runtime).
    static void Fingerprint(int width,int years)
    {
        var map=FlatGenerator.Generate(width,width/2,"EMPIRE-FINGERPRINT",TerrainPreset.Continents,default);
        var w=EmpireRunner.Open(map,FMath.Seed(map.Seed));
        for(int m=0;m<years*12;m++)EmpireSimulation.Month(w);
        var bytes=EmpireSave.Encode(w,2);   // the layout the first fingerprints were taken on
        string hash=BitConverter.ToString(System.Security.Cryptography.SHA256.HashData(bytes)).Replace("-","");
        Console.WriteLine($"fingerprint {width}x{width/2} {years}y: {hash} ({bytes.Length} bytes, {w.Realms.Count} realms, {w.Cities.Count} cities, {w.People.Count} people, {w.Annals.Entries.Count} annals)");
    }

    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static void Throws(Action action,string message)
    {
        checks++;try{action();}catch(InvalidOperationException){return;}
        throw new Exception("Expected refusal: "+message);
    }
    static void Consistent(EmpireWorld w,string when)
    {
        var errors=EmpireInvariants.Check(w);checks++;
        if(errors.Count>0)throw new Exception(when+": "+string.Join("; ",errors));
    }

    // World operations keep the invariants and refuse illegal changes atomically.
    static void Operations()
    {
        var map=FlatGenerator.Generate(256,128,"EMPIRE-OPS",TerrainPreset.Continents,default);
        var w=new EmpireWorld(map,7);
        var sites=Enumerable.Range(0,map.Count).Where(w.Settleable).OrderByDescending(t=>map.Fertility[t]).ToList();
        Clan clan=WorldOps.NewClan(w,"林");
        Person king=WorldOps.Birth(w,"远",Sex.Male,clan,null,null,null,40);
        Person queen=WorldOps.Birth(w,"清",Sex.Female,WorldOps.NewClan(w,"沈"),null,null,null,36);
        Realm a=WorldOps.FoundRealmAt(w,"甲国",0xaa3333,sites[0],"甲城",king);
        WorldOps.Marry(w,king,queen);
        int far=sites.First(t=>w.Distance(t,sites[0])>12);
        City b=WorldOps.FoundCity(w,far,a,"乙城");
        Consistent(w,"after founding");
        Throws(()=>WorldOps.FoundCity(w,sites[0],a,"重复"),"second city on an occupied seat");
        Throws(()=>WorldOps.Marry(w,king,queen),"remarriage");
        Throws(()=>WorldOps.EndRealm(w,a),"ending a realm that still holds cities");
        Throws(()=>WorldOps.MoveCapital(w,a,null),"no capital city");
        Throws(()=>WorldOps.MoveCapital(w,a,new City{Id=99}),"a city the world does not hold");
        Person son=WorldOps.Birth(w,"承",Sex.Male,clan,a.Capital,king,queen,18);
        Clan rebelClan=WorldOps.NewClan(w,"韩");Person rebel=WorldOps.Birth(w,"霆",Sex.Male,rebelClan,b,null,null,30);
        Realm r2=WorldOps.FoundRealm(w,"乙国",0x3333aa,b,rebel);
        Check(b.Realm==r2&&a.Cities.Count==1,"breakaway moves the city");
        Throws(()=>WorldOps.MoveCapital(w,a,b),"capital outside the realm");
        Consistent(w,"after breakaway");
        WorldOps.Death(w,king);
        Check(a.Ruler==null&&a.PreviousRuler==king&&queen.Spouse==null,"death vacates throne and marriage");
        WorldOps.SetRuler(w,a,son);Consistent(w,"after succession");
        Throws(()=>WorldOps.SetRuler(w,r2,son),"one person ruling two realms");
        WorldOps.TransferCity(w,a.Capital,r2);
        Check(!a.Alive&&r2.Cities.Count==2,"losing the last city ends the realm");
        Consistent(w,"after conquest");
        Console.WriteLine("PASS world operations ("+checks+" checks)");
    }

    static EmpireWorld Start(WorldData map,int cities,int realms)
    {
        var w=new EmpireWorld(map.Clone(),FMath.Seed(map.Seed));
        EmpireBootstrap.Seed(w,cities,realms);return w;
    }
    static void Run(EmpireWorld w,int years,Action<EmpireWorld> yearly=null)
    {
        for(int m=0;m<years*12;m++){SkeletonRules.Month(w);if(w.Month%12==0)yearly?.Invoke(w);}
    }
    // The full step: skeleton rules plus the books kept by the ported rule sets.
    static void RunAll(EmpireWorld w,int years,Action<EmpireWorld> yearly=null)
    {
        for(int m=0;m<years*12;m++){EmpireSimulation.Month(w);if(w.Month%12==0)yearly?.Invoke(w);}
    }

    // Deterministic, consistent and save-stable on a small world.
    static void Skeleton()
    {
        var map=FlatGenerator.Generate(256,128,"EMPIRE-SKELETON",TerrainPreset.Continents,default);
        var a=Start(map,60,10);Consistent(a,"opening");
        Check(a.Cities.Count==60&&a.Realms.Count>=10,"opening count "+a.Cities.Count+" cities "+a.Realms.Count+" realms");
        Run(a,60,w=>Consistent(w,"year "+w.Year));
        var b=Start(map,60,10);Run(b,60);
        Check(EmpireSave.Encode(a).SequenceEqual(EmpireSave.Encode(b)),"same seed must give the same 60 years");
        var bytes=EmpireSave.Encode(a);var loaded=EmpireSave.Decode(bytes,a.Map);
        Check(EmpireSave.Encode(loaded).SequenceEqual(bytes),"save round trip");
        Run(a,15);Run(loaded,15);
        Check(EmpireSave.Encode(a).SequenceEqual(EmpireSave.Encode(loaded)),"a loaded world continues identically");
        bool refused=false;try{EmpireSave.Decode(bytes,FlatGenerator.Generate(256,128,"OTHER-MAP",TerrainPreset.Continents,default));}catch(InvalidDataException){refused=true;}
        Check(refused,"a section must not load onto another map");
        Console.WriteLine($"PASS skeleton 60+15 years: {a.Realms.Count(r=>r.Alive)}/{a.Realms.Count} realms alive, {a.Cities.Count} cities, {a.Living.Count}/{a.People.Count} people living, {a.Annals.Entries.Count} annal entries ({checks} checks)");
    }

    static void Smoke(int width,int years,string outDir)
    {
        var clock=Stopwatch.StartNew();
        var map=FlatGenerator.Generate(width,width/2,"ORBIS-2D-2026",TerrainPreset.Continents,default);
        long generation=clock.ElapsedMilliseconds;
        int cities=Math.Max(30,300*width*width/(1024*1024)),realms=Math.Max(6,40*width/1024);
        clock.Restart();var w=Start(map,cities,realms);long seeding=clock.ElapsedMilliseconds;
        Consistent(w,"opening");
        if(outDir!=null)MapImage.Write(w,Path.Combine(outDir,"realms_year0.png"));
        var yearMs=new System.Collections.Generic.List<double>();var yearClock=new Stopwatch();
        FrameProfiler.Reset();FrameProfiler.Enabled=true;
        yearClock.Start();
        RunAll(w,years,x=>{yearMs.Add(yearClock.Elapsed.TotalMilliseconds);Consistent(x,"year "+x.Year);yearClock.Restart();});
        var bytes=EmpireSave.Encode(w);var loaded=EmpireSave.Decode(bytes,w.Map);
        Check(EmpireSave.Encode(loaded).SequenceEqual(bytes),"save round trip");
        FrameProfiler.Enabled=false;
        RunAll(w,10);RunAll(loaded,10);
        Check(EmpireSave.Encode(w).SequenceEqual(EmpireSave.Encode(loaded)),"loaded world continues identically");
        if(outDir!=null)MapImage.Write(w,Path.Combine(outDir,"realms_year"+years+".png"));
        yearMs.Sort();
        int claimed=w.TileCity.Count(c=>c>=0);
        Console.WriteLine($"{width}x{width/2}: generation {generation} ms, seeding {seeding} ms, year median {yearMs[yearMs.Count/2]:F2} ms, p95 {yearMs[yearMs.Count*95/100]:F2} ms, max {yearMs[^1]:F2} ms");
        Console.WriteLine($"after {years}+10 years: {w.Realms.Count(r=>r.Alive)}/{w.Realms.Count} realms alive, {w.Cities.Count} cities, {claimed} tiles held, {w.Living.Count}/{w.People.Count} people living, save {bytes.Length/1024} KiB");
        Console.WriteLine("books: "+RulesChecks.Summary(w));
        Console.WriteLine($"per simulated year: skeleton {FrameProfiler.Milliseconds("empire.skeleton")/years:F2} ms, monthly books {FrameProfiler.Milliseconds("empire.accounts.month")/years:F2} ms, yearly close {FrameProfiler.Milliseconds("empire.accounts.year")/years:F3} ms");
        foreach(var e in w.Annals.Entries.Where(e=>e.Kind!=AnnalKind.CityFounded).Take(6))Console.WriteLine("  "+Annals.Format(w,e));
        Console.WriteLine("  ...");
        foreach(var e in w.Annals.Entries.Skip(Math.Max(0,w.Annals.Entries.Count-6)))Console.WriteLine("  "+Annals.Format(w,e));
        Console.WriteLine("PASS empire smoke ("+checks+" checks)");
    }
}
