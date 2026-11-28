using System;
using System.IO;
using System.IO.Compression;

namespace EmpireCraft.Core
{
    // The ported systems' state in a save. The mod wrote it beside the base game's save as one file
    // (DataManager.SaveAll: every realm's, city's, person's, war's and clan's extra data, the empires and their titles,
    // offices, wilderness, settlements, roads, decisions, companies, the systems' own records) and read it back after
    // the base game had rebuilt its world (DataManager.LoadAll). Here that file rides inside the core's save section
    // (EmpireSave, version 8), compressed, after the base layout.
    //
    // The ported systems keep their state in statics that belong to the current world (they are reset when another
    // world becomes current, see WorldSwitch), so only the current world has any to write. A loaded world keeps the
    // state it was saved with until it becomes current (EmpireWorld.Activate), and then it is put back.
    //
    // The mod's save and load go through a file in a folder; a folder of their own under the system's temporary folder
    // is made for each and removed after.
    public static class PortedSave
    {
        public static bool Applies(EmpireWorld w) => PortedSystems.Enabled && w != null && ReferenceEquals(EmpireWorld.Current, w);

        public static byte[] Capture(EmpireWorld w)
        {
            if (!Applies(w)) return Array.Empty<byte>();
            string folder = Folder();
            try
            {
                DataManager.SaveAll(folder);
                string file = Path.Combine(folder, DataManager.EmpireCraftSaveFileName);
                return File.Exists(file) ? Pack(File.ReadAllBytes(file)) : Array.Empty<byte>();
            }
            finally { Remove(folder); }
        }

        public static void Restore(EmpireWorld w, byte[] packed)
        {
            if (packed == null || packed.Length == 0 || !ReferenceEquals(EmpireWorld.Current, w)) return;
            string folder = Folder();
            try
            {
                File.WriteAllBytes(Path.Combine(folder, DataManager.EmpireCraftSaveFileName), Unpack(packed));
                DataManager.LoadAll(folder);
            }
            finally { Remove(folder); }
        }

        static string Folder()
        {
            string folder = Path.Combine(Path.GetTempPath(), "EmpireCraftSave-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        static void Remove(string folder)
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static byte[] Pack(byte[] raw)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true)) gzip.Write(raw, 0, raw.Length);
            return output.ToArray();
        }

        static byte[] Unpack(byte[] packed)
        {
            using var input = new GZipStream(new MemoryStream(packed), CompressionMode.Decompress);
            using var output = new MemoryStream();
            input.CopyTo(output);
            return output.ToArray();
        }
    }
}
