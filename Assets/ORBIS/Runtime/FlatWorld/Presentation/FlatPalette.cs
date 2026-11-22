using UnityEngine;
namespace Orbis.Flat
{
    public static class FlatPalette
    {
        public static readonly uint[] BiomeColors={0x3694a5,0xd8c28a,0x7d9860,0x486d49,0x285d46,0xcda775,0xa8a36a,0x557c75,0xa6b6a2,0x878b90,0xdde4dc,0x547d61};
        public static readonly uint[] VegetationColors={0x9a805b,0x8fa66a,0xb2a570,0xa0a365,0x899468,0x648756,0x56805b,0x386c58,0x557b70,0x285d46,0x63856c,0x95a886,0xa6b6a2,0xdde4dc};
        public static readonly string[] VegetationNames={"裸地 / 非植被","温带草原","干草原","稀树草原","灌丛 / 地中海植被","季节性森林","温带落叶林","温带雨林","北方针叶林","热带雨林","湿地草本","高山草甸","苔原植被","冰雪"};
        public static readonly string[] TreeNames={"阔叶树","松树","桦树","棕榈","金合欢","柳树","灌木","热带阔叶树"};
        public static readonly uint[] ClimateColors={0x58666e,0x266849,0x36875c,0x91ad56,0xd9b175,0xc5ba9b,0xc4a863,0xb7b77d,0xe2c865,0xd9d88b,0xb1caa4,0x92bf73,0x6da171,0xabcbb4,0x48a888,0x579e96,0x99c3c2,0xaa929f,0x9585b0,0x9791b5,0xc0b9d0,0x9b819a,0x887dad,0xa5a6c5,0xc1c6dc,0x5c829e,0x739cb8,0x9fbaca,0xc6d8e0,0xb8c9c7,0xe5ecea};
        public static readonly uint[] LandformColors={0x36758a,0x7eaa68,0xb69c64,0xb99886,0x827d78,0x65998b,0x73935b,0x547970};
        public static readonly uint[] MaterialColors={0x9a805b,0xd8c28a,0x897257,0x9b9e91,0x878b90,0xdde4dc};
        public static readonly string[] LandformNames={"水域","平原","丘陵","高原","山地","河谷","泛洪平原","洼地"};
        public static readonly string[] MaterialNames={"土壤","沙子","淤泥 / 细粉砂","砾石河滩","裸岩","冰雪"};
        public static readonly string[] BiomeNames={"水域","旧存档沙滩","草地 / 热带草原","林地 / 季节林","热带雨林","沙漠","半干旱草原","针叶林","苔原","高山带","冰雪带","沼泽湿地"};
        public static string Legend(MapLayer layer)
        {
            var text=new System.Text.StringBuilder();
            uint[] colors=layer==MapLayer.Vegetation?VegetationColors:layer==MapLayer.Landforms?LandformColors:layer==MapLayer.Materials?MaterialColors:layer==MapLayer.Climate?ClimateColors:BiomeColors;
            if(layer==MapLayer.Temperature)return "冷蓝 → 暖橙　−25…35 °C，显示所选年份的同月温度。";
            if(layer==MapLayer.Rain)return "干沙 → 湿蓝　0…240 mm/月，显示所选年份的同月降水。";
            if(layer==MapLayer.Wetness)return "棕 → 湿绿：0…100% 土壤桶湿度；不是地下水实测值。";
            if(layer==MapLayer.Sediment)return "蓝 → 沙褐：0…1 相对悬沙；陆地灰色。";
            if(layer==MapLayer.Height)return "低绿 → 高白：0…4000m；等高线使用真实预设海拔插值。";
            if(layer==MapLayer.Water)return "深海 / 中海 / 浅海 / 淡水湖 / 细河道；混合区随汇流量展开。";
            if(layer==MapLayer.Rivers)return "深蓝粗线为主河道，细线为支流。地球按已测绘河道显示，随机世界按本地汇流显示。";
            if(layer==MapLayer.SurfaceWinds)return "Gleba 风带与海平面气压：深蓝为高压脊，亮黄为赤道槽/季风低压/副极地低压；蓝岸为向岸湿风，红岸为离岸干风。\n橙实线：ITCZ 热带辐合带 · 青虚线：STHZ 副热带高压脊 · 紫虚线：极锋带 · 动态流线箭头显示当月近地风向与风速。";
            if(layer==MapLayer.OceanCurrents)return "Gleba 表层洋流与海温：Cividis 动态海面高度叠加暖流（暖金橙）与涌升寒流（冰青蓝），动态流线箭头显示当月表层洋流方向与流速。";
            if(layer==MapLayer.RossbyJets)return "Gleba 高空急流与罗斯贝波：显示南北极锋急流与副热带急流的 4–8 波行星蜿蜒槽脊、急流核风速（暗紫 → 亮金）及实时动态高空急流流线。";
            if(layer==MapLayer.RainShadow)return "Gleba 地形雨影与风程大陆度：Cividis 深蓝为海洋迎风湿润带，亮金黄为山脉背风坡 Helmholtz 雨影区与深内陆风程干旱核。";
            if(layer==MapLayer.RelativeHumidity)return "Gleba 月均相对湿度：荒漠暖赭（0%）→ 温带青绿（50%）→ 季风/赤道深钴蓝（100%）。";
            if(layer==MapLayer.GlebaBiomes)return "Gleba 23 类生态群系：基于 Holdridge 17 条生理温度/降水/林线曲线、稀树草原干季转移、泥炭/草本/森林沼泽及海冰/冰盖分类。";
            if(layer==MapLayer.GlebaFlora)return "Gleba 乔灌草植被结构（RGB 三元图）：红 = 灌木比例 · 绿 = 草本比例 · 蓝 = 乔木比例 · 深黑 = 裸地/荒漠（Empty Land）。";
            if(layer==MapLayer.GlebaSoilTexture)return "Gleba 六遍土壤质地（RGB 三元图）：红 = 砂粒（风蚀沙丘/砂岩）· 绿 = 粉砂（冰期黄土/冲积平原）· 蓝 = 黏粒（风化红壤/湖积黏土）。";
            if(layer==MapLayer.GlebaSoilFertility)return "Gleba 土壤厚度与矿质养分：综合 6 遍流水搬运沉积的表土层深度、基岩矿质养分富集度与有机质（褐岩 → 金壤 → 沃绿）。";
            if(layer==MapLayer.GlebaBedrock)return "Gleba 20 类基岩岩性：砂岩/灰岩/白垩/页岩/岩盐（沉积岩）· 花岗岩/闪长岩/辉长岩/玄武岩/安山岩/流纹岩（火成岩）· 板岩/片岩/片麻岩（变质岩）。";
            if(layer==MapLayer.GlebaTectonics)return "Gleba 板块、微板块地体、造山带与火山：浅界线为板块边界，绯红为俯冲海沟弧，琥珀橙为陆陆碰撞造山带，青色为洋中脊，紫色为大陆裂谷，亮橙黄点为活火山。";
            if(layer==MapLayer.GlebaHydroGeo)return "Gleba 水文地质综合：粉红/亮金为热带珊瑚礁与环礁沙洲 · 亮青为 LGM 冰蚀峡湾海岸 · 冰蓝为末次盛冰期冰盖 · 橙褐为内流咸水湖 · 宝蓝为构造裂谷淡水湖 · 青绿斑块为深层承压/自流含水层。";
            if(layer==MapLayer.Fertility)return "棕 → 绿：0…100% 文明用地肥力。";
            if(layer==MapLayer.Nations)return "国家色代表 Zone 归属，不决定河岸或地貌。";
            for(int i=layer==MapLayer.Climate?1:0;i<colors.Length;i++)
            {
                string name=layer==MapLayer.Vegetation?VegetationNames[i]:layer==MapLayer.Climate?((Koppen)i).ToString():layer==MapLayer.Landforms?LandformNames[i]:layer==MapLayer.Materials?MaterialNames[i]:BiomeNames[i];
                text.Append("<color=#").Append(colors[i].ToString("X6")).Append(">■</color> ").Append(name).Append("  #").Append(colors[i].ToString("X6")).Append('\n');
            }
            return text.ToString();
        }
        public static Color32 RGB(uint h,byte a=255) { return new Color32((byte)(h>>16),(byte)(h>>8),(byte)h,a); }
        public static Color32 RiverMapBase(WaterKind water)
        {
            if(water==WaterKind.Lake)return RGB(0x94c4cf);
            return water>=WaterKind.DeepOcean&&water<=WaterKind.ShallowOcean?RGB(0x9fb5c2):RGB(0xe5e1cc);
        }
        public static Color32 Water(WaterKind kind)
        {
            switch(kind){case WaterKind.DeepOcean:return RGB(0x183950);case WaterKind.MiddleOcean:return RGB(0x245c78);case WaterKind.ShallowOcean:return RGB(0x449295);case WaterKind.Lake:return RGB(0x599ba7);case WaterKind.River:return RGB(0x74b0bd);default:return RGB(0x867c64);}
        }
        // A weight of 256 is full coverage. Byte arithmetic gives identical
        // albedo in the Burst texture job, the serial exporter and coast padding.
        static Color32 MixAlbedo(Color32 from,Color32 to,int weight)
        {
            int remaining=256-weight;
            return new Color32((byte)((from.r*remaining+to.r*weight+128)>>8),
                               (byte)((from.g*remaining+to.g*weight+128)>>8),
                               (byte)((from.b*remaining+to.b*weight+128)>>8),255);
        }
        internal static Color32 NaturalLand(VegetationCover cover,GroundMaterial material,float wetness,uint coverColor,uint materialColor)
        {
            var substrate=RGB(materialColor);
            if(material==GroundMaterial.Snow)return substrate;
            if(cover==VegetationCover.Ice)return RGB(coverColor);
            int moisture=(int)(Mathf.Clamp01(wetness)*256);
            // Moist fine earth absorbs more diffuse light than stone or gravel.
            int absorption=material==GroundMaterial.Rock||material==GroundMaterial.Gravel?26:54;
            int darkening=(moisture*absorption+128)>>8;
            substrate.r=(byte)((substrate.r*(256-darkening)+128)>>8);
            substrate.g=(byte)((substrate.g*(256-darkening*7/8)+128)>>8);
            substrate.b=(byte)((substrate.b*(256-darkening*3/4)+128)>>8);
            if(cover==VegetationCover.Bare)return substrate;

            var plants=RGB(coverColor);
            if(cover==VegetationCover.DrySteppe)plants=MixAlbedo(RGB(0x9e9f72),RGB(0x82945f),moisture);
            else if(cover==VegetationCover.Grassland)plants=MixAlbedo(RGB(0x9aa171),RGB(0x70995e),moisture);
            return MixAlbedo(substrate,plants,material==GroundMaterial.Rock?189:236);
        }
        static Color32 LandColor(WorldData w,int i)
        {
            if(w.Facts==null)return RGB(BiomeColors[(int)w.Biomes[i]]);
            var cover=w.Facts.Vegetation[i];
            var material=w.Facts.Materials[i];
            return NaturalLand(cover,material,w.Facts.Wetness[i],VegetationColors[(int)cover],MaterialColors[(int)material]);
        }
        public static Color32 Terrain(WorldData w,int i)
        {
            bool area=FlatTerrainAnalysis.AreaWater(w.Water[i]);
            Color32 c=area?Water(w.Water[i]):LandColor(w,i);
            // Dilate land albedo only under the sub-tile shore reconstruction. Water
            // is composited afterwards. This prevents bilinear blue halos on beaches.
            if(area&&w.Facts!=null&&w.Facts.ShoreDistance[i]<=2)
            {
                int best=-1,distance=100;
                for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++)
                {
                    int n=w.Index(i%w.Width+dx,i/w.Width+dy),d=dx*dx+dy*dy;
                    if(n>=0&&d<distance&&!FlatTerrainAnalysis.AreaWater(w.Water[n])){best=n;distance=d;}
                }
                if(best>=0)c=LandColor(w,best);
            }
            c.a=w.Water[i]==WaterKind.Land?(byte)0:(byte)255;return c;
        }
        public static Color32 Display(WorldData w,int i,MapLayer layer)
        {
            Color c=Terrain(w,i);c.a=1;
            if(layer>=MapLayer.SurfaceWinds)
            {
                GlebaGeographySupplement.EvaluateMapmodeColor(w,i,w.Month,GlebaGeographySupplement.MapLayerToGlebaMode(layer),out byte r,out byte g,out byte b);
                return new Color32(r,g,b,255);
            }
            switch(layer)
            {
                case MapLayer.Nations:break; // Exactly one political tint, in the shader.
                case MapLayer.Height: c=FlatTerrainAnalysis.AreaWater(w.Water[i])?Water(w.Water[i]):Color.Lerp(RGB(0x597b62),RGB(0xe4e1d2),w.Elevation[i]/4000f);break;
                case MapLayer.Climate:c=w.Water[i]>=WaterKind.DeepOcean&&w.Water[i]<=WaterKind.ShallowOcean?Water(w.Water[i]):RGB(ClimateColors[(int)w.Climates[i]]);break;
                case MapLayer.Temperature:
                case MapLayer.Rain:
                    var year=FlatClimateYear.At(FMath.Seed(w.Seed),System.Math.Floor(w.Day/30)*30);
                    year.Weather(w,i,w.Month,out float temperature,out float rain);
                    c=layer==MapLayer.Temperature?Color.Lerp(RGB(0x4169ab),RGB(0xe9aa6a),Mathf.InverseLerp(-25,35,temperature)):
                        Color.Lerp(RGB(0xd1bb93),RGB(0x2c869b),Mathf.Clamp01(rain/240));break;
                case MapLayer.Water:c=FlatTerrainAnalysis.AreaWater(w.Water[i])?Water(w.Water[i]):RGB(0x767b71);break;
                case MapLayer.Rivers:c=RiverMapBase(w.Water[i]);break;
                case MapLayer.Landforms:c=RGB(LandformColors[(int)w.Facts.Forms[i]]);break;
                case MapLayer.Materials:c=FlatTerrainAnalysis.AreaWater(w.Water[i])?Water(w.Water[i]):RGB(MaterialColors[(int)w.Facts.Materials[i]]);break;
                case MapLayer.Wetness:c=Color.Lerp(RGB(0xad8862),RGB(0x326c56),w.Facts.Wetness[i]);break;
                case MapLayer.Sediment:c=w.Water[i]==WaterKind.Land?RGB(0x727773):Color.Lerp(RGB(0x326785),RGB(0xb79b69),w.Facts.Sediment[i]);break;
                case MapLayer.Vegetation:c=FlatTerrainAnalysis.AreaWater(w.Water[i])?Water(w.Water[i]):RGB(VegetationColors[(int)w.Facts.Vegetation[i]]);break;
                case MapLayer.Fertility:c=Color.Lerp(RGB(0xb39d7a),RGB(0x478c68),w.Fertility[i]);break;
            }
            c.a=1;return c;
        }
    }
}
