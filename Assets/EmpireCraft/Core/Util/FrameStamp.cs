// Ported from the EmpireCraft mod: Scripts/HelperFunc/FrameStamp.cs @ e770dbbe. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using Time = EmpireCraft.Core.FrameClock;
namespace EmpireCraft.Core
{

// 模组自己的帧号：ModClass.Update 每帧(独立核心每步)加一。给"一帧里问很多回、答案这一帧不会变"的查询当缓存的期限
// (执政宗族、制度特性、世界提示的国名表……)。不用 UnityEngine.Time.frameCount：那个只许主线程读，工作线程一碰就抛异常
public static class FrameStamp
{
    public static int Current;
    // 主线程的编号(ModClass.Update 每帧记一回)：不加锁的缓存只许主线程用，别的线程照老办法现算
    public static int MainThread = -1;

    public static bool OnMainThread => global::System.Environment.CurrentManagedThreadId == MainThread;
}
}
