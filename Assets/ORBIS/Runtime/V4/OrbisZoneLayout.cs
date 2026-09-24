using System;
using UnityEngine;
namespace Orbis
{
    /// <summary>Persistent gameplay Zone == a pre-existing cubed-sphere tile ID.
    /// Sixteen subplots are geometry/interaction data, NOT sixteen new climate columns.
    /// Natural geography zones/chunks remain a separate observational hierarchy.</summary>
    public static class OrbisZoneLayout
    {
        public const int Side=4, PlotsPerZone=Side*Side;
        public static int PlotId(int zone,int x,int y)
        {
            if(zone<0||x<0||x>=Side||y<0||y>=Side)throw new ArgumentOutOfRangeException("Invalid Zone/plot coordinate.");
            return checked(zone*PlotsPerZone+y*Side+x);
        }
        public static Vector3 Point(OrbisWorld world,int zone,float u,float v)
        {
            if(world==null||zone<0||zone>=world.tiles.Length)throw new ArgumentOutOfRangeException("zone");
            int[] c=world.grid.tileCorners[zone];u=Mathf.Clamp01(u);v=Mathf.Clamp01(v);
            // Shared edges are the same normalized chord with reversed parameterization
            // on the neighboring face. No latitude/longitude or camera-following UV grid.
            return Vector3.Lerp(Vector3.Lerp(world.grid.corners[c[0]],world.grid.corners[c[1]],u),
                Vector3.Lerp(world.grid.corners[c[3]],world.grid.corners[c[2]],u),v).normalized;
        }
        public static int PickPlot(OrbisWorld world,int zone,Vector3 direction)
        {
            if(world==null||zone<0||zone>=world.tiles.Length)return -1;
            Vector3 p=direction.normalized;int best=-1;float score=-2;
            // Spherical half-plane containment, including cube-face seams and poles.
            for(int y=0;y<Side;y++)for(int x=0;x<Side;x++)
            {
                Vector3 a=Point(world,zone,x/(float)Side,y/(float)Side),b=Point(world,zone,(x+1)/(float)Side,y/(float)Side);
                Vector3 c=Point(world,zone,(x+1)/(float)Side,(y+1)/(float)Side),d=Point(world,zone,x/(float)Side,(y+1)/(float)Side);
                Vector3 center=(a+b+c+d).normalized;
                if(InEdge(a,b,p,center)&&InEdge(b,c,p,center)&&InEdge(c,d,p,center)&&InEdge(d,a,p,center))return PlotId(zone,x,y);
                float dot=Vector3.Dot(center,p);if(dot>score){score=dot;best=PlotId(zone,x,y);}
            }
            return best;
        }
        static bool InEdge(Vector3 a,Vector3 b,Vector3 p,Vector3 center)
        {Vector3 n=Vector3.Cross(a,b);return Vector3.Dot(n,p)*Mathf.Sign(Vector3.Dot(n,center))>=-1e-8f;}
    }
}
