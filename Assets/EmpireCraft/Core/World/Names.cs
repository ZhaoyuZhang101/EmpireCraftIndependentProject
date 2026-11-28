namespace EmpireCraft.Core
{
    // Names for what the world makes: a person, a clan, a city, a realm. Given the people of what is named, the name comes
    // from that people's word lists (CultureNames); without one, or without the rule data, from the placeholder lists here.
    // Nothing here names a real dynasty or place.
    public static class Names
    {
        const uint Stream=0x4E414D45;
        static readonly string[] Surnames={"林","沈","陆","顾","苏","韩","程","叶","许","秦","温","蒋","方","邵","简","江","柳","萧","宁","岑","廖","纪","乔","闻","滕","汪","瞿","贺","骆","晏"};
        static readonly string[] GivenMale={"承","远","平","安","衡","昭","宁","谦","睿","泽","晖","铭","嘉","祐","恒","穆","策","澄","弘","霆"};
        static readonly string[] GivenFemale={"婉","清","宜","蘅","芷","若","瑶","姝","锦","岚","绮","素","萱","琳","晴","筠"};
        static readonly string[] Lead={"青","白","丹","云","石","松","川","泉","霞","金","苍","沧","临","安","平","长","桐","枫","雁","鹿","兰","澜","岚","洛"};
        static readonly string[] Tail={"川","州","原","阳","陵","泽","津","关","岭","渚","丘","城","野","坪","湾","堰","塘","桥","滩","谷"};

        public static string Surname(EmpireWorld w,int subject,string people=null)=>CultureNames.Surname(w,people,subject)??Surnames[w.Pick(Stream,subject,Surnames.Length,1)];
        public static string Given(EmpireWorld w,Sex sex,int subject,string people=null)
        {
            string named=CultureNames.Given(w,people,sex,subject);if(named!=null)return named;
            var pool=sex==Sex.Male?GivenMale:GivenFemale;
            string a=pool[w.Pick(Stream,subject,pool.Length,2)];
            return w.Chance(Stream,subject,3)<.5?a:a+pool[w.Pick(Stream,subject,pool.Length,4)];
        }
        public static string Place(EmpireWorld w,int subject,string people=null)
        {
            string named=CultureNames.Place(w,people,subject);if(named!=null)return named;
            for(int attempt=0;attempt<8;attempt++)
            {
                string n=Lead[w.Pick(Stream,subject,Lead.Length,10+attempt)]+Tail[w.Pick(Stream,subject,Tail.Length,30+attempt)];
                if(n[0]!=n[1])return n;
            }
            return Lead[0]+Tail[0];
        }
        public static string RealmOf(string capital)=>capital+"国";
        // The people of what is being named: a person's own, else that of the city they live in; a city's, else its realm's.
        public static string PeopleOf(Person p)=>p?.Culture?.Family??PeopleOf(p?.City);
        public static string PeopleOf(City c)=>c?.Culture?.Family??c?.Realm?.Culture?.Family;
        public static string PeopleOf(Realm r)=>r?.Culture?.Family??PeopleOf(r?.Capital);
        // A person's full name in their people's order: surname first, or the given name first (CultureNames.GivenFirst).
        public static string Full(Person p)
        {
            string surname=p.Clan!=null?p.Clan.Surname:"";
            if(surname.Length==0||!CultureNames.GivenFirst(PeopleOf(p)))return surname+p.Given;
            // A given name that ends in its own separator (the middle dot of a transcribed name) takes no space after it.
            string given=p.Given??"";char last=given.Length>0?given[given.Length-1]:' ';
            return given+(last=='·'||last=='・'||last==' '?"":" ")+surname;
        }
        public static uint Color(EmpireWorld w,int subject)
        {
            // Mid-saturation hues that stay readable over terrain.
            double h=w.Chance(Stream,subject,50)*6,s=.45+.25*w.Chance(Stream,subject,51),v=.70+.2*w.Chance(Stream,subject,52);
            int k=(int)h;double f=h-k,p=v*(1-s),q=v*(1-s*f),t=v*(1-s*(1-f));
            double r,g,b;
            switch(k%6){case 0:r=v;g=t;b=p;break;case 1:r=q;g=v;b=p;break;case 2:r=p;g=v;b=t;break;case 3:r=p;g=q;b=v;break;case 4:r=t;g=p;b=v;break;default:r=v;g=p;b=q;break;}
            return ((uint)(r*255)<<16)|((uint)(g*255)<<8)|(uint)(b*255);
        }
    }
}
