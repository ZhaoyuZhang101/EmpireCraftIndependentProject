using System;

namespace Orbis.Flat
{
    // CPU shallow-water solver with Audusse-style hydrostatic reconstruction on real beds,
    // dynamic wetting/drying (no permanent wet mask), donor-available positivity limiting,
    // semi-implicit Manning bed friction, Flather radiation open boundaries, and exact
    // source/boundary mass accounting. Pure managed C#; no Unity or HDRP dependencies.
    public sealed class FlatShallowWater
    {
        public const double Gravity = 9.81;
        public const double DryEpsilon = 1e-6;
        public const double WetDepthThreshold = 1e-4;
        public const double VisibleFloodThreshold = 0.05;

        public readonly int Width, Height, Count;
        public readonly double Dx;
        public readonly double[] Bed, Depth, VelocityX, VelocityY, Foam;
        public readonly double[] RestSurface, InitialDepth;
        public readonly bool[] InitiallyWet;

        // Optional external forcing fields (SI units: m head, m^2/s^2 kinematic stress, m/s source rates).
        public readonly double[] PressureHead, WindStressX, WindStressY;
        public readonly double[] RainRate, ChannelInflowRate, InfiltrationRate;

        readonly bool[] openBoundary;
        readonly double[] boundarySurface;
        readonly double[] east, north, scale, next;
        readonly double[] westOut, eastOut, southOut, northOut;
        readonly double manningN, linearDrag;
        readonly bool legacyStepCap;

        public double InitialVolume { get; private set; }
        public double RainInputVolume { get; private set; }
        public double ChannelInflowVolume { get; private set; }
        public double BoundaryInflowVolume { get; private set; }
        public double BoundaryOutflowVolume { get; private set; }
        public double InfiltrationVolume { get; private set; }
        public double SeabedUpliftVolume { get; private set; }
        public double SeabedSubsidenceVolume { get; private set; }
        public double Elapsed { get; private set; }
        public int LastSubsteps { get; private set; }

        // Legacy perturbation constructor retained for existing ripple & self-test callers.
        public FlatShallowWater(int width, int height, double cellMeters, float[] restDepth)
        {
            if (width < 2 || height < 2 || width > 256 || height > 256 || restDepth == null ||
                restDepth.Length != width * height || cellMeters <= 0 || double.IsInfinity(cellMeters) || double.IsNaN(cellMeters))
                throw new ArgumentException("Invalid local shallow-water grid.");

            Width = width; Height = height; Count = width * height; Dx = cellMeters;
            manningN = 0.0; linearDrag = 0.18; legacyStepCap = true;

            Bed = new double[Count]; Depth = new double[Count];
            VelocityX = new double[Count]; VelocityY = new double[Count]; Foam = new double[Count];
            RestSurface = new double[Count]; InitialDepth = new double[Count]; InitiallyWet = new bool[Count];
            PressureHead = new double[Count]; WindStressX = new double[Count]; WindStressY = new double[Count];
            RainRate = new double[Count]; ChannelInflowRate = new double[Count]; InfiltrationRate = new double[Count];
            openBoundary = new bool[Count]; boundarySurface = new double[Count];
            east = new double[Count]; north = new double[Count]; scale = new double[Count]; next = new double[Count];
            westOut = new double[Height]; eastOut = new double[Height];
            southOut = new double[Width]; northOut = new double[Width];

            for (int i = 0; i < Count; i++)
            {
                if (!FMath.Finite(restDepth[i]) || restDepth[i] < 0 || restDepth[i] > 10000)
                    throw new ArgumentException("Invalid water depth.");
                bool wet = restDepth[i] > 1e-4f;
                InitiallyWet[i] = wet;
                Depth[i] = InitialDepth[i] = wet ? restDepth[i] : 0.0;
                // Dry bank cells in the perturbation-only constructor represent walls above the eta=0 rest surface.
                Bed[i] = wet ? -Depth[i] : 10.0;
                RestSurface[i] = wet ? 0.0 : Bed[i];
                boundarySurface[i] = RestSurface[i];
            }
            InitialVolume = Volume();
        }

        // Real-bed wet-dry constructor for flood, storm surge, and tsunami scenarios.
        public FlatShallowWater(int width, int height, double cellMeters, double[] bedElevation, double[] initialDepth,
            double manningRoughness = 0.028, double backgroundDrag = 0.0015)
        {
            if (width < 2 || height < 2 || width > 256 || height > 256 ||
                bedElevation == null || initialDepth == null ||
                bedElevation.Length != width * height || initialDepth.Length != width * height ||
                cellMeters <= 0 || double.IsInfinity(cellMeters) || double.IsNaN(cellMeters))
                throw new ArgumentException("Invalid real-bed shallow-water grid.");
            if (double.IsNaN(manningRoughness) || double.IsInfinity(manningRoughness) || manningRoughness < 0 ||
                double.IsNaN(backgroundDrag) || double.IsInfinity(backgroundDrag) || backgroundDrag < 0)
                throw new ArgumentOutOfRangeException("Invalid friction parameters.");

            Width = width; Height = height; Count = width * height; Dx = cellMeters;
            manningN = manningRoughness; linearDrag = backgroundDrag; legacyStepCap = false;

            Bed = new double[Count]; Depth = new double[Count];
            VelocityX = new double[Count]; VelocityY = new double[Count]; Foam = new double[Count];
            RestSurface = new double[Count]; InitialDepth = new double[Count]; InitiallyWet = new bool[Count];
            PressureHead = new double[Count]; WindStressX = new double[Count]; WindStressY = new double[Count];
            RainRate = new double[Count]; ChannelInflowRate = new double[Count]; InfiltrationRate = new double[Count];
            openBoundary = new bool[Count]; boundarySurface = new double[Count];
            east = new double[Count]; north = new double[Count]; scale = new double[Count]; next = new double[Count];
            westOut = new double[Height]; eastOut = new double[Height];
            southOut = new double[Width]; northOut = new double[Width];

            for (int i = 0; i < Count; i++)
            {
                double b = bedElevation[i], d = initialDepth[i];
                if (double.IsNaN(b) || double.IsInfinity(b) || b < -15000 || b > 15000)
                    throw new ArgumentException("Invalid bed elevation.");
                if (double.IsNaN(d) || double.IsInfinity(d) || d < 0 || d > 15000)
                    throw new ArgumentException("Invalid initial water depth.");
                Bed[i] = b;
                Depth[i] = InitialDepth[i] = d;
                bool wet = d > WetDepthThreshold;
                InitiallyWet[i] = wet;
                RestSurface[i] = wet ? b + d : b;
                boundarySurface[i] = RestSurface[i];
            }
            InitialVolume = Volume();
        }

        public bool IsWet(int i) => i >= 0 && i < Count && Depth[i] > WetDepthThreshold;

        public bool IsInundatedDryCell(int i, double threshold = VisibleFloodThreshold)
            => i >= 0 && i < Count && !InitiallyWet[i] && Depth[i] >= threshold;

        public double SurfaceElevation(int i) => Bed[i] + Depth[i];

        // Zero everywhere at initial rest; equals exact flood depth on initially dry land;
        // equals free-surface surge/wave crest or trough anomaly on initially wet water.
        public double ExcessHead(int i) => (Bed[i] + Depth[i]) - RestSurface[i];

        public void SetOpenBoundary(int cell, bool isOpen, double referenceSurface)
        {
            if ((uint)cell >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(cell));
            if (double.IsNaN(referenceSurface) || double.IsInfinity(referenceSurface))
                throw new ArgumentOutOfRangeException(nameof(referenceSurface));
            openBoundary[cell] = isOpen;
            boundarySurface[cell] = referenceSurface;
        }

        public void ClearForcing()
        {
            Array.Clear(PressureHead, 0, Count);
            Array.Clear(WindStressX, 0, Count);
            Array.Clear(WindStressY, 0, Count);
            Array.Clear(RainRate, 0, Count);
            Array.Clear(ChannelInflowRate, 0, Count);
            Array.Clear(InfiltrationRate, 0, Count);
        }

        // Coseismic seafloor displacement lifts/lowers the bed without creating or destroying water column thickness.
        public void DisplaceBed(int cell, double deltaMeters)
        {
            if ((uint)cell >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(cell));
            if (double.IsNaN(deltaMeters) || double.IsInfinity(deltaMeters) || deltaMeters == 0) return;
            Bed[cell] += deltaMeters;
            if (Depth[cell] > WetDepthThreshold)
            {
                if (deltaMeters > 0) SeabedUpliftVolume += deltaMeters * Dx * Dx;
                else SeabedSubsidenceVolume += (-deltaMeters) * Dx * Dx;
            }
        }

        public static void ComputeWindStress(double windEastMs, double windNorthMs, out double tauOverRhoX, out double tauOverRhoY)
        {
            double speed = Math.Sqrt(windEastMs * windEastMs + windNorthMs * windNorthMs);
            if (speed <= 1e-6) { tauOverRhoX = 0; tauOverRhoY = 0; return; }
            double cd = Math.Min(0.0028, (0.75 + 0.067 * speed) * 1e-3);
            const double rhoAirOverWater = 1.22 / 1025.0;
            double factor = rhoAirOverWater * cd * speed;
            tauOverRhoX = factor * windEastMs;
            tauOverRhoY = factor * windNorthMs;
        }

        public static double InverseBarometerMeters(double pressureDeficitHpa)
        {
            return Math.Max(-2.0, Math.Min(3.0, pressureDeficitHpa * 100.0 / (1025.0 * Gravity)));
        }

        public double Volume()
        {
            double sum = 0;
            for (int i = 0; i < Count; i++) sum += Depth[i];
            return sum * Dx * Dx;
        }

        public double ExpectedVolume =>
            InitialVolume + RainInputVolume + ChannelInflowVolume + BoundaryInflowVolume
            - BoundaryOutflowVolume - InfiltrationVolume;

        public double MassBalanceError => Math.Abs(Volume() - ExpectedVolume);

        public double RelativeMassError()
        {
            double scaleRef = Math.Max(1.0, Math.Max(InitialVolume, ExpectedVolume));
            return Math.Abs(Volume() - ExpectedVolume) / scaleRef;
        }

        // An exactly balanced free-surface disturbance. No water is created by this button.
        public void Ripple(int cx, int cy, double amplitude)
        {
            if (cx < 0 || cx >= Width || cy < 0 || cy >= Height || !IsWet(cy * Width + cx)) return;
            amplitude = Math.Max(0, Math.Min(.08, amplitude));
            double removed = 0;
            int center = cy * Width + cx;
            for (int y = Math.Max(0, cy - 4); y <= Math.Min(Height - 1, cy + 4); y++)
            for (int x = Math.Max(0, cx - 4); x <= Math.Min(Width - 1, cx + 4); x++)
            {
                int i = y * Width + x;
                if (i == center || !IsWet(i)) continue;
                double amount = Math.Min(Depth[i] * .015, amplitude * Math.Exp(-((x - cx) * (x - cx) + (y - cy) * (y - cy)) * .3) * .08);
                Depth[i] -= amount;
                removed += amount;
            }
            Depth[center] += removed;
        }

        public void Advance(double requestedSeconds, int maxSubsteps = 16)
        {
            if (double.IsNaN(requestedSeconds) || double.IsInfinity(requestedSeconds) || requestedSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(requestedSeconds));
            double remaining = legacyStepCap ? Math.Min(0.2, requestedSeconds) : requestedSeconds;
            int budget = Math.Max(1, Math.Min(256, maxSubsteps));
            LastSubsteps = 0;
            while (remaining > 1e-9 && LastSubsteps < budget)
            {
                double maxWaveSpeed = 0.25;
                for (int i = 0; i < Count; i++)
                {
                    double d = Depth[i];
                    if (d > DryEpsilon)
                    {
                        double speed = Math.Sqrt(Gravity * d) + Math.Abs(VelocityX[i]) + Math.Abs(VelocityY[i]);
                        if (speed > maxWaveSpeed) maxWaveSpeed = speed;
                    }
                    else if (RainRate[i] > 0 || ChannelInflowRate[i] > 0)
                    {
                        if (1.0 > maxWaveSpeed) maxWaveSpeed = 1.0;
                    }
                }
                for (int y = 0; y < Height; y++)
                {
                    int wCell = y * Width, eCell = wCell + Width - 1;
                    if (openBoundary[wCell]) maxWaveSpeed = Math.Max(maxWaveSpeed, Math.Sqrt(Gravity * Math.Max(0, boundarySurface[wCell] - Bed[wCell])));
                    if (openBoundary[eCell]) maxWaveSpeed = Math.Max(maxWaveSpeed, Math.Sqrt(Gravity * Math.Max(0, boundarySurface[eCell] - Bed[eCell])));
                }
                for (int x = 0; x < Width; x++)
                {
                    int sCell = x, nCell = (Height - 1) * Width + x;
                    if (openBoundary[sCell]) maxWaveSpeed = Math.Max(maxWaveSpeed, Math.Sqrt(Gravity * Math.Max(0, boundarySurface[sCell] - Bed[sCell])));
                    if (openBoundary[nCell]) maxWaveSpeed = Math.Max(maxWaveSpeed, Math.Sqrt(Gravity * Math.Max(0, boundarySurface[nCell] - Bed[nCell])));
                }

                double cflDt = (legacyStepCap ? 0.30 : 0.38) * Dx / maxWaveSpeed;
                double maxDt = legacyStepCap ? 0.04 : 4.0;
                double dt = Math.Min(remaining, Math.Min(maxDt, cflDt));
                Step(dt);
                remaining -= dt;
                Elapsed += dt;
                LastSubsteps++;
            }
        }

        double UpdateInteriorFace(int left, int right, double qPrev, double windL, double windR, double dt)
        {
            double bL = Bed[left], bR = Bed[right];
            double etaL = bL + Depth[left], etaR = bR + Depth[right];
            double sill = bL >= bR ? bL : bR;
            double hL = etaL > sill ? etaL - sill : 0.0;
            double hR = etaR > sill ? etaR - sill : 0.0;
            if (hL <= DryEpsilon && hR <= DryEpsilon) return 0.0;

            double etaLStar = sill + hL, etaRStar = sill + hR;
            double hFace = (hL > DryEpsilon && hR > DryEpsilon) ? 0.5 * (hL + hR) : (hL >= hR ? hL : hR);
            double minStar = hL < hR ? hL : hR;
            double pWeight = minStar >= 0.05 ? 1.0 : minStar / 0.05;
            double dHead = (etaLStar - etaRStar) - pWeight * (PressureHead[left] - PressureHead[right]);

            double windWeight = hFace / (hFace + 0.25);
            double tau = 0.5 * (windL + windR) * windWeight;

            double q = qPrev + (Gravity * hFace * dHead / Dx + tau) * dt;
            double hFric = hFace > 0.04 ? hFace : 0.04;
            double friction = linearDrag;
            if (manningN > 0.0)
                friction += Gravity * manningN * manningN * Math.Abs(q) / Math.Pow(hFric, 7.0 / 3.0);
            q /= (1.0 + dt * friction);

            // Water below the hydrostatically reconstructed sill cannot cross a dry levee.
            double donorStar = q >= 0.0 ? hL : hR;
            double maxSillFlux = 0.5 * donorStar * Dx / dt;
            if (Math.Abs(q) > maxSillFlux)
                q = q >= 0.0 ? maxSillFlux : -maxSillFlux;
            return q;
        }

        double ComputeRadiationOutflow(int cell)
        {
            if (!openBoundary[cell]) return 0.0;
            double b = Bed[cell], hIn = Depth[cell];
            double etaRef = boundarySurface[cell];
            double hExt = etaRef > b ? etaRef - b : 0.0;
            if (hIn <= DryEpsilon && hExt <= DryEpsilon) return 0.0;
            double hBar = hIn >= hExt ? hIn : hExt;
            double etaInStar = b + hIn;
            double etaExtStar = b + hExt;
            return Math.Sqrt(Gravity * hBar) * (etaInStar - etaExtStar);
        }

        void Step(double dt)
        {
            // 1. Source additions (rainfall + upstream channel flood hydrograph) with exact mass ledger.
            double cellArea = Dx * Dx;
            double rainStep = 0.0, chanStep = 0.0;
            for (int i = 0; i < Count; i++)
            {
                double r = RainRate[i] > 0.0 ? RainRate[i] * dt : 0.0;
                double c = ChannelInflowRate[i] > 0.0 ? ChannelInflowRate[i] * dt : 0.0;
                if (r > 0.0 || c > 0.0)
                {
                    Depth[i] += r + c;
                    rainStep += r;
                    chanStep += c;
                }
            }
            RainInputVolume += rainStep * cellArea;
            ChannelInflowVolume += chanStep * cellArea;

            // 2. Interior staggered face fluxes with hydrostatic reconstruction.
            for (int y = 0; y < Height; y++)
            {
                int row = y * Width;
                for (int x = 0; x < Width; x++)
                {
                    int i = row + x;
                    east[i] = (x + 1 < Width)
                        ? UpdateInteriorFace(i, i + 1, east[i], WindStressX[i], WindStressX[i + 1], dt)
                        : 0.0;
                    north[i] = (y + 1 < Height)
                        ? UpdateInteriorFace(i, i + Width, north[i], WindStressY[i], WindStressY[i + Width], dt)
                        : 0.0;
                }
            }

            // 3. Exterior Flather radiation boundary fluxes (positive = outward from domain).
            for (int y = 0; y < Height; y++)
            {
                westOut[y] = ComputeRadiationOutflow(y * Width);
                eastOut[y] = ComputeRadiationOutflow(y * Width + Width - 1);
            }
            for (int x = 0; x < Width; x++)
            {
                southOut[x] = ComputeRadiationOutflow(x);
                northOut[x] = ComputeRadiationOutflow((Height - 1) * Width + x);
            }

            // 4. Donor availability scaling across all four faces (interior + open exterior).
            for (int y = 0; y < Height; y++)
            {
                int row = y * Width;
                for (int x = 0; x < Width; x++)
                {
                    int i = row + x;
                    double outward = 0.0;
                    outward += x + 1 < Width ? Math.Max(0.0, east[i]) : Math.Max(0.0, eastOut[y]);
                    outward += x > 0 ? Math.Max(0.0, -east[i - 1]) : Math.Max(0.0, westOut[y]);
                    outward += y + 1 < Height ? Math.Max(0.0, north[i]) : Math.Max(0.0, northOut[x]);
                    outward += y > 0 ? Math.Max(0.0, -north[i - Width]) : Math.Max(0.0, southOut[x]);
                    scale[i] = outward > 0.0 ? Math.Min(1.0, Depth[i] * Dx / (dt * outward)) : 1.0;
                }
            }

            for (int y = 0; y < Height; y++)
            {
                int row = y * Width;
                for (int x = 0; x < Width; x++)
                {
                    int i = row + x;
                    if (x + 1 < Width) east[i] *= east[i] >= 0.0 ? scale[i] : scale[i + 1];
                    if (y + 1 < Height) north[i] *= north[i] >= 0.0 ? scale[i] : scale[i + Width];
                }
                if (westOut[y] > 0.0) westOut[y] *= scale[row];
                if (eastOut[y] > 0.0) eastOut[y] *= scale[row + Width - 1];
            }
            for (int x = 0; x < Width; x++)
            {
                if (southOut[x] > 0.0) southOut[x] *= scale[x];
                if (northOut[x] > 0.0) northOut[x] *= scale[(Height - 1) * Width + x];
            }

            // Record exact exterior boundary inflows and outflows.
            double bcIn = 0.0, bcOut = 0.0;
            for (int y = 0; y < Height; y++)
            {
                if (westOut[y] >= 0.0) bcOut += westOut[y]; else bcIn += -westOut[y];
                if (eastOut[y] >= 0.0) bcOut += eastOut[y]; else bcIn += -eastOut[y];
            }
            for (int x = 0; x < Width; x++)
            {
                if (southOut[x] >= 0.0) bcOut += southOut[x]; else bcIn += -southOut[x];
                if (northOut[x] >= 0.0) bcOut += northOut[x]; else bcIn += -northOut[x];
            }
            BoundaryInflowVolume += bcIn * Dx * dt;
            BoundaryOutflowVolume += bcOut * Dx * dt;

            // 5. Conservative depth update, optional land infiltration sink, velocity & bore/front foam.
            double infStep = 0.0;
            for (int y = 0; y < Height; y++)
            {
                int row = y * Width;
                for (int x = 0; x < Width; x++)
                {
                    int i = row + x;
                    double qW = x > 0 ? east[i - 1] : -westOut[y];
                    double qE = x + 1 < Width ? east[i] : eastOut[y];
                    double qS = y > 0 ? north[i - Width] : -southOut[x];
                    double qN = y + 1 < Height ? north[i] : northOut[x];
                    double netFlux = qW - qE + qS - qN;
                    double hNew = Depth[i] + (dt / Dx) * netFlux;
                    if (hNew < -1e-9 || double.IsNaN(hNew) || double.IsInfinity(hNew))
                        throw new InvalidOperationException("Shallow water left its numerical range.");
                    if (hNew < 0.0) hNew = 0.0;

                    if (InfiltrationRate[i] > 0.0 && hNew > 0.0)
                    {
                        double lost = Math.Min(hNew, InfiltrationRate[i] * dt);
                        hNew -= lost;
                        infStep += lost;
                    }
                    next[i] = hNew;

                    if (hNew > WetDepthThreshold)
                    {
                        double invH = 1.0 / Math.Max(0.04, hNew);
                        double ux = 0.5 * (qW + qE) * invH;
                        double uy = 0.5 * (qS + qN) * invH;
                        VelocityX[i] = ux;
                        VelocityY[i] = uy;
                        double speed = Math.Sqrt(ux * ux + uy * uy);
                        double convergence = Math.Max(0.0, netFlux / Dx);
                        double froude = (hNew > 0.14 && hNew < 8.0) ? speed / Math.Sqrt(Gravity * hNew + 0.25) : 0.0;
                        double boreShear = (froude > 0.88 && speed > 1.35) ? (froude - 0.88) * 0.26 : 0.0;
                        double sourceFoam = legacyStepCap
                            ? Math.Abs(netFlux) / Dx * 0.18
                            : convergence * 1.9 + boreShear;
                        Foam[i] = Math.Min(1.0, Foam[i] * Math.Exp(-dt * 1.45) + sourceFoam * dt);
                    }
                    else
                    {
                        VelocityX[i] = 0.0;
                        VelocityY[i] = 0.0;
                        Foam[i] *= Math.Exp(-dt * 2.8);
                    }
                }
            }
            InfiltrationVolume += infStep * cellArea;
            Array.Copy(next, Depth, Count);
        }
    }
}
