using System;
namespace Orbis.Flat
{
    // Beck et al. 2018, Table 2: 0 C C/D threshold and 30 classes (Aw includes As).
    // Input is climatological monthly means, NOT one frame's weather.
    public static class KoppenClassifier
    {
        public static Koppen Classify(float[] temperature,float[] precipitation,int offset=0)
        {
            if(temperature==null||precipitation==null||offset<0||temperature.Length-offset<12||precipitation.Length-offset<12)
                throw new ArgumentException("Twelve temperature and precipitation values are required.");
            float cold=1000,hot=-1000,mean=0,rain=0,driest=1e9f,aprTemp=0,octTemp=0;
            int warmMonths=0;
            for(int m=0;m<12;m++)
            {
                float t=temperature[offset+m],p=precipitation[offset+m];
                if(!FMath.Finite(t)||!FMath.Finite(p)||p<0)throw new ArgumentException("Invalid climatology.");
                cold=Math.Min(cold,t);hot=Math.Max(hot,t);mean+=t;rain+=p;driest=Math.Min(driest,p);
                if(t>10)warmMonths++;
                if(m>=3&&m<=8)aprTemp+=t;else octTemp+=t;
            }
            mean/=12;
            bool summerApr=aprTemp>=octTemp;
            float summer=0,winter=0,sDry=1e9f,wDry=1e9f,sWet=0,wWet=0;
            for(int m=0;m<12;m++)
            {
                float p=precipitation[offset+m];bool apr=m>=3&&m<=8;
                if(apr==summerApr){summer+=p;sDry=Math.Min(sDry,p);sWet=Math.Max(sWet,p);}
                else{winter+=p;wDry=Math.Min(wDry,p);wWet=Math.Max(wWet,p);}
            }
            float dryThreshold=Math.Max(0,20*mean+(summer>rain*.7f?280:winter>rain*.7f?0:140));
            if(rain<dryThreshold)
            { bool desert=rain<dryThreshold*.5f;return desert?(mean>=18?Koppen.BWh:Koppen.BWk):(mean>=18?Koppen.BSh:Koppen.BSk); }
            if(cold>=18)return driest>=60?Koppen.Af:driest>=100-rain/25?Koppen.Am:Koppen.Aw;
            if(hot<=10)return hot>0?Koppen.ET:Koppen.EF;
            bool s=sDry<40&&sDry<wWet/3, w=wDry<sWet/10;
            if(s&&w){s=winter>summer;w=!s;} // mutually exclusive, as Beck's implementation
            int dryness=s?0:w?1:2;
            if(cold>0)
            {
                int heat=hot>=22?0:warmMonths>=4?1:2;
                return (Koppen)((int)Koppen.Csa+dryness*3+heat);
            }
            int severe=hot>=22?0:warmMonths>=4?1:cold<-38?3:2;
            return (Koppen)((int)Koppen.Dsa+dryness*4+severe);
        }
    }
}
