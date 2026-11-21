using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using Orbis.Flat;

internal static class CompareKoppen
{
    // Use the game's classifier for both sets of monthly normals at the same
    // resolution. A class inferred from gridded normals is a diagnostic.
    public static void Run(string export)
    {
        using var model = new BinaryReader(File.OpenRead(export));
        int magic=model.ReadInt32(), width=model.ReadInt32(), height=model.ReadInt32();
        if(magic!=0x314c4345 || width!=256 || height!=128)
            throw new InvalidDataException("Expected the ECL1 export on the common 256 x 128 grid.");
        int count=width*height;
        long temperatureOffset=12L+5L*count, rainOffset=temperatureOffset+48L*count;
        var obsT=new float[12]; var obsP=new float[12];
        var modelT=new float[12]; var modelP=new float[12];
        using var compressed=File.OpenRead("Validation/EarthClimate/holdout.csv.gz");
        using var data=new StreamReader(new GZipStream(compressed,CompressionMode.Decompress));
        data.ReadLine();
        int used=0; double total=0, full=0, broad=0;
        string line;
        while((line=data.ReadLine())!=null)
        {
            var row=line.Split(',');
            int i=int.Parse(row[0],CultureInfo.InvariantCulture);
            double area=double.Parse(row[1],CultureInfo.InvariantCulture);
            double south=Math.PI*((double)(i/width)/height-.5);
            double tileArea=6371.0088*6371.0088*(2*Math.PI/width)*(Math.Sin(south+Math.PI/height)-Math.Sin(south));
            if(area<.5*tileArea)continue;
            model.BaseStream.Position=temperatureOffset+48L*i;
            for(int m=0;m<12;m++)modelT[m]=model.ReadSingle();
            model.BaseStream.Position=rainOffset+48L*i;
            for(int m=0;m<12;m++)
            {
                modelP[m]=model.ReadSingle();
                obsT[m]=float.Parse(row[3+m],CultureInfo.InvariantCulture);
                obsP[m]=float.Parse(row[15+m],CultureInfo.InvariantCulture);
            }
            var a=KoppenClassifier.Classify(obsT,obsP);
            var b=KoppenClassifier.Classify(modelT,modelP);
            if(a==b)full+=area;
            if(a.ToString()[0]==b.ToString()[0])broad+=area;
            total+=area;used++;
        }
        if(used==0)throw new InvalidDataException("No matching climate cells.");
        Console.WriteLine($"{used} CRU holdout cells, area-weighted Köppen agreement: 5 groups {broad/total:P2}, 30 subtypes {full/total:P2}");
    }
}
