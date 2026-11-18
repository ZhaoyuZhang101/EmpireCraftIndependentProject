using System;
using System.Threading;

namespace Orbis.Flat
{
    // Each tile samples its nearest drawn reach. Padding adjacent tiles with their
    // nearest hydrograph lets a migrating bend carry its own seasonal discharge
    // across a dry semantic tile. CPU water queries use the same field as the GPU.
    public sealed class FlatRiverStageField
    {
        const int Channels=4, Months=12;
        readonly int width,height;
        public readonly byte[] Packed;

        internal FlatRiverStageField(FlatRiverCurves curves,FlatRiverDischarge discharge,CancellationToken cancel)
        {
            width=curves.Width;height=curves.Height;
            int tiles=width*height;
            Packed=new byte[tiles*Months];
            Array.Fill(Packed,(byte)60);
            for(int k=0;k<curves.ActiveTileCount;k++)
            {
                if((k&255)==0)cancel.ThrowIfCancellationRequested();
                int tile=curves.GetActiveTile(k);
                curves.Sample(tile%width+.5f,tile/width+.5f,out int reach);
                float mean=discharge.Annual(reach);
                if(mean<=0)continue;
                for(int m=0;m<Months;m++)
                {
                    float ratio=FMath.Clamp(discharge.Monthly(reach,m)/mean,.1f,4);
                    Packed[((m/Channels)*tiles+tile)*Channels+m%Channels]=(byte)(ratio*60+.5f);
                }
            }
        }

        float Month(int x,int y,int month)
        {
            int offset=((month/Channels*height+y)*width+x)*Channels+month%Channels;
            return Packed[offset]/60f;
        }

        float Spatial(int x0,int x1,int y0,int y1,float u,float v,int month)
        {
            float south=FMath.Lerp(Month(x0,y0,month),Month(x1,y0,month),u);
            float north=FMath.Lerp(Month(x0,y1,month),Month(x1,y1,month),u);
            return FMath.Lerp(south,north,v);
        }

        public float Ratio(float x,float y,double day)
        {
            if(!FMath.Finite(x)||!FMath.Finite(y))return 1;
            x=x%width-.5f;y=FMath.Clamp(y-.5f,0,height-1);
            int left=(int)Math.Floor(x),bottom=(int)y;
            float u=x-left,v=y-bottom;
            int x0=FMath.Wrap(left,width),x1=FMath.Wrap(left+1,width),y1=Math.Min(height-1,bottom+1);
            double period=day/30,whole=Math.Floor(period);
            int month=FMath.Wrap((int)(whole%Months),Months);
            return FMath.Lerp(Spatial(x0,x1,bottom,y1,u,v,month),
                              Spatial(x0,x1,bottom,y1,u,v,(month+1)%Months),(float)(period-whole));
        }

        // At-a-station hydraulic geometry. Flow may vary markedly through the
        // year while bank width varies more slowly than discharge or water depth.
        public static float WidthScale(float ratio) { return FMath.Clamp((float)Math.Pow(FMath.Clamp(ratio,.1f,4),.23),.58f,1.30f); }
        public static float DepthScale(float ratio) { return FMath.Clamp((float)Math.Pow(FMath.Clamp(ratio,.1f,4),.38),.42f,1.62f); }
    }
}
