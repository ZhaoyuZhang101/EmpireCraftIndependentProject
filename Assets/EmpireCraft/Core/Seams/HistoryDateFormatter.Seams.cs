using System.Text.RegularExpressions;

namespace EmpireCraft.Core
{
    // Hand-written half of HistoryDateFormatter: TryParseTime. The mod turned a date the base game wrote ("8,682年6月30日",
    // a year of 60 seconds, a month of 5) back into world time. Here dates are written by GameClock.DateText ("<year>年<month>月",
    // the year counted from the world's start year) and world time counts months, so a date reads back as the month it names.
    // A day of the month, when given, does not change the month.
    public static partial class HistoryDateFormatter
    {
        private static readonly Regex ChineseYearMonth = new Regex(@"(-?[\d,]+)\s*年\s*(\d+)\s*月", RegexOptions.Compiled);

        public static bool TryParseTime(string date, out double time)
        {
            time = 0d;
            if (string.IsNullOrWhiteSpace(date)) return false;
            Match match = ChineseYearMonth.Match(date);
            if (!match.Success || !int.TryParse(match.Groups[1].Value.Replace(",", ""), out int year) ||
                !int.TryParse(match.Groups[2].Value, out int month) || month < 1 || month > 12) return false;
            var world = EmpireWorld.Current;
            int start = world == null ? 1 : world.StartYear;
            time = (year - start) * 12d + (month - 1);
            return true;
        }
    }
}
