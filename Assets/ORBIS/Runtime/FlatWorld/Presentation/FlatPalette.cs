using UnityEngine;
namespace Orbis.Flat
{
    public static class FlatPalette
    {
        public static readonly uint[] BiomeColors={0x3694a5,0xd8c28a,0x7d9860,0x486d49,0x285d46,0xcda775,0xa8a36a,0x557c75,0xa6b6a2,0x878b90,0xdde4dc,0x547d61};
        public static readonly uint[] VegetationColors={0x9a805b,0x8fa66a,0xb2a570,0xa0a365,0x899468,0x648756,0x56805b,0x386c58,0x557b70,0x285d46,0x63856c,0x95a886,0xa6b6a2,0xdde4dc};
        public static readonly string[] VegetationNames={"裸地 / 非植被","草原","干草原","稀树草原","灌丛","季节性森林","温带落叶林","温带雨林","北方针叶林","热带雨林","湿地草本","高山草甸","苔原植被","冰雪"};
        public static readonly string[] TreeNames={"阔叶树","松树","桦树","棕榈","金合欢","柳树","灌木","热带阔叶树"};
        public static readonly uint[] ClimateColors={0x58666e,0x266849,0x36875c,0x91ad56,0xd9b175,0xc5ba9b,0xc4a863,0xb7b77d,0xe2c865,0xd9d88b,0xb1caa4,0x92bf73,0x6da171,0xabcbb4,0x48a888,0x579e96,0x99c3c2,0xaa929f,0x9585b0,0x9791b5,0xc0b9d0,0x9b819a,0x887dad,0xa5a6c5,0xc1c6dc,0x5c829e,0x739cb8,0x9fbaca,0xc6d8e0,0xb8c9c7,0xe5ecea};
        public static readonly uint[] LandformColors={0x36758a,0x7eaa68,0xb69c64,0xb99886,0x827d78,0x65998b,0x73935b,0x547970};
        public static readonly uint[] MaterialColors={0x9a805b,0xd8c28a,0x897257,0x9b9e91,0x878b90,0xdde4dc};
        public static readonly string[] LandformNames={"水域","平原","丘陵","高原","山地","河谷","泛洪平原","洼地"};
        public static readonly string[] MaterialNames={"土壤","沙子","淤泥 / 细粉砂","砾石河滩","裸岩","冰雪"};
        public static readonly string[] BiomeNames={"水域","旧存档沙滩","草地","林地 / 季节林","热带雨林","沙漠","半干旱草原","针叶林","苔原","高山带","冰雪带","沼泽湿地"};
        public static string ClimateName(Koppen type)
        {
            switch(type)
            {
                case Koppen.Csa: return "Csa · 热夏地中海气候（夏干冬雨）";
                case Koppen.Csb: return "Csb · 暖夏地中海气候（夏干冬雨）";
                case Koppen.Csc: return "Csc · 凉夏地中海气候（夏干冬雨）";
                case Koppen.Af: return "Af · 热带雨林气候";
                case Koppen.Am: return "Am · 热带季风气候";
                case Koppen.Aw: return "Aw · 热带干湿季气候";
                case Koppen.BSh: return "BSh · 热半干旱气候";
                case Koppen.BSk: return "BSk · 冷半干旱气候";
                default: return type.ToString();
            }
        }
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
            if(layer==MapLayer.GlebaBiomes)return "Gleba 生态群系综合全年冷暖、干湿季、林线和植被结构，区分地中海硬叶植被、热带稀树草原、湿热草地及半干旱草原。";
            if(layer==MapLayer.GlebaFlora)return "Gleba 乔灌草植被结构（RGB 三元图）：红 = 灌木比例 · 绿 = 草本比例 · 蓝 = 乔木比例 · 深黑 = 裸地/荒漠（Empty Land）。";
            if(layer==MapLayer.GlebaSoilTexture)return "Gleba 六遍土壤质地（RGB 三元图）：红 = 砂粒（风蚀沙丘/砂岩）· 绿 = 粉砂（冰期黄土/冲积平原）· 蓝 = 黏粒（风化红壤/湖积黏土）。";
            if(layer==MapLayer.GlebaSoilFertility)return "Gleba 土壤厚度与矿质养分：综合 6 遍流水搬运沉积的表土层深度、基岩矿质养分富集度与有机质（褐岩 → 金壤 → 沃绿）。";
            if(layer==MapLayer.GlebaBedrock)return "Gleba 37 类完整基岩岩性：7 类碎屑沉积岩 · 5 类化学/蒸发沉积岩 · 10 类侵入/喷出长英度火成岩（含大火成岩省溢流玄武岩）· 15 类高压俯冲/巴罗变质带/太古宙绿岩带变质岩。";
            if(layer==MapLayer.GlebaTectonics)return "Gleba 板块、微板块地体、古造山带、大火成岩省与火山：浅界线为板块边界，绯红为俯冲海沟弧，琥珀橙为碰撞造山带，靛紫为大火成岩省玄武岩台地，青铜为古造山缝合带，亮橙黄点为活火山。";
            if(layer==MapLayer.GlebaHydroGeo)return "Gleba 水文地质综合：粉红/亮金为热带珊瑚礁与环礁沙洲 · 亮青为 LGM 冰蚀峡湾海岸 · 冰蓝为末次盛冰期冰盖 · 橙褐为内流咸水湖 · 宝蓝为构造裂谷淡水湖 · 青绿斑块为深层承压/自流含水层。";
            if(layer==MapLayer.GlebaCrops)return "Gleba 起源农作物与文明承载力：金珀为野生豌豆 · 赤陶橙为新月沃地苦野豌豆 · 鲜柠绿为温带野生黑麦 · 翡翠青/亮水青为季风野生稻与满江红固氮共生系 · 亮暖金高光为大河冲积与海岸文明摇篮高承载区。";
            if(layer==MapLayer.ExoConvectiveRain)return "ExoPlaSim 水循环拆分（对流雨 vs 层状雨 vs 降雪）：暖品红/珊瑚红为深对流雷暴降水（ITCZ/季风/海陆风辐合）· 青钴蓝为斜压锋面与迎风地形大尺度层状降水 · 冰白为固态降雪。";
            if(layer==MapLayer.ExoRadiationOlr)return "ExoPlaSim 辐射能量收支与出射长波 OLR：深靛蓝（<160 W/m²）为高耸深对流云顶与强温室捕获区 · 亮金黄（>285 W/m²）为副高晴空干沙漠热辐射窗 · 冰青高光为高反照率冰雪盖。";
            if(layer==MapLayer.ExoDiurnalRange)return "ExoPlaSim 昼夜温差 DTR 与感潜热波文比：深海蓝（1–3 °C）为高热容海洋恒温区 · 翠青绿（6–9 °C）为高潜热湿润雨林/季风海岸 · 炽金橙（16–22 °C）为干土高感热荒漠大温差区。";
            if(layer==MapLayer.ExoStormPotential)return "ExoPlaSim 风暴气候学（MPI / CAPE / 生成潜势 GPI）：暗紫 → 绯红 → 亮金核显示热带洋面最大潜在台风强度（m/s）与低风切变生成温床；陆地绿金显示深对流有效位能 CAPE；叠加实时引导气流流线。";
            if(layer==MapLayer.ExoPastaClimate)
            {
                var sb=new System.Text.StringBuilder("ExoPlaSim Pasta 生物气候分类（19 类陆海气候带）：\n");
                for(int p=0;p<=18;p++)
                {
                    var pk=(PastaClimateKind)p;
                    uint hex=ExoPlaSimSupplement.GetPastaColorHex(pk);
                    sb.Append("<color=#").Append(hex.ToString("X6")).Append(">■</color> ").Append(ExoPlaSimSupplement.GetPastaLabel(pk)).Append('\n');
                }
                return sb.ToString();
            }
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
        // Ocean depth is recorded in its own layers. On a surveyed climate map a
        // single marine colour keeps coarse shelf boundaries out of the coastline.
        internal const uint ClimateOceanColor = 0x183950;
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
        internal static ushort RockWeathering(GroundMaterial material,Biome biome,float temperature,float wetness)
        {
            if(material!=GroundMaterial.Rock||biome==Biome.Alpine||biome==Biome.Tundra||biome==Biome.Snow)return 0;
            // Use annual climate so the rock's base pigment is stable through the seasons.
            float exposure=Mathf.InverseLerp(10,20,temperature)*(1-Mathf.InverseLerp(.28f,.65f,wetness));
            return (ushort)Mathf.RoundToInt(exposure*exposure*(3-2*exposure)*256);
        }
        internal static Color32 NaturalLand(VegetationCover cover,GroundMaterial material,float wetness,uint coverColor,uint materialColor,ushort rockWeathering)
        {
            var substrate=RGB(materialColor);
            if(material==GroundMaterial.Snow)return substrate;
            if(cover==VegetationCover.Ice)return RGB(coverColor);
            if(material==GroundMaterial.Rock&&rockWeathering>0)
                substrate=MixAlbedo(substrate,RGB(0xad906f),rockWeathering);
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
            return NaturalLand(w,i,w.Facts.Vegetation[i],w.Facts.Materials[i]);
        }
        static Color32 NaturalLand(WorldData w,int i,VegetationCover cover,GroundMaterial material)
        {
            float wetness=w.Facts.Wetness[i];
            ushort weathering=RockWeathering(material,w.Biomes[i],w.Facts.MeanTemperature[i],wetness);
            return NaturalLand(cover,material,wetness,VegetationColors[(int)cover],MaterialColors[(int)material],weathering);
        }
        // Offshore climate cells still have monthly normals and a land water-balance
        // estimate. Use those for the dry underlay of islands below the grid scale.
        // Gameplay water, habitat and building ownership remain per simulation cell.
        internal static VegetationCover IslandCover(WorldData world, int tile)
        {
            var facts = world.Facts;
            var climate = world.Climates[tile];
            float temperature = facts.MeanTemperature[tile], rain = facts.AnnualRain[tile];
            var biome = FlatTerrainAnalysis.ClassifyBiome(climate, 0, temperature, rain,
                facts.SaturatedMonths[tile], 0, 0, false, false);
            return FlatEcology.Classify(biome, climate, temperature, rain, 0);
        }
        internal static GroundMaterial IslandSubstrate(VegetationCover cover)
            => cover == VegetationCover.Bare ? GroundMaterial.Rock : GroundMaterial.Soil;
        public static Color32 Terrain(WorldData w,int i)
        {
            bool area=FlatTerrainAnalysis.AreaWater(w.Water[i]);
            Color32 c=area?Water(w.Water[i]):LandColor(w,i);
            if (area && w.Preset == TerrainPreset.Earth && w.WaterReference != null && w.Facts != null)
            {
                var cover = IslandCover(w,i);
                var material = IslandSubstrate(cover);
                c = NaturalLand(w, i, cover, material);
            }
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
            if(layer>=MapLayer.ExoConvectiveRain)
            {
                ExoPlaSimSupplement.EvaluateMapmodeColor(w,i,w.Month,ExoPlaSimSupplement.MapLayerToExoMode(layer),out byte r,out byte g,out byte b);
                return new Color32(r,g,b,255);
            }
            if(layer>=MapLayer.SurfaceWinds)
            {
                GlebaGeographySupplement.PackMapmodeDisplay(w,i,w.Month,GlebaGeographySupplement.MapLayerToGlebaMode(layer),out byte r,out byte g,out byte b,out byte a);
                return new Color32(r,g,b,a);
            }
            switch(layer)
            {
                case MapLayer.Nations:break; // Exactly one political tint, in the shader.
                case MapLayer.Height: c=FlatTerrainAnalysis.AreaWater(w.Water[i])?Water(w.Water[i]):Color.Lerp(RGB(0x597b62),RGB(0xe4e1d2),w.Elevation[i]/4000f);break;
                case MapLayer.Climate:
                    c=w.Water[i]>=WaterKind.DeepOcean&&w.Water[i]<=WaterKind.ShallowOcean
                        ?(w.UsesGlobalStraits?RGB(ClimateOceanColor):Water(w.Water[i])):RGB(ClimateColors[(int)w.Climates[i]]);
                    break;
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
