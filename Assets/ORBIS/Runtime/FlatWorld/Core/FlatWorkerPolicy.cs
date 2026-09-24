using System;
using System.Threading;
using System.Threading.Tasks;
namespace Orbis.Flat
{
    /// <summary>Shared cap for independent managed generation passes; Jobs use Unity's own worker pool.</summary>
    public static class FlatWorkerPolicy
    {
        public static int Resolve(int requested=0)
        {
            int available=Math.Max(1,Math.Min(8,Environment.ProcessorCount-2));
            return requested>0?Math.Min(requested,available):available;
        }
        public static ParallelOptions Options(CancellationToken cancel,int requested=0)
        {return new ParallelOptions{MaxDegreeOfParallelism=Resolve(requested),CancellationToken=cancel};}
    }
}
