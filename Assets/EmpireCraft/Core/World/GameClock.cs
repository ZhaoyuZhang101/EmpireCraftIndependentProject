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
        // A world is loaded and being simulated; the host may pause it.
        public static bool Running => EmpireWorld.Current != null;
        public static bool Paused;
        public static int MonthsSince(double stamp) => (int)Math.Floor(Now - stamp);
        public static int YearsSince(double stamp) => (int)Math.Floor((Now - stamp) / 12d);
        // How much a timestamp grows in a month: stamps are world months.
        public const double MonthLength = 1d;
        // The month of the year (1-12) and the written date of a stamp.
        public static int MonthOf(double stamp) { int month = (int)Math.Floor(stamp) % 12; return (month < 0 ? month + 12 : month) + 1; }
        public static int YearOf(double stamp) { var w = EmpireWorld.Current; return (w == null ? 1 : w.StartYear) + (int)Math.Floor(stamp / 12d); }
        public static string DateText(double stamp) => YearOf(stamp) + "年" + MonthOf(stamp) + "月";
    }
}
