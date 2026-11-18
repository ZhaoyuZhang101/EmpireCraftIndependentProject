using System;
using System.IO;
using System.Threading.Tasks;
using Orbis.Flat;
using UnityEngine;

// From the project root: unity command --detach run_script Tools/CaptureWorldSnapshot.cs --timeout_ms 120000
// The completed job returns the snapshot path: unity job status <job-id>
public static class CaptureWorldSnapshot
{
    public static async Task<string> Run()
    {
        var map = UnityEngine.Object.FindFirstObjectByType<FlatMapRenderer>();
        if (map == null || map.World == null)
            throw new InvalidOperationException("Open a running ORBIS world before capturing a snapshot.");

        // Copy one simulation tick on the editor thread. Validation, encoding,
        // hashing and disk I/O only see the detached copy and can run on a worker.
        var snapshot = map.World.Clone();
        string name = "world-" + Guid.NewGuid().ToString("N") + ".orbis";
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/OrbisSnapshots", name));
        await Task.Run(() => FlatSave.WriteAtomic(path, snapshot)).ConfigureAwait(false);
        return path;
    }
}
