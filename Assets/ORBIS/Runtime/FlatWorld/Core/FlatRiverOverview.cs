using System;
using System.Threading;

namespace Orbis.Flat
{
    public sealed partial class FlatRiverCurves
    {
        public const float DisplayWidthGain = .16f;

        // A bounded, filterable overview keeps subpixel tributaries from blinking
        // on a globe. Close views use the distance field from the same segments.
        public int OverviewWidth => Math.Min(2048,Math.Max(512,Width*4));

        public byte[] DrawOverview(CancellationToken cancel)
        {
            int width=OverviewWidth,height=width/2;
            float scale=(float)width/Width;
            var pixels=new byte[width*height];
            for(int k=0;k<segments.Length;k++)
            {
                if((k&255)==0)cancel.ThrowIfCancellationRequested();
                var segment=segments[k];
                var a=segment.A;var b=segment.B;
                float importance=segment.Reach<0 ? 1-(~segment.Reach)*.1f :
                    FMath.Clamp((Math.Max(a.Radius,b.Radius)-.012f)/.14f,0,1);
                float radius=(.46f+1.10f*importance)*(1+DisplayWidthGain);
                float opacity=.43f+.57f*importance;
                float ax=a.X*scale,ay=a.Y*scale,bx=b.X*scale,by=b.Y*scale;
                float metric=Metric((ay+by)*.5f,height);
                float dx=(bx-ax)*metric,dy=by-ay;
                float den=Math.Max(1e-12f,dx*dx+dy*dy);
                float pad=radius+.65f,padX=pad/metric;
                int x0=(int)Math.Floor(Math.Min(ax,bx)-padX),x1=(int)Math.Floor(Math.Max(ax,bx)+padX);
                int y0=Math.Max(0,(int)Math.Floor(Math.Min(ay,by)-pad)),y1=Math.Min(height-1,(int)Math.Floor(Math.Max(ay,by)+pad));
                for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
                {
                    float px=(x+.5f-ax)*metric,py=y+.5f-ay;
                    float t=FMath.Clamp((px*dx+py*dy)/den,0,1);
                    float ex=px-dx*t,ey=py-dy*t,d2=ex*ex+ey*ey;
                    if(d2>=pad*pad)continue;
                    int column=FMath.Wrap(x,width);
                    if(editedRiverTiles!=null)
                    {
                        int tile=(int)((y+.5f)/scale)*Width+(int)((column+.5f)/scale);
                        if(editedRiverTiles[tile]!=(segment.Reach>=0))continue;
                    }
                    int index=y*width+column;
                    float coverage=FMath.Clamp((pad-(float)Math.Sqrt(d2))/1.3f,0,1);
                    byte ink=(byte)(255*opacity*coverage+.5f);
                    if(ink>pixels[index])pixels[index]=ink;
                }
            }
            cancel.ThrowIfCancellationRequested();
            return pixels;
        }
    }
}
