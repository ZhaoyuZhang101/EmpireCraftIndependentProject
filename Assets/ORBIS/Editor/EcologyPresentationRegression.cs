using System;
using System.Collections.Generic;
using System.IO;
using Orbis.Flat;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
    public static class EcologyPresentationRegression
    {
        [MenuItem("Tools/ORBIS/Local Planet/Validate ecology presentation data")]
        public static void Run()
        {
            var report=new List<string>();
            try
            {
                var ecologyField=typeof(FlatMapRenderJobs.Pixel).GetField("Ecology");
                Check(report,ecologyField!=null,"render jobs publish derived ecology metadata");
                var defaults=Resources.LoadAll<TextAsset>("ORBIS/WorldAtlas");
                var terrain=Array.Find(defaults,asset=>asset.name=="Default"&&FlatAtlasTerrain.HasSignature(asset.bytes));
                Check(report,terrain!=null&&AssetDatabase.GetAssetPath(terrain).EndsWith(".bytes"),"default terrain selection rejects same-name JSON metadata");
                Check(report,!FlatAtlasTerrain.HasSignature(new byte[]{79,65,84,49})&&!FlatAtlasTerrain.HasSignature(null),"truncated and missing atlas data fail signature validation");
                var world=FlatGenerator.Generate(64,32,"ECOLOGY-PRESENTATION",TerrainPreset.Continents,default);
                byte[] before=FlatSave.Encode(world);
                var first=new Color32[world.Count];
                using(var jobs=new FlatMapRenderJobs(world))
                {
                    for(int pass=0;pass<2;pass++)
                    {
                        world.MarkAllDirty();
                        jobs.Schedule(pass==0?MapLayer.Natural:MapLayer.Nations,true);
                        jobs.CompleteInitialWarmup();
                        if(!jobs.TryComplete(world,pass==0?MapLayer.Natural:MapLayer.Nations,out bool accepted)||!accepted)
                            throw new InvalidOperationException("Ecology render batch failed to publish.");
                        uint seed=FMath.Seed(world.Seed);
                        bool correct=true,stable=true;
                        foreach(var chunk in jobs.Chunks)
                        for(int y=0;y<chunk.Height;y++)for(int x=0;x<chunk.Width;x++)
                        {
                            int tile=(chunk.Y+y)*world.Width+chunk.X+x;
                            var packed=(Color32)ecologyField.GetValue(jobs.GetPixel(chunk.Offset+y*chunk.Width+x));
                            correct&=packed.r==(byte)world.Facts.Vegetation[tile]&&packed.g==(byte)world.Biomes[tile];
                            correct&=Math.Abs(packed.b/255f-world.Facts.Wetness[tile])<=1f/255;
                            correct&=packed.a==(byte)(FMath.Hash((uint)tile^seed)&255);
                            if(pass==0)first[tile]=packed;
                            else stable&=packed.Equals(first[tile]);
                        }
                        Check(report,correct,"cover, biome, wetness and seed match authoritative source values, pass="+pass);
                        if(pass>0)Check(report,stable,"visual traits stay stable across overlay and publication changes");
                    }
                }
                byte[] after=FlatSave.Encode(world);
                bool unchanged=before.Length==after.Length;
                for(int i=0;unchanged&&i<before.Length;i++)unchanged=before[i]==after[i];
                Check(report,unchanged,"ecology presentation preserves serialized world state byte-for-byte");
            }
            finally
            {
                Directory.CreateDirectory("ValidationOutput/Ecology");
                File.WriteAllLines("ValidationOutput/Ecology/presentation-data.txt",report);
            }
        }

        static void Check(List<string> report,bool passed,string description)
        {
            report.Add((passed?"PASS ":"FAIL ")+description);
            if(!passed)throw new InvalidOperationException(description);
        }
    }
}
