using System;
using UnityEngine;

namespace Orbis
{
    public sealed partial class OrbisWorld
    {
        struct StormSystem
        {
            public float latitude,longitude,speed,latitudeAmplitude,period,radius,strength,phase,lifePeriod,lifePhase;
            public bool tropical;
            public int hemisphere;
        }
        StormSystem[] storms;
        Vector3[] stormWinds;
        float[] stormLift;
        void BuildStorms()
        {
            var random=new RandomStream(RandomStream.Hash(seed+"|moving-storms"));
            storms=new StormSystem[8];stormWinds=new Vector3[tiles.Length];stormLift=new float[tiles.Length];
            for(int k=0;k<storms.Length;k++)
            {
                bool tropical=k<4;int hemisphere=k%2==0?1:-1;
                storms[k]=new StormSystem{tropical=tropical,hemisphere=hemisphere,
                    latitude=hemisphere*random.Range(tropical?8:34,tropical?22:58)*Mathf.Deg2Rad,
                    longitude=random.Range(-Mathf.PI,Mathf.PI),speed=random.Range(tropical?.008f:.014f,tropical?.018f:.032f)*(k%3==0?-1:1),
                    latitudeAmplitude=random.Range(tropical?1.5f:2.5f,tropical?5:7)*Mathf.Deg2Rad,
                    period=random.Range(80,170),radius=random.Range(tropical?5.5f:10,tropical?9:17)*Mathf.Deg2Rad,
                    strength=random.Range(tropical?18:28,tropical?28:38),phase=random.Range(0,2*Mathf.PI),
                    lifePeriod=random.Range(tropical?110:150,tropical?220:300),lifePhase=random.Range(0,2*Mathf.PI)};
            }
        }
        void PrepareAtmosphere(float declination)
        {
            if(storms==null)BuildStorms();
            if(Weather!=null)
            {
                var a=Weather.Atmosphere;
                for(int i=0;i<tiles.Length;i++)
                {
                    tiles[i].pressure=1013+(float)a.PressureAnomaly[i];
                    stormWinds[i]=Vector3.zero;stormLift[i]=(float)Math.Min(1.6,a.Updraft[i]/12);
                }
                return; // Do not keep moving prescribed vortices behind the prognostic model.
            }
            double time=AtmosphereClock;float tau=2*Mathf.PI;
            for(int i=0;i<tiles.Length;i++)
            {
                Tile tile=tiles[i];Vector3 p=grid.points[i];float lat=Mathf.Asin(p.y),lon=Mathf.Atan2(p.x,p.z);
                Vector3 east=new Vector3(p.z,0,-p.x).normalized,north=Vector3.Cross(p,east).normalized;
                float band=Mathf.Abs(lat-declination*.52f)*Mathf.Rad2Deg;
                float wave=(float)Math.Sin((lon*2+Mathf.Sin(lat*2.4f)*1.4f-time*.030f)%(Math.PI*2));
                float pressure=1013+9*Mathf.Exp(-Mathf.Pow((band-29)/12,2))-7*Mathf.Exp(-Mathf.Pow(band/15,2))-7*Mathf.Exp(-Mathf.Pow((band-56)/13,2));
                pressure+=Mathf.Exp(-Mathf.Pow((band-48)/24,2))*wave*5.2f;
                Vector3 wind=Vector3.zero;float lifting=0,vortex=0;
                foreach(var storm in storms)
                {
                    float centerLat=storm.latitude+(float)Math.Sin((time/storm.period*tau+storm.phase)%(Math.PI*2))*storm.latitudeAmplitude;
                    float centerLon=(float)((storm.longitude+storm.speed*time)%(Math.PI*2))+(float)Math.Sin((time/storm.period*tau+storm.phase*.7f)%(Math.PI*2))*.08f;
                    float dx=(Mathf.Repeat(lon-centerLon+Mathf.PI,tau)-Mathf.PI)*Mathf.Cos((lat+centerLat)*.5f),dy=lat-centerLat;
                    float distance=Mathf.Sqrt(dx*dx+dy*dy),q=distance/storm.radius;
                    float envelope=Mathf.Exp(-q*q*1.45f),ring=Mathf.Exp(-Mathf.Pow((q-.70f)/.32f,2));
                    float cycle=.5f+.5f*(float)Math.Sin((time/storm.lifePeriod*tau+storm.lifePhase)%(Math.PI*2));
                    float pulse=.12f+.88f*Mathf.Clamp01((cycle-.05f)/.77f);
                    float warm=storm.tropical?(tile.h<=0?Mathf.Clamp01((tile.temp-18)/12):.12f):1;
                    float life=pulse*(storm.tropical?.25f+.75f*warm:.48f+.52f*pulse)*(tile.h>0?.68f:1);
                    pressure-=storm.strength*envelope*life;
                    float swirl=storm.hemisphere*(ring*.92f+envelope*.18f)*life*(storm.tropical?1.18f:.72f)/Mathf.Max(.01f,distance);
                    wind+=east*(-dy*swirl)+north*(dx*swirl);
                    vortex+=storm.hemisphere*ring*life;lifting+=(storm.tropical?1.15f:.58f)*envelope*life;
                }
                tile.pressure=pressure;tile.vorticity=Mathf.Clamp(vortex,-1,1);
                stormWinds[i]=wind;stormLift[i]=Mathf.Clamp(lifting,0,1.6f);
            }
        }
        public float WeatherLift(int id)=>stormLift==null?0:stormLift[id];
        Vector3 PressureWind(int i)
        {
            Vector3 p=grid.points[i],gradient=Vector3.zero;
            foreach(int j in grid.neighbors[i])
            {
                float distance=Mathf.Max(.025f,Vector3.Angle(p,grid.points[j])*Mathf.Deg2Rad);
                gradient+=Vector3.ProjectOnPlane(grid.points[j]-p,p).normalized*((tiles[j].pressure-tiles[i].pressure)/distance*.25f);
            }
            float coriolis=(float)System.Math.Tanh(Mathf.Asin(p.y)*4.5f);
            return Vector3.Cross(p,gradient)*(coriolis*.014f)-gradient*.004f+stormWinds[i]*.78f;
        }
        public string CloudKind(int id)
        {
            Tile t=tiles[id];
            if(Weather!=null)
            {
                if(t.cloud<.05f)return "晴空／雲已消散";
                if(t.stormStage==3)return "成熟積雨雲／混合相雷暴";
                if(t.stormStage==2)return "積雲垂直發展中";
                if(t.stormStage==4)return "降水尾部／消散階段";
                if(t.cloudBaseMeters>=6500)return "高層冰雲／卷雲";
                if(t.precipitationMmH>.2f)return "層狀雲／持續降雨";
                if(t.cloudBaseMeters>=2500)return "中層雲／高積雲";
                return "低層積雲／層雲";
            }
            if(t.cloud<.18f)return "晴朗／零星薄雲";
            if(t.Temperature<0)return "冰晶雲／陣雪";
            if(Mathf.Abs(t.vorticity)>.42f&&t.rain>120)return "旋轉低壓／暴雨雨帶";
            if(t.rain>115&&t.Temperature>18)return "積雨雲／對流降雨";
            if(t.pressure<1004)return "鋒面雲／層狀降雨";
            if(t.h>.35f)return "迎風坡雲／地形雨";
            return "低層積雲／間歇晴空";
        }
    }
}
