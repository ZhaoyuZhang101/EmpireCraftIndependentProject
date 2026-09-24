using System;
using System.Collections.Generic;
using UnityEngine;
namespace Orbis
{
    public sealed partial class OrbisWorld
    {
        public int PickTile(Vector3 direction)
        {
            Vector3 p=direction.normalized;int hint=grid.Nearest(p);
            if(ContainsTile(hint,p))return hint;
            foreach(int i in grid.neighbors[hint])if(ContainsTile(i,p))return i;
            for(int i=0;i<tiles.Length;i++)if(ContainsTile(i,p))return i;
            return hint;
        }
        bool ContainsTile(int tile,Vector3 p)
        {
            int[] c=grid.tileCorners[tile];
            for(int k=0;k<c.Length;k++)
            {
                Vector3 plane=Vector3.Cross(grid.corners[c[k]],grid.corners[c[(k+1)%c.Length]]);
                if(Vector3.Dot(plane,p)*Mathf.Sign(Vector3.Dot(plane,grid.points[tile]))< -1e-7f)return false;
            }
            return true;
        }
        public int[] SelectionTiles(int tile,int mode)
        {
            if(tile<0||tile>=tiles.Length)return Array.Empty<int>();
            if(mode==2&&tiles[tile].chunkId>=0)return geography.chunks[tiles[tile].chunkId].tileIds;
            if(mode==1&&tiles[tile].zoneId>=0)return geography.zones[tiles[tile].zoneId].tileIds;
            return new[]{tile};
        }
        public int FoundNation(int tile,int mode)
        {
            int[] region=SelectionTiles(tile,0);int capital=-1; // Always one 4x4 gameplay Zone.
            foreach(int i in region)
            {
                Tile t=tiles[i];if(t.owner>=0)return -1;
                if(t.h>0&&t.lake<=.018f&&(capital<0||t.Fertility>tiles[capital].Fertility))capital=i;
            }
            if(capital<0)return -1;int civ=Spawn(capital);if(civ<0)return -1;
            foreach(int i in region)if(tiles[i].h>0&&tiles[i].lake<=.018f)tiles[i].owner=civ;
            Log(civs[civ].name+"建立了 1 個 4×4 Zone；16 個小地塊共用首府人口，不複製人口。");UpdateStats();return civ;
        }
        public bool ClaimRegion(int tile,int mode,int civ)
        {
            if(civ<0||civ>=civs.Count)return false;
            int[] region=SelectionTiles(tile,0);bool adjacent=false,land=false; // Natural layers never change claim size.
            foreach(int i in region)
            {
                Tile t=tiles[i];if(t.owner>=0&&t.owner!=civ)return false;
                if(t.h<=0||t.lake>.018f)continue;if(t.owner<0)land=true;
                foreach(int j in grid.neighbors[i])if(tiles[j].owner==civ)adjacent=true;
            }
            if(!land||!adjacent)return false;
            foreach(int i in region)if(tiles[i].h>0&&tiles[i].lake<=.018f)tiles[i].owner=civ;
            Log(civs[civ].name+"擴展了 1 個相鄰無主 4×4 Zone。");return true;
        }
        void StepTerritories(float dt)
        {
            if(!settings.regionExpansion||dt<=0)return;
            TerritoryClock+=dt;if(TerritoryClock<20)return;TerritoryClock%=20;
            // Bounded frontier scan. No HashSet, no whole natural-region scan/annexation.
            // At most one adjacent Zone per civilization per 20 calendar days.
            for(int civ=0;civ<civs.Count;civ++)
            {
                int donor=-1,best=-1;float population=0,score=-1;
                for(int i=0;i<tiles.Length;i++)if(tiles[i].owner==civ)
                {
                    population+=tiles[i].pop;
                    if(donor<0||tiles[i].pop>tiles[donor].pop)donor=i;
                    foreach(int j in grid.neighbors[i])
                    {
                        Tile t=tiles[j];if(t.owner>=0||t.h<=0||t.lake>.018f)continue;
                        float quality=t.Fertility*(1-Mathf.Clamp01(t.h));
                        if(quality>score||quality==score&&(best<0||j<best)){score=quality;best=j;}
                    }
                }
                if(donor<0||population<400||tiles[donor].pop<120||best<0||score<=.06f)continue;
                if(ClaimRegion(best,0,civ))
                {
                    float migrants=Mathf.Min(60,tiles[donor].pop*.15f);
                    tiles[donor].pop-=migrants;tiles[best].pop+=migrants;
                }
            }
        }
    }
}
