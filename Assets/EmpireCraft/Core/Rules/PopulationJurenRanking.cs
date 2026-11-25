// Ported from the EmpireCraft mod: Scripts/GeneralSystems/PopulationJurenRanking.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace EmpireCraft.Core
{

// 主线程取得政绩快照。四个人口工作线程只排序 ID 与数值；不接触 Person、城市或 Unity。
public static class PopulationJurenRanking
{
    private sealed class Request
    {
        public Realm Owner;
        public PopulationNumericInput Input;
        public PopulationMathWorkers.Job Job;
    }
    private static ConditionalWeakTable<City, Request> Requests = new();
    private static City _activeCity;
    private static HashSet<long> _kept;
    public static int WorkerResults { get; private set; }
    public static int FallbackResults { get; private set; }

    public static void Reset()
    {
        Requests = new();
        EndPass();
        WorkerResults = FallbackResults = 0;
    }

#if EC_PENDING
    private static PopulationRankValue[] Capture(City city)
    {
        var values = new List<PopulationRankValue>();
        foreach (Person actor in city.units)
            if (actor?.Record != null && !actor.IsGone() && actor.Alive && actor.hasTrait("juren"))
                values.Add(new PopulationRankValue(actor.Key, actor.GetIdentity()?.TotalPerformance ?? 0d));
        return values.ToArray();
    }

    public static void Prewarm(IEnumerable<City> cities)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled) return;
        int visited = 0;
        foreach (City city in cities)
        {
            if (++visited > 4 || !SimulationFrameBudget.HasTime) break;
            if (city?.Record == null || city.IsGone() || city.units == null || city.Realm == null ||
                EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(city) ||
                Requests.TryGetValue(city, out _)) continue;
            var input = new PopulationNumericInput(Capture(city), 5);
            Requests.Add(city, new Request { Owner = city.Realm, Input = input,
                Job = input.Candidates.Length > 5 ? PopulationMathWorkers.Submit(input) : null });
        }
    }

    public static void BeginPass(City city)
    {
        EndPass();
        if (!CityPopulationSystem.AbstractPopulationEnabled || city?.units == null) return;
        using var timing = FrameProfiler.Measure("虚拟人口·举人排名");
        PopulationRankValue[] current = Capture(city);
        PopulationNumericResult result = null;
        if (Requests.TryGetValue(city, out Request request))
        {
            Requests.Remove(city);
            bool matches = request.Owner == city.Realm && request.Input.Candidates.Length == current.Length;
            for (int i = 0; matches && i < current.Length; i++)
                matches = request.Input.Candidates[i].Id == current[i].Id &&
                          request.Input.Candidates[i].Score.Equals(current[i].Score);
            if (matches && request.Job != null) request.Job.TryGetResult(out result);
        }
        // 队列满、未完成、迁城或政绩变化时直接按当前快照排一次；绝不等待后台。
        if (result == null)
        {
            FallbackResults++;
            result = PopulationMathWorkers.Compute(new PopulationNumericInput(current, 5));
        }
        else WorkerResults++;
        _activeCity = city;
        _kept = new HashSet<long>(result.RankedIds);
    }
#endif

    public static bool TryIsKept(Person actor, out bool kept)
    {
        kept = false;
        if (_activeCity == null || actor.City != _activeCity || _kept == null) return false;
        kept = _kept.Contains(actor.Key);
        return true;
    }

    public static void EndPass() { _activeCity = null; _kept = null; }
}
}
