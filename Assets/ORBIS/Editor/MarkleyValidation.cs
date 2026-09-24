#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using Orbis.Planet;
namespace Orbis.EditorTools
{
    public static class MarkleyValidation
    {
        [MenuItem("ORBIS/Validation/Markley Projection and Shaders")]
        public static void Run()
        {
            double maximum=0;MarkleyProjection.Load();
            for(int i=0;i<10000;i++)
            {
                double lon=((i*.6180339887498949)%1)*Math.PI*2-Math.PI,lat=Math.Asin(2*(i+.5)/10000-1);
                var location=new PlanetLocation(lon,lat);var uv=MarkleyProjection.Project(location);
                var expected=PlanetCoordinateSystem.SurfaceNormal(location);var actual=MarkleyProjection.Direction(uv.x,uv.y);
                maximum=Math.Max(maximum,Distance(actual,expected));
                Require(Distance(actual,MarkleyProjection.Direction(uv.x+.5,uv.y+2))<.00001,"Vertical deck transform mismatch");
                Require(Distance(actual,MarkleyProjection.Direction(uv.x+1,uv.y))<.00001,"Horizontal seam mismatch");
            }
            Require(maximum<.00015,"Markley round-trip error "+maximum);
            for(int i=0;i<=1024;i++)
            {
                double u=i/1024.0;
                Require(Distance(MarkleyProjection.Direction(u,0),MarkleyProjection.Direction(.25-u,0))<.00001,"Bottom edge mismatch");
                Require(Distance(MarkleyProjection.Direction(u,1),MarkleyProjection.Direction(.75-u,1))<.00001,"Top edge mismatch");
            }
            foreach(string path in new[]{"ORBIS/FlatWorld/ScrollSurface","ORBIS/FlatWorld/FlatMap","ORBIS/Clouds"})
            {
                Shader shader=Resources.Load<Shader>(path);Require(shader,"Missing shader "+path);
                Require(shader.isSupported&&!ShaderUtil.ShaderHasError(shader),"Shader error: "+path+". Inspect Console on the target graphics API.");
            }
            Debug.Log("ORBIS Markley PASS: 10000 round trips; maximum unit-vector error "+maximum+". Graphics API: "+SystemInfo.graphicsDeviceType+". Manually check drag during transition, four-edge traversal and returning to the current map centre.");
        }
        static double Distance(PlanetVector a,PlanetVector b){var d=a-b;return Math.Sqrt(PlanetVector.Dot(d,d));}
        static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    }
}
#endif
