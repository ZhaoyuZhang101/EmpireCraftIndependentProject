using System;

namespace Orbis.Flat
{
    public enum ClimateFeature
    {
        Temperature, Humidity, Pressure, Precipitation, WindSpeed, SoilMoisture, Elevation,
        Evaporation, Runoff, Discharge, Groundwater
    }

    public enum ClimateTransform { Identity, LogOnePlus }

    public readonly struct ClimateAxis
    {
        public readonly ClimateFeature Feature;
        public readonly ClimateTransform Transform;
        public readonly double Scale;

        public ClimateAxis(ClimateFeature feature, ClimateTransform transform=ClimateTransform.Identity, double scale=1)
        {
            if ((uint)feature>=(uint)ClimateFeatureSet.FeatureCount || (uint)transform>1 ||
                !(scale>0) || double.IsInfinity(scale)) throw new ArgumentOutOfRangeException();
            if(transform==ClimateTransform.LogOnePlus && (feature==ClimateFeature.Temperature || feature==ClimateFeature.Elevation))
                throw new ArgumentException("A signed climate feature cannot use the nonnegative logarithm.");
            Feature=feature; Transform=transform; Scale=scale;
        }

        internal double Encode(double value)
        {
            if(double.IsNaN(value)||double.IsInfinity(value)) return double.NaN;
            double x=value/Scale;
            if(Transform==ClimateTransform.Identity) return x;
            if(x<0) return double.NaN;
            return x<1e-8 ? x*(1-.5*x) : Math.Log(1+x);
        }
    }

    public sealed class ClimateFeatureSet
    {
        public const int FeatureCount=11;
        readonly ClimateAxis[] axes;
        public int Dimension => axes.Length;
        public ClimateAxis this[int i] => axes[i];
        public ClimateFeatureSet(params ClimateAxis[] selected)
        {
            if(selected==null||selected.Length==0||selected.Length>FeatureCount)throw new ArgumentException("Invalid climate feature count.");
            int used=0;
            foreach(var axis in selected)
            {
                int bit=1<<(int)axis.Feature;
                if((used&bit)!=0 || !(axis.Scale>0))throw new ArgumentException("Repeated or unconfigured feature.");
                used|=bit;
            }
            axes=(ClimateAxis[])selected.Clone();
        }

        static ClimateAxis Linear(ClimateFeature f) => new ClimateAxis(f);
        static ClimateAxis Log(ClimateFeature f) => new ClimateAxis(f,ClimateTransform.LogOnePlus);
        public static readonly ClimateFeatureSet Weather=new ClimateFeatureSet(Linear(ClimateFeature.Temperature),
            Linear(ClimateFeature.Humidity),Linear(ClimateFeature.Pressure),Log(ClimateFeature.Precipitation),Log(ClimateFeature.WindSpeed));
        public static readonly ClimateFeatureSet LandWater=new ClimateFeatureSet(Log(ClimateFeature.Precipitation),
            Log(ClimateFeature.Evaporation),Linear(ClimateFeature.SoilMoisture),Log(ClimateFeature.Runoff),Log(ClimateFeature.Groundwater));
        public static readonly ClimateFeatureSet RiverWater=new ClimateFeatureSet(Log(ClimateFeature.Precipitation),
            Log(ClimateFeature.Evaporation),Linear(ClimateFeature.SoilMoisture),Log(ClimateFeature.Runoff),
            Log(ClimateFeature.Groundwater),Log(ClimateFeature.Discharge));
        public static readonly ClimateFeatureSet LakeOutlet=new ClimateFeatureSet(Log(ClimateFeature.Precipitation),
            Log(ClimateFeature.Evaporation),Log(ClimateFeature.Discharge));
        public static readonly ClimateFeatureSet LakeWater=new ClimateFeatureSet(Log(ClimateFeature.Precipitation),
            Log(ClimateFeature.Evaporation));

        internal bool Matches(ClimateFeatureSet other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null || other.Dimension != Dimension) return false;
            for (int i=0; i<Dimension; i++)
                if (axes[i].Feature != other[i].Feature || axes[i].Transform != other[i].Transform ||
                    axes[i].Scale != other[i].Scale) return false;
            return true;
        }

        public static double Value(in FlatClimateObservation observation,ClimateFeature feature)
        {
            switch(feature) {
                case ClimateFeature.Temperature:return observation.Temperature;
                case ClimateFeature.Humidity:return observation.Humidity;
                case ClimateFeature.Pressure:return observation.Pressure;
                case ClimateFeature.Precipitation:return observation.Precipitation;
                case ClimateFeature.WindSpeed:return observation.WindSpeed;
                case ClimateFeature.SoilMoisture:return observation.SoilMoisture;
                case ClimateFeature.Elevation:return observation.Elevation;
                case ClimateFeature.Evaporation:return observation.Evaporation;
                case ClimateFeature.Runoff:return observation.Runoff;
                case ClimateFeature.Discharge:return observation.Discharge;
                case ClimateFeature.Groundwater:return observation.Groundwater;
                default:throw new ArgumentOutOfRangeException(nameof(feature));
            }
        }

        public int IndexOf(ClimateFeature feature)
        {
            for(int i=0;i<Dimension;i++) if(axes[i].Feature==feature)return i;
            return -1;
        }
    }
}
