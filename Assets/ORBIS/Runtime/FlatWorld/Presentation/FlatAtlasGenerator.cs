using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
namespace Orbis.Flat
{
    // Offline physical generation. The game consumes the portable cache and has no Python dependency.
    public static class FlatAtlasGenerator
    {
        public static WorldData Generate(string project,int width,string seed,byte[] baked,CancellationToken cancel)
        {
            if(baked!=null&&width==512&&seed=="ORBIS-2D-2026")return FlatAtlasTerrain.Read(baked,seed,cancel);
            string output=Path.Combine(project,"Library/WorldAtlas/spherical-relief-2",FMath.Seed(seed).ToString("x8")+"-"+width+".bytes");
            if(!File.Exists(output))
            {
                string python=Path.Combine(project,"Tools/WorldAtlas/.venv/Scripts/python.exe");
                if(!File.Exists(python))throw new InvalidOperationException("此种子尚未生成 World Atlas 数据；请在编辑器中生成并保存世界，或导入高度图。");
                // Pass a seed file instead of shell interpolation (quotes/newlines are valid world names).
                Directory.CreateDirectory(Path.GetDirectoryName(output));string seedFile=output+".seed.txt";File.WriteAllText(seedFile,seed);
                var info=new ProcessStartInfo(python){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=project,
                    Arguments="\""+Path.Combine(project,"Tools/WorldAtlas/export_orbis.py")+"\" --seed-file \""+seedFile+"\" --width "+width+" --output \""+output+"\""};
                using(var process=Process.Start(info))
                {while(!process.WaitForExit(100)){if(cancel.IsCancellationRequested){process.Kill();cancel.ThrowIfCancellationRequested();}}if(process.ExitCode!=0)throw new InvalidOperationException("World Atlas 地理生成失败，现有世界未替换。");}
            }
            cancel.ThrowIfCancellationRequested();return FlatAtlasTerrain.Read(File.ReadAllBytes(output),seed,cancel);
        }
    }
}
