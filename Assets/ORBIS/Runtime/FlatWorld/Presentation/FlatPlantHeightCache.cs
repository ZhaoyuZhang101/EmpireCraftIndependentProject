using UnityEngine;

namespace Orbis.Flat
{
    // One height per vertex of the displayed relief mesh. Trees, ground cover and
    // wildlife occlusion share it, including across geographic tile boundaries.
    public sealed class FlatPlantHeightCache : System.IDisposable
    {
        Material bake;
        RenderTexture heights;
        Texture source;
        uint revision;
        Vector4 lastCenter,lastEast,lastNorth,lastPoles,lastSize;
        float lastRadius,lastMorph,lastHeightScale;

        public void Bind(Material surface,Texture terrain,int columns,int rows,bool atlas=false)
        {
            float morph=surface.GetFloat("_ScrollMorph");
            if(!terrain||morph<=.75f||surface.GetVector("_ScrollRelief").w<=0)
            {
                surface.SetVector("_PlantLattice",Vector4.zero);
                return;
            }
            var size=surface.GetVector("_MapSize");
            var poles=surface.GetVector("_ScrollPoleHeights");
            var center=atlas?Vector4.zero:surface.GetVector("_LocalCenter");
            var east=atlas?Vector4.zero:surface.GetVector("_LocalEast");
            var north=atlas?Vector4.zero:surface.GetVector("_LocalNorth");
            float radius=atlas?0:surface.GetFloat("_LocalRadius"),heightScale=surface.GetFloat("_ScrollHeightScale");
            int width=atlas?columns+1:columns,height=rows+1;
            if(!heights)
            {
                bake=new Material(Resources.Load<Shader>("ORBIS/FlatWorld/LocalPlantHeights"));
                var format=SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat)?
                    RenderTextureFormat.RFloat:RenderTextureFormat.ARGBHalf;
                heights=new RenderTexture(width,height,0,format,RenderTextureReadWrite.Linear)
                {
                    name="ORBIS local terrain vertex heights",filterMode=FilterMode.Point,
                    wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp
                };
            }
            var lattice=new Vector4(width,height,1f/width,1f/height);
            if(!heights.IsCreated()||terrain!=source||revision!=terrain.updateCount||
               !center.Equals(lastCenter)||!east.Equals(lastEast)||!north.Equals(lastNorth)||
               !poles.Equals(lastPoles)||!size.Equals(lastSize)||radius!=lastRadius||(!atlas&&morph!=lastMorph)||heightScale!=lastHeightScale)
            {
                bake.SetTexture("_VisualTex",terrain);bake.SetVector("_MapSize",size);
                bake.SetVector("_LocalCenter",center);bake.SetVector("_LocalEast",east);bake.SetVector("_LocalNorth",north);
                bake.SetVector("_ScrollPoleHeights",poles);bake.SetFloat("_ScrollMorph",morph);
                bake.SetFloat("_LocalRadius",radius);bake.SetFloat("_ScrollHeightScale",heightScale);
                bake.SetVector("_PlantLattice",lattice);
                if(atlas)Orbis.Planet.MarkleyProjection.Bind(bake);
                var previous=RenderTexture.active;
                try{Graphics.Blit(null,heights,bake,atlas?1:0);}
                finally{RenderTexture.active=previous;}
                source=terrain;revision=terrain.updateCount;lastSize=size;lastPoles=poles;
                lastCenter=center;lastEast=east;lastNorth=north;lastRadius=radius;lastMorph=morph;lastHeightScale=heightScale;
            }
            surface.SetTexture("_PlantHeightTex",heights);surface.SetVector("_PlantLattice",lattice);
        }

        public void Dispose()
        {
            if(heights)heights.Release();
            if(Application.isPlaying){Object.Destroy(heights);Object.Destroy(bake);}
            else {Object.DestroyImmediate(heights);Object.DestroyImmediate(bake);}
        }
    }
}
