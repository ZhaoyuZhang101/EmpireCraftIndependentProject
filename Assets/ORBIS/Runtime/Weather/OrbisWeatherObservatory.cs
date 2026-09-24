using System;
using UnityEngine;
namespace Orbis
{
    public sealed partial class OrbisDemo
    {
        public void FindWeather(bool thunder)
        {
            var a=World.Weather.Atmosphere;int best=-1;double score=0;
            for(int i=0;i<World.tiles.Length;i++)
            {
                double candidate=thunder?(a.Stage[i]==3?a.Charge[i]+a.Updraft[i]+a.RainRate[i]:0):a.RainRate[i]*(World.tiles[i].h>0?1.15:1);
                if(candidate>score){score=candidate;best=i;}
            }
            if(best<0||score<.02)
            {Notice=thunder?"目前沒有成熟雷暴。可觀察發展中的積雲，或使用明確標示的對流試驗。":"目前沒有地面降雨；請讓水汽輸運與凝結繼續演化。";hud.Refresh();return;}
            SelectTile(best);FocusSelected();
            Notice=(thunder?"已定位活躍雷暴。":"已定位正在降雨的區域。")+"按「雨中近景」進入同一地點。";hud.Refresh();
        }
        public void RainSurfaceView()
        {
            if(Selected<0)FindWeather(false);
            if(Selected<0)return;
            StopAtmosphereFlight();if(MapMode)ToggleMap();FocusSelected();altitude=.0024f;lookTilt=72;lookHeading=0;AutoOrbit=false;
            Notice="近地雨滴讀取此處的實際降水；晴天不會強制播放雨。濕潤地面會在雨停後逐步乾燥。";hud.Refresh();
        }
        public void ConvectiveExperiment()
        {
            if(Selected<0){Notice="先選擇一個地區再進行對流試驗。";hud.Refresh();return;}
            var core=World.Weather.Atmosphere;Vector3 center=World.grid.points[Selected];int count=0;
            for(int i=0;i<World.tiles.Length;i++)if(Vector3.Dot(center,World.grid.points[i])>.987f)
            {core.AddWarmMoistBubble(i);count++;}
            World.Weather.Publish();View.UpdateWeather(true);
            Notice="對流試驗：已擾動 "+count+" 個大氣柱的溫濕條件；水汽取自地表水庫，不直接生成雲、雨或閃電。";hud.Refresh();
        }
        public string WeatherStatus()
        {
            var core=World.Weather.Atmosphere;int raining=0,storms=0;double maxRain=0;
            for(int i=0;i<core.Count;i++){if(core.RainRate[i]>.1)raining++;if(core.Stage[i]==3)storms++;maxRain=Math.Max(maxRain,core.RainRate[i]);}
            string text="12 層 · 降雨地塊 "+raining+" · 成熟對流 "+storms+"\n最高雨強 "+maxRain.ToString("F1")+" mm/h · 雷電事件 "+core.LightningCount;
            if(Selected>=0)
            {
                int i=Selected;
                text+="\n本地 "+core.RainRate[i].ToString("F2")+" mm/h · 濕潤 "+(core.Wetness[i]*100).ToString("F0")+"%";
                text+="\n雲底／頂 "+(core.CloudTop[i]>0?core.CloudBase[i].ToString("F0")+" / "+core.CloudTop[i].ToString("F0"):"無雲")+" m";
                text+="\nCAPE "+core.CAPE[i].ToString("F0")+" J/kg · 上升 "+core.Updraft[i].ToString("F1")+" m/s";
            }
            text+="\n水量帳本相對誤差 "+(core.WaterBudgetError()/Math.Max(1,core.InitialWater)).ToString("E2");
            return text;
        }
    }
}
