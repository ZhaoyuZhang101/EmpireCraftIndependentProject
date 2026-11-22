using System;
using System.IO;
using System.Linq;
using Orbis.Flat;

internal static class MoistureFluxRegression
{
    // A moist cell on the dateline sends water east and north in six hours.
    // Gather weights refer to the receiving area, which narrows towards the pole.
    static void CheckFaces()
    {
        const int width = 8, height = 4, row = 2, donor = row*width + width - 1;
        const double radius = 6371008.8, seconds = 21600, q = 24;
        var mass = new double[width * height];
        var faces = new FlatMoisture.TransportWeights[mass.Length];
        mass[donor] = 2 * q; // Sum of two equal beginning-of-step column samples.
        int acrossSeam = row * width, poleward = donor + width;
        double edge = Math.PI / 4;
        double area = radius * radius * edge * Math.Sin(edge);
        double polarArea = radius * radius * edge * (1 - Math.Sin(edge));
        faces[acrossSeam].West = .25f;
        faces[poleward].South = (float)(.125 * area / polarArea);
        FlatMoisture.ReportFlux(0, width, height, faces, mass, 2, .25f, (month, w, h, east, north) =>
        {
            double actualEast = east[donor] * radius * edge * seconds;
            double actualNorth = north[donor] * radius * edge * Math.Cos(edge) * seconds;
            if (Math.Abs(actualEast / (q * area * .25) - 1) > 1e-6 ||
                Math.Abs(actualNorth / (q * area * .125) - 1) > 1e-6 ||
                north[poleward] != 0 || east[acrossSeam] != 0)
                throw new InvalidDataException("Directed spherical face transport has the wrong sign, mass or SI units.");
        });
        mass[acrossSeam] = 2 * q;
        faces[donor].East = .5f;
        FlatMoisture.ReportFlux(0, width, height, faces, mass, 2, .25f, (month, w, h, east, north) =>
        {
            double transport = east[donor] * radius * edge * seconds;
            if (Math.Abs(transport / (-q * area * .25) - 1) > 1e-6)
                throw new InvalidDataException("Opposing flows must subtract at their common face.");
        });
        var stormFaces = new FlatMoisture.TransportWeights[mass.Length];
        var stormMass = new double[mass.Length];
        stormFaces[acrossSeam].West = .5f;
        stormMass[donor] = q;
        FlatMoisture.ReportFlux(0, width, height, faces, mass, 2, .25f, (month, w, h, east, north) =>
        {
            if (Math.Abs(east[donor]) > 1e-8 || north[donor] <= 0)
                throw new InvalidDataException("Organized and ambient moisture must sum at the same face.");
        }, stormFaces, stormMass);
    }

    public static void Run(string[] args)
    {
        CheckFaces();
        Console.WriteLine("PASS vapour mass and direction across a longitude seam and unequal-area latitude rings.");
        if (args.Length == 1) return;
        if (args.Length > 3) throw new ArgumentException("--moisture-flux [output.omf [Earth width]]");
        int width = args.Length == 3 ? int.Parse(args[2]) : 256;
        const string assets = "Assets/ORBIS/Resources/ORBIS/FlatWorld/";
        var world = FlatGenerator.Generate(width, width/2, "ORBIS-2D-2026", TerrainPreset.Earth, default,
            File.ReadAllBytes($"{assets}Earth{width}.bytes"), File.ReadAllBytes($"{assets}EarthWater{width}.bytes"),
            File.ReadAllBytes(assets + "EarthRivers.bytes"));
        var currents = FlatOceanCirculation.Build(world, FlatWorkerPolicy.Options(default));
        string output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        using var stream = new BinaryWriter(File.Create(output));
        stream.Write(System.Text.Encoding.ASCII.GetBytes("OMF1"));
        int months = 0;
        var measured = FlatMoisture.BuildWithCirculation(world, default, 0, true, currents, observeFlux:
            (month, w, h, east, north) =>
            {
                if (month != months++) throw new InvalidDataException("Monthly transport sampled out of order.");
                if (month == 0) { stream.Write(w); stream.Write(h); }
                foreach (double value in east) stream.Write((float)value);
                foreach (double value in north) stream.Write((float)value);
            });
        if (months != 12 || !measured.Rain.SequenceEqual(world.Moisture.Rain) ||
            !measured.BudgetError.SequenceEqual(world.Moisture.BudgetError))
            throw new InvalidDataException("The flux diagnostic must reproduce the player's last monthly moisture solve.");
        Console.WriteLine($"PASS twelve net east/north face fluxes in kg/(m s), with identical monthly rain and water residuals. {output}");
    }
}
