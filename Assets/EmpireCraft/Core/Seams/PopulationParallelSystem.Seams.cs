using System.Collections.Generic;

namespace EmpireCraft.Core
{
    // Hand-written half of PopulationParallelSystem: PrewarmWorkforce. It worked out, ahead of the cities' month, the
    // workforce for the economy step (jobs in buildings). The economy step is not run yet (see the CityPopulationSystem
    // seam), so there is no workforce to prepare.
    public static partial class PopulationParallelSystem
    {
        public static void PrewarmWorkforce(IEnumerable<City> cities, double now) { }

        // Submit: the mod worked out a city's growth on background threads and picked the result up a frame or two
        // later. Here a month is one step and the cities are settled in a fixed order, so growth is worked out in place
        // (no job: the caller computes it at once). The sums are the same wherever they are done; only the order of
        // the cities' month around them could change with the threads' timing, and now it cannot.
        private static PopulationMathWorkers.Job Submit(PopulationNumericInput input) => null;
    }
}
