using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{
    // Hand-written half of RebellionStartupService: RaiseUprisingMilitia.
    //
    // When a city rises, the mod armed part of its grown-ups as a rebel militia, sized by how
    // fierce the grievance is (12% to 35% of the people, halved for a petty rising), so the
    // rising is not put out by the first enemy soldier to walk in. It had two ways of doing it:
    // with the population a number (its "abstract population" mode), the militia is raised
    // from the background population through CityPopulationSystem.RaiseMilitia; with persons on
    // the map, it also turned townsfolk into soldiers one by one and formed an army of them.
    //
    // Here the population is always a number and persons on record are not soldiers, so only
    // the first way applies: the same share and the same bounds (at least 3, at most 15 bands
    // per city), raised through the same call. Nothing is "captured" by soldiers walking into a
    // city here, so there is no capture to clear.
    public static partial class RebellionStartupService
    {
        public static int RaiseUprisingMilitia(Realm rebel, float fervor, IEnumerable<City> cities = null)
        {
            if (rebel == null || rebel.IsGone() || rebel.Cities == null) return 0;
            float share = MinMilitiaShare + (MaxMilitiaShare - MinMilitiaShare) * Math.Max(0f, Math.Min(1f, fervor));
            if (PettyRebellion.IsPetty(rebel)) share *= 0.5f;
            int raised = 0;
            foreach (City city in (cities ?? rebel.Cities).ToList())
            {
                if (city == null || city.IsGone() || city.Realm != rebel) continue;
                int militia = Math.Max(MinMilitia, Math.Min(15, (int)Math.Ceiling(CityPopulationSystem.Households(city) * share * 0.3f)));
                raised += CityPopulationSystem.RaiseMilitia(city, militia, share);
                // The city looks at its troops again at once, not a month later.
                CityExtension.CityExtraData cityData = city.GetOrCreate();
                if (cityData != null) cityData.last_army_check_ts = -1d;
            }
            return raised;
        }
    }
}
