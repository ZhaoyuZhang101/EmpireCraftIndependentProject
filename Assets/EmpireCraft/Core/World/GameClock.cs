using System;

namespace EmpireCraft.Core
{
    // The calendar the ported systems read. A timestamp is a world month (a double,
    // so a system may stamp part-months); spans are whole months or years between a
    // stamp and now. Everything reads the current world.
    public static class GameClock
    {
        public static double Now { get { var w = EmpireWorld.Current; return w == null ? 0d : w.Month; } }
        public static int Year { get { var w = EmpireWorld.Current; return w == null ? 0 : w.Year; } }
        public static int Month { get { var w = EmpireWorld.Current; return w == null ? 1 : w.MonthOfYear; } }
        public static int MonthsSince(double stamp) => (int)Math.Floor(Now - stamp);
        public static int YearsSince(double stamp) => (int)Math.Floor((Now - stamp) / 12d);
    }
}
