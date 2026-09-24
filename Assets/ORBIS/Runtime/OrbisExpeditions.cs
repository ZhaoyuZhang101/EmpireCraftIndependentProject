using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orbis
{
    [Serializable]
    public sealed class Expedition
    {
        public int civ, tile, goal, index;
        public int[] path;
        public float progress, strength;
        public bool settlers, returning;
    }

    public sealed partial class OrbisWorld
    {
        public List<Expedition> expeditions = new List<Expedition>();
        public float expeditionClock;

        public bool CanSail(int civilization) => civs[civilization].tech >= 2;

        public int[] FindRoute(int start, int goal, int civilization, bool hostile)
        {
            if (start < 0 || goal < 0 || start >= tiles.Length || goal >= tiles.Length || civilization < 0 || civilization >= civs.Count) return null;
            var cost = new float[tiles.Length];
            var previous = new int[tiles.Length];
            var closed = new bool[tiles.Length];
            for (int i = 0; i < cost.Length; i++) { cost[i] = float.PositiveInfinity; previous[i] = -1; }
            var open = new MinHeap();
            cost[start] = 0; open.Push(start, 0);
            while (open.Count > 0)
            {
                int i = open.Pop();
                if (closed[i]) continue;
                if (i == goal)
                {
                    var result = new List<int>();
                    for (int p = goal; p >= 0; p = previous[p]) result.Add(p);
                    result.Reverse(); return result.ToArray();
                }
                closed[i] = true;
                foreach (int j in grid.neighbors[i])
                {
                    if (closed[j] || !Passable(j, goal, civilization, hostile)) continue;
                    float length = Vector3.Angle(grid.points[i], grid.points[j]) * Mathf.Deg2Rad;
                    float edge = length * (tiles[j].h <= 0 ? 1.7f : 1 + Mathf.Max(0, tiles[j].h) * 6);
                    edge += Mathf.Abs(tiles[i].h - tiles[j].h) * .22f;
                    if (tiles[i].basin != tiles[j].basin && tiles[j].h > 0) edge += .03f;
                    if (tiles[j].snow > 30) edge *= 1.4f;
                    float next = cost[i] + edge;
                    if (next >= cost[j]) continue;
                    cost[j] = next; previous[j] = i;
                    float heuristic = Vector3.Angle(grid.points[j], grid.points[goal]) * Mathf.Deg2Rad;
                    open.Push(j, next + heuristic);
                }
            }
            return null;
        }

        bool Passable(int tile, int goal, int civ, bool hostile)
        {
            Tile t = tiles[tile];
            if ((t.h <= 0 || t.lake > .018f) && !CanSail(civ)) return false;
            return t.owner < 0 || t.owner == civ || (tile == goal && hostile && (war || settings.autoWar));
        }

        public bool Dispatch(int home, bool settlers, int requestedGoal = -1)
        {
            if (home < 0 || home >= tiles.Length || tiles[home].owner < 0 || tiles[home].pop < 240 || expeditions.Count >= 48) return false;
            int civilization = tiles[home].owner;
            int active = 0; foreach (var e in expeditions) if (e.civ == civilization) active++;
            if (active >= 3) return false;
            if (!settlers && !war && !settings.autoWar) return false;
            int goal = requestedGoal;
            int[] route = null;
            if (goal >= 0)
            {
                if (goal >= tiles.Length || tiles[goal].h <= 0 || tiles[goal].lake > .018f) return false;
                if (settlers ? tiles[goal].owner >= 0 : tiles[goal].owner < 0 || tiles[goal].owner == civilization) return false;
                route = FindRoute(home, goal, civilization, !settlers);
            }
            else
            {
                var candidates = new List<int>();
                for (int i = 0; i < tiles.Length; i++)
                {
                    Tile t = tiles[i];
                    if (t.h <= 0 || t.lake > .018f || t.Fertility < .1f) continue;
                    if (settlers ? t.owner >= 0 : t.owner < 0 || t.owner == civilization) continue;
                    candidates.Add(i);
                }
                candidates.Sort((a, b) => RouteScore(home, a, settlers).CompareTo(RouteScore(home, b, settlers)));
                for (int k = 0; k < Mathf.Min(18, candidates.Count); k++)
                {
                    goal = candidates[k]; route = FindRoute(home, goal, civilization, !settlers);
                    if (route != null) break;
                }
            }
            if (route == null || route.Length < 2) return false;
            float strength = Mathf.Min(tiles[home].pop * (settlers ? .22f : .35f), settlers ? 700 : 1800);
            tiles[home].pop -= strength;
            expeditions.Add(new Expedition { civ = civilization, tile = home, goal = goal, path = route, strength = strength, settlers = settlers });
            Log(civs[civilization].name + (settlers ? "派出拓荒隊。" : "派出遠征軍。"));
            UpdateStats(); return true;
        }

        float RouteScore(int home, int goal, bool settlers)
        {
            float distance = Vector3.Angle(grid.points[home], grid.points[goal]);
            return distance + (settlers ? (1 - tiles[goal].Fertility) * 12 : tiles[goal].pop * .015f - (tiles[goal].city ? 5 : 0));
        }

        public void RecallExpeditions()
        {
            foreach (var e in expeditions)
            {
                if (e.settlers) continue;
                ReturnHome(e);
            }
        }

        bool ReturnHome(Expedition e)
        {
            int home = FindSafeLand(e.tile, e.civ);
            // FindSafeLand can return unclaimed land; a returning army must end on owned land.
            if (home < 0 || tiles[home].owner != e.civ)
            {
                home = -1; float nearest = float.PositiveInfinity;
                for (int i = 0; i < tiles.Length; i++) if (tiles[i].owner == e.civ && tiles[i].h > 0)
                { float d = Vector3.Angle(grid.points[e.tile], grid.points[i]); if (d < nearest) { nearest = d; home = i; } }
            }
            if (home < 0) return false;
            var route = FindRoute(e.tile, home, e.civ, false);
            if (route == null) return false;
            e.goal = home; e.path = route; e.index = 0; e.progress = 0; e.returning = true; return true;
        }

        void StepExpeditions(float days)
        {
            if (days <= 0) return;
            expeditionClock += days;
            if (settings.autoExpeditions && expeditionClock >= 45)
            {
                expeditionClock %= 45;
                for (int c = 0; c < civs.Count; c++)
                {
                    int home = -1; float population = 350;
                    for (int i = 0; i < tiles.Length; i++) if (tiles[i].owner == c && tiles[i].pop > population) { home = i; population = tiles[i].pop; }
                    if (home >= 0) Dispatch(home, !war && !settings.autoWar);
                }
            }
            for (int k = expeditions.Count - 1; k >= 0; k--)
            {
                Expedition e = expeditions[k];
                if (!e.settlers && !e.returning && !war && !settings.autoWar) ReturnHome(e);
                bool water = tiles[e.tile].h <= 0 || tiles[e.tile].lake > .018f;
                float travelDays = water ? 7 : 8 + Mathf.Max(0, tiles[e.tile].h) * 20;
                if (water && e.index + 1 < e.path.Length)
                {
                    Vector3 direction = Vector3.ProjectOnPlane(grid.points[e.path[e.index + 1]] - grid.points[e.tile], grid.points[e.tile]).normalized;
                    travelDays /= Mathf.Clamp(1 + Vector3.Dot(tiles[e.tile].current, direction) * .3f, .6f, 1.5f);
                }
                e.progress += days / travelDays;
                bool blocked = false;
                while (e.progress >= 1 && e.index + 1 < e.path.Length)
                {
                    int next = e.path[e.index + 1];
                    if (!Passable(next, e.goal, e.civ, !e.settlers && !e.returning))
                    {
                        var route = FindRoute(e.tile, e.goal, e.civ, !e.settlers && !e.returning);
                        if (route == null) { ReturnHome(e); blocked = true; break; }
                        e.path = route; e.index = 0; e.progress = 0; break;
                    }
                    e.progress -= 1; e.index++; e.tile = next;
                }
                // Stranded forces lose supply gradually; they never teleport across blocked terrain.
                if (blocked) { e.strength *= Mathf.Exp(-days / 120); e.progress = 0; }
                if (e.strength < 10) { Log(civs[e.civ].name + "的一支遠征隊因補給中斷而解散。"); expeditions.RemoveAt(k); continue; }
                if (e.index + 1 < e.path.Length) continue;
                Tile destination = tiles[e.tile];
                if (destination.h <= 0 || destination.lake > .018f) { if (!ReturnHome(e)) { e.strength *= Mathf.Exp(-days / 120); e.progress = 0; } continue; }
                if (destination.owner == e.civ) destination.pop += e.strength;
                else if (destination.owner < 0)
                {
                    destination.owner = e.civ; destination.pop += e.strength;
                    if (e.settlers) { destination.city = true; Log(civs[e.civ].name + "在" + PlaceName(e.tile) + "建立新聚落。"); }
                }
                else if (!e.settlers && !e.returning && (war || settings.autoWar))
                {
                    int defender = destination.owner;
                    float defense = destination.pop * (1 + Mathf.Max(0, destination.h) * .5f);
                    if (e.strength > defense * .8f)
                    { destination.pop = Mathf.Max(10, e.strength - defense * .6f) + destination.pop * .25f; destination.owner = e.civ; Log(civs[e.civ].name + "攻占了" + civs[defender].name + "的領地。"); }
                    else { destination.pop = Mathf.Max(0, destination.pop - e.strength * .65f); Log(civs[e.civ].name + "的遠征軍進攻失利。"); }
                }
                else { if (ReturnHome(e)) continue; e.strength *= .9f; e.progress = 0; continue; }
                expeditions.RemoveAt(k);
            }
        }
    }
}
