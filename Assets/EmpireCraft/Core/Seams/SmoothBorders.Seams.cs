namespace EmpireCraft.Core
{
    // Hand-written half of SmoothBorders: the map's borders are drawn by the client. When the front takes a cell the
    // mod told its painter to show the new owner at once (Settle) and described what the painter showed (DescribeDisplay,
    // for diagnostics). The owner is on the record already; there is nothing to paint or describe here.
    public static partial class SmoothBorders
    {
        public static void Settle(Cell zone) { }
        internal static string DescribeDisplay(Cell zone) => "";
        // The whole-map canvas the mod painted borders on is the client's; it is never up here.
        internal static bool CanvasActive => false;
    }
}
