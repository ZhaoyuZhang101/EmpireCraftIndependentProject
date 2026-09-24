import fs from 'node:fs';
import {getLeeTetrahedralEngine} from './source/mapshaper-lee-tetrahedral.mjs';
import {rotateSphericalRadians as rotate} from './source/mapshaper-polyhedral-projection.mjs';
const root=new URL('../../',import.meta.url),out=new URL('Assets/ORBIS/Resources/ORBIS/Markley/',root);fs.mkdirSync(out,{recursive:true});
const e=getLeeTetrahedralEngine('markley'),rt=Math.sqrt(3),W=1025,H=445;
const geo=n=>[Math.atan2(n[0],n[2]),Math.asin(Math.max(-1,Math.min(1,n[1])))];
const vector=p=>[Math.sin(p[0])*Math.cos(p[1]),Math.sin(p[1]),Math.cos(p[0])*Math.cos(p[1])];
let faces=e.base.faces.map(f=>{
 const c=f.centroid,r=Math.abs(c[1])===90?[0,-c[1],-30]:[-c[0],-c[1],30];
 const cols=[[1,0,0],[0,1,0],[0,0,1]].map(n=>{let p=rotate(...rotate(...geo(n),e.rotation),r);return [Math.cos(p[0])*Math.cos(p[1]),Math.sin(p[0])*Math.cos(p[1]),Math.sin(p[1])];});
 const matrix=[0,1,2].flatMap(row=>cols.map(col=>col[row]));
 const raw=(x,y)=>{let p=e.base.projectRaw(f.id,x,-y),n=e.normalization;return [(p[0]-n.cx)*n.scale,-(p[1]-n.cy)*n.scale];};
 let a=raw(0,0),b=raw(1,0),c1=raw(0,1);return {matrix,affine:[b[0]-a[0],c1[0]-a[0],a[0],b[1]-a[1],c1[1]-a[1],a[1]]};
});
fs.writeFileSync(new URL('faces.json',out),JSON.stringify({faces}));
const vertices=[[0,90],[-180,-Math.asin(1/3)*180/Math.PI],[-60,-Math.asin(1/3)*180/Math.PI],[60,-Math.asin(1/3)*180/Math.PI]].map(p=>rotate(p[0]*Math.PI/180,p[1]*Math.PI/180,e.rotation,true));
const data=new Float32Array(W*H*4);let fallback=0,failed=0;
for(let y=0;y<H;y++){
 for(let x=0;x<W;x++){
  let px=x/(W-1)*8-4,py=y/(H-1)*2*rt-rt,p=e.inverse(px,py);
  if(!p){
   // Exact cone vertices have a direction even though their conformal derivative diverges.
   p=vertices.find(v=>{let q=e.forward(...v);return Math.hypot(q[0]-px,q[1]-py)<1e-6;});
  }
  if(!p){for(let eps of [1e-9,1e-8,1e-7]){p=e.inverse(Math.max(-4+eps,Math.min(4-eps,px)),Math.max(-rt+eps,Math.min(rt-eps,py)));if(p){fallback++;break;}}}
  if(!p){failed++;continue;}
  data.set([...vector(p),1],(y*W+x)*4);
 }
 if(y%100===0)console.log('rows',y);
}
if(failed)throw Error('inverse failures '+failed);
// Enforce the exact boundary identifications used by the repeated cover.
for(let y of [0,H-1])for(let x=0;x<W-1;x++){
 const phase=y===0?(W-1)/4:3*(W-1)/4,j=((phase-x)%(W-1)+W-1)%(W-1);if(j<x)continue;
 let a=(y*W+x)*4,b=(y*W+j)*4,n=[0,1,2].map(k=>(data[a+k]+data[b+k])*.5),l=Math.hypot(...n);
 for(let k=0;k<3;k++)data[a+k]=data[b+k]=n[k]/l;
}
for(let y=0;y<H;y++)data.set(data.subarray(y*W*4,y*W*4+4),(y*W+W-1)*4);
fs.writeFileSync(new URL('inverse.bytes',out),Buffer.from(data.buffer));
const refs=[];for(let i=0;i<10000;i++){let lon=(i*.6180339887498949%1)*2*Math.PI-Math.PI,lat=Math.asin(2*(i+.5)/10000-1),p=e.forward(lon,lat);refs.push([lon,lat,(p[0]+4)/8,(p[1]+rt)/(2*rt)]);}
fs.writeFileSync(new URL('reference-points.json',import.meta.url),JSON.stringify(refs));
fs.writeFileSync(new URL('table-info.json',out),JSON.stringify({W,H,fallback,failed,source:'Mapshaper Lee Markley, MPL-2.0 / d3 ISC'},null,2));console.log({W,H,fallback,failed});
