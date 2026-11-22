using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orbis.Planet;
using UnityEngine;

namespace Orbis.Flat
{
    public sealed partial class FlatWorldApp
    {
        const string SeaRouteToolName = "海上航线（选择两处海面）";
        FlatSeaPoint? seaOrigin;
        Task<FlatStraitNavigation> seaSurvey;
        bool seaSurveyIsGlobal;
        Task<SeaPreview> seaRequest;
        CancellationTokenSource seaCancellation;

        readonly struct SeaPreview
        {
            public readonly string Message;
            public readonly bool KeepOrigin;
            public readonly FlatSeaPoint[] Points;
            public SeaPreview(string message, bool keepOrigin, FlatSeaPoint[] points = null)
            { Message = message; KeepOrigin = keepOrigin; Points = points; }
        }

        Task<FlatStraitNavigation> SurveyFor(WorldData world)
        {
            if (!world.UsesSurveyedStraits) return Task.FromResult<FlatStraitNavigation>(null);
            if (seaSurvey == null || seaSurvey.IsFaulted || seaSurveyIsGlobal != world.UsesGlobalStraits)
            {
                string key = world.UsesGlobalStraits ? "EarthGlobalStraitShore" : "EarthStraitShore";
                var asset = Resources.Load<TextAsset>("ORBIS/FlatWorld/" + key);
                if (!asset) throw new InvalidOperationException("缺少地球海峡岸线资料。");
                var bytes = asset.bytes;
                seaSurveyIsGlobal = world.UsesGlobalStraits;
                seaSurvey = Task.Run(() => FlatStraitNavigation.Read(bytes));
            }
            return seaSurvey;
        }

        void CancelSeaRequest()
        {
            if (seaCancellation == null) return;
            var source = seaCancellation;
            source.Cancel();
            seaRequest.ContinueWith(t => { if (t.IsFaulted) { var observed = t.Exception; } source.Dispose(); },
                TaskScheduler.Default);
            seaRequest = null;
            seaCancellation = null;
        }

        void ResetSeaRoute()
        {
            CancelSeaRequest();
            seaOrigin = null;
            if (navigation) navigation.SetRoute(Array.Empty<int>());
        }

        void PickSeaPoint(PlanetLocation location)
        {
            var point = new FlatSeaPoint(location.Longitude * 180 / Math.PI, location.Latitude * 180 / Math.PI);
            var first = seaOrigin;
            var world = World;
            CancelSeaRequest();
            navigation.SetRoute(Array.Empty<int>());
            Task<FlatStraitNavigation> survey;
            try { survey = SurveyFor(world); }
            catch (Exception e) { SetStatus(e.Message); return; }
            if (!first.HasValue) seaOrigin = point;
            seaCancellation = new CancellationTokenSource();
            var token = seaCancellation.Token;
            seaRequest = Task.Run(async () =>
            {
                var chart = await survey.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var planner = new FlatSeaNavigation(world, chart);
                if (!planner.IsWater(first ?? point)) return new SeaPreview("出发点在陆地或内陆水域，请点击海面。", false);
                if (!first.HasValue) return new SeaPreview("已选出发点，点击目的海面查看航线。", true);
                if (!planner.IsWater(point)) return new SeaPreview("目的地在陆地或内陆水域，请另选一处海面。", true);
                var path = planner.Find(first.Value, point, token);
                if (path.Length == 0) return new SeaPreview("两点之间没有可通航的海路。可另选目的地，或右键重选出发点。", true);
                if (path.Length == 1) return new SeaPreview("请在另一处海面选择目的地。", true);
                return new SeaPreview("已显示海上航线。再次点击海面可规划下一条。", false, path);
            }, token);
            SetStatus(first.HasValue ? "正在查找连续水路…" : "正在核对出发海面…");
        }

        void PublishSeaRoute()
        {
            if (seaRequest == null || !seaRequest.IsCompleted) return;
            var completed = seaRequest;
            seaRequest = null;
            seaCancellation.Dispose();
            seaCancellation = null;
            try
            {
                var result = completed.GetAwaiter().GetResult();
                if (!result.KeepOrigin) seaOrigin = null;
                if (result.Points != null) navigation.SetGeographicRoute(DisplayPoints(result.Points), true);
                SetStatus(result.Message);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { seaOrigin = null; SetStatus("航线计算失败：" + e.Message); Debug.LogException(e); }
        }

        // Split long legs for the curved globe and local chart clipping. Interpolate
        // in the same unwrapped geographic coordinates as the validated sailing leg.
        static PlanetLocation[] DisplayPoints(FlatSeaPoint[] path)
        {
            const double rad = Math.PI / 180;
            var points = new List<PlanetLocation>(path.Length) { new PlanetLocation(path[0].Longitude * rad, path[0].Latitude * rad) };
            for (int i = 1; i < path.Length; i++)
            {
                var a = path[i - 1]; var b = path[i];
                double dx = PlanetCoordinateSystem.NearestOffset(b.Longitude - a.Longitude, 360), dy = b.Latitude - a.Latitude;
                double east = dx * Math.Cos((a.Latitude + b.Latitude) * .5 * rad);
                int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(east * east + dy * dy)));
                for (int j = 1; j < steps; j++)
                {
                    double t = (double)j / steps;
                    points.Add(new PlanetLocation((a.Longitude + dx * t) * rad, (a.Latitude + dy * t) * rad));
                }
                points.Add(new PlanetLocation(b.Longitude * rad, b.Latitude * rad));
            }
            return points.ToArray();
        }
    }
}
