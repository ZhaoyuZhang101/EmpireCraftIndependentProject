#!/usr/bin/env python3
"""Current C# equation bodies -> C++ numerical execution, NOT C#/Unity validation.
Uses the original V4.2 translator; adds language scaffolding for repair-only fields.
"""
import validate_v42 as base
from pathlib import Path
import sys
base.RESULT=base.ROOT/'Documentation/V4_2_Repair/validation_core.json'
base.HEADER=base.HEADER.replace('const double PI=', 'double Tanh(double x){return tanh(x);} double Sign(double x){return (x>0)-(x<0); } const double PI=')
base.HEADER=base.HEADER.replace('double Length()const', 'AtmosVector& operator+=(AtmosVector b){*this=*this+b;return *this;} double Length()const')
base.HEADER=base.HEADER.replace('Position,Wind,baseWind;', 'Position,Wind,baseWind,pressureGradient,eastDirection; vector<double> jetStrength;')
base.HEADER=base.HEADER.replace('baseWind.resize(n);', 'baseWind.resize(n);pressureGradient.resize(n);eastDirection.resize(n);jetStrength.resize(n);for(int i=0;i<n;i++){auto p=Position[i];eastDirection[i]=AtmosVector(p.Z,0,-p.X).Normalized();jetStrength[i]=exp(-pow((fabs(p.Y)-.60)/.22,2));}')
original=base.translate
source=base.SOURCE.read_text()
def translate(name,args,body):
    body=body.replace('var centers=new AtmosVector[6];var radii=new double[6];', 'vector<AtmosVector> centers(6);vector<double> radii(6);').replace('centers.Length','centers.size()')
    body=body.replace('Array.Clear(pressureGradient,0,Count);','std::fill(pressureGradient.begin(),pressureGradient.end(),AtmosVector());')
    out=original(name,args,body)
    if name=='Saturation':out+=original('SaturationSlope',*base.method(source,'SaturationSlope'))
    return out
base.translate=translate
if __name__=='__main__':
    try:sys.exit(base.main())
    except base.subprocess.CalledProcessError as e:
        print(e.stderr);raise
