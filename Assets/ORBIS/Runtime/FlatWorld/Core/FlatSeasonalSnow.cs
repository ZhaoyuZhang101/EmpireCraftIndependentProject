using System;

namespace Orbis.Flat
{
    // Reused for one latitude row. Rain is the water reaching the surface in
    // mm per 30-day month. Temperature and shortwave use the same calendar.
    internal sealed class FlatSeasonalSnow
    {
        const double MaxActiveSwe = 600; // mm, deeper perennial ice leaves the seasonal store
        const double Memory = .26359713811572677; // exp(-30 days / 22.5 days)
        internal readonly float[] Rain = new float[12], Cloud = new float[12];
        readonly double[] solidFraction = new double[12], meltPotential = new double[12], forcing = new double[12], snowSeason = new double[12];

        static double Smooth(double f)
        {
            f = Math.Max(0, Math.Min(1,f));
            return f*f*(3-2*f);
        }

        // Snow is added at the start of the monthly budget. The fraction of the
        // month before disappearance scales its patchy mean snow-covered area.
        static void Month(ref double swe, double snowfall, double demand, out double melt, out double cover)
        {
            double available = swe+snowfall;
            melt = Math.Min(available,demand);
            swe = Math.Min(MaxActiveSwe, available-melt);
            double duration = Math.Min(1,available/Math.Max(.001,demand));
            cover = duration*(1-Math.Exp(-(available+swe)/50));
        }

        internal void Apply(float[] temperature, int offset, float[] sunlight)
        {
            double rain = 0, freeze = 0, thaw = 0, growing = 0;
            for (int m = 0; m < 12; m++)
            {
                double t = temperature[offset+m];
                rain += Rain[m]; freeze += Math.Max(0,-t); thaw += Math.Max(0,t);
                growing += 30*Math.Max(0,t-5);
                solidFraction[m] = Smooth((2-t)/4);
                // Smooth positive degree days near the mixed rain/snow boundary.
                double degreeDays = 15*(Math.Sqrt(t*t+4)+t)*Smooth((t+8)/8);
                meltPotential[m] = 3*degreeDays*(.65+.35*sunlight[m]*Math.Max(.65,1-.35*Cloud[m]));
            }
            if (freeze == 0 || rain == 0) return;
            double meanRain = rain/12, activeNet = 0, uniformNet = 0;
            for (int m = 0; m < 12; m++)
            {
                activeNet += Rain[m]*solidFraction[m]-meltPotential[m];
                uniformNet += meanRain*solidFraction[m]-meltPotential[m];
            }
            // The receiving soil responds to snow timing relative to equal monthly
            // delivery of the same annual precipitation at this land surface. Both
            // integrations use the same radiation, temperature and canopy shielding.
            double swe = activeNet > 0 ? MaxActiveSwe : 0;
            double reference = uniformNet > 0 ? MaxActiveSwe : 0;
            double canopy = growing/(growing+500)*rain/(rain+400);
            double meanFlux = 0, meanSnow = 0;
            // A circuit beginning at the correct extreme finds the periodic
            // boundary. Only the second circuit needs snow-cover exponentials.
            for (int m = 0; m < 12; m++)
            {
                swe = Math.Max(0,Math.Min(MaxActiveSwe,swe+Rain[m]*solidFraction[m]-meltPotential[m]));
                reference = Math.Max(0,Math.Min(MaxActiveSwe,reference+meanRain*solidFraction[m]-meltPotential[m]));
            }
            for (int m = 0; m < 12; m++)
            {
                Month(ref swe,Rain[m]*solidFraction[m],meltPotential[m],out double melt,out double cover);
                Month(ref reference,meanRain*solidFraction[m],meltPotential[m],out double baselineMelt,out double baselineCover);
                double incident = 300*sunlight[m]*(.74-.34*Cloud[m]);
                double absorbed = -(cover-baselineCover)*incident*(.42-.20*canopy);
                double fusion = -(melt-baselineMelt)*334000/(30*86400);
                forcing[m] = absorbed+fusion;
                snowSeason[m] = Math.Max(cover,baselineCover);
                meanFlux += forcing[m]/12;
                meanSnow += snowSeason[m]/12;
            }
            // The annual background already includes mean snow/climate effects.
            // Preserve that mean and solve the seasonal soil response across December.
            if (meanSnow == 0) return;
            double frozenShare = freeze/(freeze+thaw), previous = 0;
            for (int m = 0; m < 12; m++)
            {
                // The seasonal store releases its offset while snow persists. A
                // larger annual freezing index spreads more heat into the deep soil.
                forcing[m] -= meanFlux*(frozenShare+(1-frozenShare)*snowSeason[m]/meanSnow);
                previous = Memory*previous+(1-Memory)*forcing[m];
            }
            previous /= 1-Math.Pow(Memory,12);
            double gain = .03524*frozenShare; // fitted K per W/m2 at complete seasonal freezing
            for (int m = 0; m < 12; m++)
            {
                previous = Memory*previous+(1-Memory)*forcing[m];
                temperature[offset+m] = FMath.Clamp(temperature[offset+m]+(float)(gain*previous),-85,55);
            }
        }
    }
}
