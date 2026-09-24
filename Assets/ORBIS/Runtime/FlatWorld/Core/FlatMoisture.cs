using System;
using System.Threading;
using System.Threading.Tasks;
namespace Orbis.Flat
{
    // Monthly equilibrium columns on a bounded grid. mm of precipitable water,
    // conservative cell-area-weighted transport, prescribed (not solved) circulation.
    public sealed class FlatMoistureState
    {
        public readonly int Width,Height;
        public readonly float[] Humidity,Condensation,East,North,Rain;
        public readonly double[] BudgetError=new double[12];
        public FlatMoistureState(int width,int height)
        {Width=width;Height=height;int n=width*height*12;Humidity=new float[n];Condensation=new float[n];East=new float[n];North=new float[n];Rain=new float[n];}
        public FlatMoistureState Clone()
        {
            var s=new FlatMoistureState(Width,Height);Array.Copy(Humidity,s.Humidity,Humidity.Length);Array.Copy(Condensation,s.Condensation,Humidity.Length);
            Array.Copy(East,s.East,Humidity.Length);Array.Copy(North,s.North,Humidity.Length);Array.Copy(Rain,s.Rain,Humidity.Length);Array.Copy(BudgetError,s.BudgetError,12);return s;
        }
        public float Sample(float[] data,float u,float v,int month)
        {
            float px=u*Width-.5f,py=FMath.Clamp(v*Height-.5f,0,Height-1);int x=(int)Math.Floor(px),y=(int)py;
            float tx=px-x,ty=py-y;int a=(y*Width+FMath.Wrap(x,Width))*12+month,b=(y*Width+FMath.Wrap(x+1,Width))*12+month;
            int c=(Math.Min(y+1,Height-1)*Width+FMath.Wrap(x,Width))*12+month,d=(Math.Min(y+1,Height-1)*Width+FMath.Wrap(x+1,Width))*12+month;
            return FMath.Lerp(FMath.Lerp(data[a],data[b],tx),FMath.Lerp(data[c],data[d],tx),ty);
        }
    }
    public static class FlatMoisture
    {
        // Land heats faster than sea, so the convergence zone follows the
        // local thermal contrast rather than a permanently straight parallel.
        public static float ConvergenceLatitude(float month,float seaFraction,float thermalAnomaly)
        {
            float season=(float)Math.Cos((month-6)*Math.PI/6);
            float excursion=FMath.Lerp(32.5f,4f,FMath.Clamp(seaFraction,0,1));
            return FMath.Clamp(season*excursion+FMath.Clamp(thermalAnomaly,-5,5)*1.2f,-38,38);
        }
        public static float Capacity(float temperature)
        {return 1.3f*6.112f*(float)Math.Exp(17.67*Math.Max(-70,temperature)/(Math.Max(-70,temperature)+243.5));}
        public static FlatMoistureState Build(WorldData world,CancellationToken cancel,int workers=0,bool evaporate=true)
        {
            int width=Math.Min(128,world.Width),height=width/2,count=width*height;
            var state=new FlatMoistureState(width,height);var elevation=new float[count];var sea=new float[count];var area=new float[count];
            int scale=world.Width/width;
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                int i=y*width+x;area[i]=(float)Math.Cos(((y+.5)/height-.5)*Math.PI);
                for(int dy=0;dy<scale;dy++)for(int dx=0;dx<scale;dx++)
                {int n=(y*scale+dy)*world.Width+x*scale+dx;elevation[i]+=Math.Max(0,(int)world.Elevation[n]);sea[i]+=FlatTerrainAnalysis.AreaWater(world.Water[n])?1:0;}
                elevation[i]/=scale*scale;sea[i]/=scale*scale;
            }
            Parallel.For(0,12,FlatWorkerPolicy.Options(cancel,workers),month=>
            {
                var temp=new float[count];var cap=new float[count];var pressure=new float[count];var u=new float[count];var v=new float[count];
                var q=new float[count];var next=new float[count];var soil=new float[count];var lift=new float[count];
                var zonalTemp=new float[height];
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    int i=y*width+x;float lat=((y+.5f)/height-.5f)*180;
                    for(int dy=0;dy<scale;dy++)for(int dx=0;dx<scale;dx++)temp[i]+=world.Temperature[((y*scale+dy)*world.Width+x*scale+dx)*12+month];
                    temp[i]/=scale*scale;cap[i]=Capacity(temp[i]);q[i]=cap[i]*.55f;soil[i]=50*(1-sea[i]);
                    zonalTemp[y]+=temp[i]+elevation[i]*.0065f;
                }
                for(int y=0;y<height;y++)zonalTemp[y]/=width;
                Func<int,int,int> index=(x,y)=>Math.Max(0,Math.Min(height-1,y))*width+FMath.Wrap(x,width);
                var convergenceLatitude=new float[count];
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    int i=y*width+x;float lat=((y+.5f)/height-.5f)*180;
                    float seaRegion=0;
                    for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++)seaRegion+=sea[index(x+dx,y+dy)];
                    seaRegion=FMath.Lerp(sea[i],seaRegion/25,.55f);
                    float thermal=temp[i]+elevation[i]*.0065f-zonalTemp[y];
                    float itcz=convergenceLatitude[i]=ConvergenceLatitude(month,seaRegion,thermal);
                    float seasonalHeat=(1-sea[i])*(float)Math.Cos((month-6)*Math.PI/6)*Math.Sign(lat)*Math.Max(0,1-Math.Abs(lat)/75);
                    float subtropical=FMath.Lerp(30,28,seaRegion)+Math.Max(0,seasonalHeat)*3;
                    pressure[i]=1.7f*(float)Math.Exp(-Math.Pow((Math.Abs(lat)-subtropical)/12,2))
                        -1.2f*(float)Math.Exp(-Math.Pow((lat-itcz)/15,2))-seasonalHeat*.8f-thermal*.055f;
                }
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    int i=y*width+x;float lat=((y+.5f)/height-.5f)*180,ab=Math.Abs(lat);
                    float gx=(pressure[index(x+1,y)]-pressure[index(x-1,y)])*.5f,gy=(pressure[index(x,y+1)]-pressure[index(x,y-1)])*.5f;
                    float coriolis=(float)Math.Sin(lat*Math.PI/180),belt=(float)Math.Tanh((ab-30)/8)*(float)Math.Tanh((68-ab)/8);
                    u[i]=FMath.Clamp(belt*.24f-gx*.12f-gy*coriolis*.08f,-.32f,.32f);
                    v[i]=y==0||y==height-1?0:FMath.Clamp(-gy*.20f+gx*coriolis*.08f,-.14f,.14f);
                    int upstream=index(x-(u[i]>=0?1:-1),y);
                    float rise=Math.Max(0,elevation[i]-elevation[upstream])*Math.Abs(u[i]);
                    upstream=index(x,y-(v[i]>=0?1:-1));rise+=Math.Max(0,elevation[i]-elevation[upstream])*Math.Abs(v[i]);
                    float convection=.065f*(float)Math.Exp(-Math.Pow((lat-convergenceLatitude[i])/14,2));
                    float fronts=.040f*(float)Math.Exp(-Math.Pow((ab-52)/13,2));
                    lift[i]=convection+fronts+Math.Min(.30f,rise/1400);
                    state.East[i*12+month]=u[i]/.32f;state.North[i*12+month]=v[i]/.32f;
                }
                double initial=0,external=0,export=0;
                for(int i=0;i<count;i++)initial+=(q[i]+soil[i])*area[i];
                const int steps=512,measure=128;const float dt=.25f;
                for(int step=0;step<steps;step++)
                {
                    cancel.ThrowIfCancellationRequested();
                    // Gather fixed outgoing fractions: each transfer has one equal donor
                    // and receiver term. No shared writes or parallel reduction ordering.
                    for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                    {
                        int i=y*width+x,w=index(x-1,y),e=index(x+1,y),s=index(x,y-1),n=index(x,y+1);
                        float mass=q[i]*area[i]*(1-Math.Abs(u[i])-Math.Abs(v[i]));
                        mass+=q[w]*area[w]*Math.Max(0,u[w])+q[e]*area[e]*Math.Max(0,-u[e]);
                        if(y>0)mass+=q[s]*area[s]*Math.Max(0,v[s]);if(y<height-1)mass+=q[n]*area[n]*Math.Max(0,-v[n]);
                        next[i]=mass/area[i];
                    }
                    for(int i=0;i<count;i++)
                    {
                        float humidity=FMath.Clamp(next[i]/cap[i],0,1),pet=Math.Max(.05f,2+temp[i]*.10f)*dt;
                        float oceanEvap=evaporate?sea[i]*pet*(1-humidity)*1.6f:0;
                        float landEvap=evaporate?Math.Min(soil[i],pet*.65f*(1-humidity)*(1-sea[i])):0;
                        soil[i]-=landEvap;q[i]=next[i]+oceanEvap+landEvap;external+=oceanEvap*area[i];
                        float condensed=Math.Max(0,q[i]-cap[i]*.90f)*.65f+q[i]*Math.Min(.7f,lift[i]*dt);
                        float rain=Math.Min(q[i],condensed);q[i]-=rain;
                        float landRain=rain*(1-sea[i]);soil[i]+=landRain;
                        float runoff=Math.Max(0,soil[i]-150*(1-sea[i]));soil[i]-=runoff;
                        export+=(rain*sea[i]+runoff)*area[i];
                        if(step>=steps-measure)
                        {
                            int a=i*12+month;state.Humidity[a]+=Math.Min(1,q[i]/cap[i])/measure;
                            state.Condensation[a]+=rain/(dt*measure);state.Rain[a]+=rain*30/(dt*measure);
                        }
                    }
                }
                double final=0;for(int i=0;i<count;i++)final+=(q[i]+soil[i])*area[i];
                state.BudgetError[month]=Math.Abs(initial+external-export-final)/Math.Max(1,initial+external);
            });
            return state;
        }
    }
}
