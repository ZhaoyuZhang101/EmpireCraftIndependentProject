"""Independent Python reference retained from V2. Not Unity/C# execution."""
import math,re
import numpy as np
from scipy.spatial import cKDTree
def norm(p): return p/np.linalg.norm(p,axis=-1,keepdims=True)

def grid(res):
    normals=np.array([[1,0,0],[-1,0,0],[0,1,0],[0,-1,0],[0,0,1],[0,0,-1]])
    lookup={};corners=[];stars=[];points=[];tilecorners=[]
    for n in normals:
        u=np.array([0,1,0]) if abs(n[0])==1 else np.array([1,0,0]);v=np.cross(n,u)
        for y in range(res):
            for x in range(res):
                ids=[]
                for dx,dy in [(0,0),(1,0),(1,1),(0,1)]:
                    key=tuple(n*res+u*(2*(x+dx)-res)+v*(2*(y+dy)-res))
                    if key not in lookup:
                        lookup[key]=len(corners);q=np.array(key)/res;xx,yy,zz=q*q
                        p=q*np.sqrt([1-yy/2-zz/2+yy*zz/3,1-zz/2-xx/2+zz*xx/3,1-xx/2-yy/2+xx*yy/3])
                        corners.append(norm(p));stars.append([])
                    cid=lookup[key];ids.append(cid);stars[cid].append(len(points))
                points.append(norm(np.sum([corners[i] for i in ids],axis=0)));tilecorners.append(ids)
    points=np.array(points);tri=[]
    for n,ids in zip(corners,stars):
        ref=np.array([1,0,0]) if abs(n[1])>.9 else np.array([0,1,0]);east=norm(np.cross(ref,n));north=np.cross(n,east)
        ids=sorted(ids,key=lambda i:math.atan2(points[i]@north,points[i]@east))
        for j in range(1,len(ids)-1):
            a,b,c=ids[0],ids[j],ids[j+1]
            if np.cross(points[b]-points[a],points[c]-points[a])@n<0:b,c=c,b
            tri.append([a,b,c])
    tri=np.array(tri);return points,tri

class Sampler:
    def __init__(self,p,t):
        self.p,self.t=p,t;self.tree=cKDTree(p)
        xyz=p[t];self.rows=np.linalg.inv(np.transpose(xyz,(0,2,1)))
        self.stars=[[] for _ in p];self.adj=[set() for _ in p]
        for i,(a,b,c) in enumerate(t):
            for x in [a,b,c]:self.stars[x].append(i);self.adj[x].update([a,b,c]);self.adj[x].discard(x)
        self.inc=np.full((len(p),max(map(len,self.stars))),-1,dtype=int)
        for i,star in enumerate(self.stars):self.inc[i,:len(star)]=star
    def locate(self,q):
        q=np.atleast_2d(q);_,h=self.tree.query(q)
        candidates=self.inc[h];valid=candidates>=0
        raw=np.einsum('nkij,nj->nki',self.rows[np.maximum(candidates,0)],q)
        valid&=(raw.min(axis=2)>=-1e-5)&(raw.sum(axis=2)>0)
        missing=~valid.any(axis=1)
        if missing.any():
            for i in np.flatnonzero(missing):
                allraw=self.rows@q[i];inside=(allraw.min(axis=1)>=-1e-5)&(allraw.sum(axis=1)>0)
                candidate=np.flatnonzero(inside)[0];candidates[i,0]=candidate;raw[i,0]=allraw[candidate];valid[i,0]=True
        idx=valid.argmax(axis=1);chosen=candidates[np.arange(len(q)),idx];w=np.maximum(raw[np.arange(len(q)),idx],0);w/=w.sum(axis=1,keepdims=True)
        return self.t[chosen],w
    def greedy(self,q,h):
        for _ in range(128):
            score=self.p[h]@q;next_=h
            for n in sorted(self.adj[h]):
                s=self.p[n]@q
                if s>score+1e-8:score=s;next_=n
            if next_==h:break
            h=next_
        for indices in [self.stars[h],sum((self.stars[n] for n in self.adj[h]),[])]:
            for tri in indices:
                raw=self.rows[tri]@q
                if raw.min()>=-1e-5 and raw.sum()>0:
                    w=np.maximum(raw,0);w/=w.sum();return self.t[tri],w
        raise AssertionError('one-ring location failed')

def lexical_check(text):
    # A delimiter scanner only, not a parser or semantic compiler.
    text=re.sub(r'@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|//[^\n]*|/\*.*?\*/',lambda m:' '*len(m.group()),text,flags=re.S)
    stack=[];pairs={')':'(',']':'[','}':'{'}
    for c in text:
        if c in '([{':stack.append(c)
        elif c in ')]}':assert stack and stack.pop()==pairs[c], 'unbalanced source delimiters'
    assert not stack

