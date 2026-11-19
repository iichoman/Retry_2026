using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.AI.Navigation;

// Cosmetic geometry only. Uses its own coordinate hash, never the dungeon's random stream.
public static class DungeonStoneMesher
{
    public const int ChunkSize = 12;
    public const float StoneHeight = .68f, StoneWidth = 1.05f, Recess = .19f;
    static readonly Vector3Int[] Directions={Vector3Int.right,Vector3Int.left,Vector3Int.forward,Vector3Int.back};
    static float Random(int seed,int x,int y,int salt)=> (DungeonSetDressing.Hash(seed^salt,x,y)&0xffff)/65535f;

    sealed class Drawing
    {
        public readonly List<Vector3> vertices=new();
        readonly List<Vector3> normals=new();
        readonly List<Vector2> uv=new();
        readonly List<int>[] indices;
        public Drawing(int materials){indices=new List<int>[materials];for(int i=0;i<materials;i++)indices[i]=new();}
        public void Vertex(Vector3 point,Vector2 texture){vertices.Add(point);uv.Add(texture);normals.Add(Vector3.zero);}
        public void IndexedTriangle(int a,int b,int c,int mat,Vector3 outward){
            var n=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]);
            if(Vector3.Dot(n,outward)<0){(b,c)=(c,b);n=-n;}
            n.Normalize();normals[a]+=n;normals[b]+=n;normals[c]+=n;indices[mat].Add(a);indices[mat].Add(b);indices[mat].Add(c);
        }
        public void FinishPatch(int start){for(int i=start;i<normals.Count;i++)normals[i]=normals[i].normalized;}
        public void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector2 ua,Vector2 ub,Vector2 uc,int mat,Vector3 outward)
        {
            if(Vector3.Dot(Vector3.Cross(b-a,c-a),outward)<0){(b,c)=(c,b);(ub,uc)=(uc,ub);}
            var normal=Vector3.Cross(b-a,c-a).normalized;int start=vertices.Count;
            vertices.Add(a);vertices.Add(b);vertices.Add(c);normals.Add(normal);normals.Add(normal);normals.Add(normal);
            uv.Add(ua);uv.Add(ub);uv.Add(uc);indices[mat].Add(start);indices[mat].Add(start+1);indices[mat].Add(start+2);
        }
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector2 ua,Vector2 ub,Vector2 uc,Vector2 ud,int mat,Vector3 outward)
        {Triangle(a,b,c,ua,ub,uc,mat,outward);Triangle(a,c,d,ua,uc,ud,mat,outward);}
        public Mesh Build(string name)
        {
            if(vertices.Count==0)return null;
            var mesh=new Mesh{name=name,indexFormat=vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16,subMeshCount=indices.Length};
            mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);
            for(int i=0;i<indices.Length;i++)mesh.SetTriangles(indices[i],i);
            mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;
        }
    }

    public static void Create(Transform parent,List<Vector3Int> tiles,HashSet<Vector3Int> walls,HashSet<Vector3Int> floors,
        Vector3Int offset,int seed,int height,Material[] materials,float textureSize,HashSet<Vector3Int> emitted,
        DungeonMasonryResources resources,Func<Vector3Int,int,int,int> materialIndex)
    {
        var chunks=new SortedDictionary<Vector2Int,List<Vector3Int>>(Comparer<Vector2Int>.Create((a,b)=>a.x!=b.x?a.x.CompareTo(b.x):a.y.CompareTo(b.y)));
        foreach(var tile in tiles){if(tile.y!=0)continue;var key=new Vector2Int(Mathf.FloorToInt(tile.x/(float)ChunkSize),Mathf.FloorToInt(tile.z/(float)ChunkSize));
            if(!chunks.TryGetValue(key,out var list)){list=new();chunks.Add(key,list);}list.Add(tile);}
        foreach(var chunk in chunks){
            chunk.Value.Sort((a,b)=>a.x!=b.x?a.x.CompareTo(b.x):a.z.CompareTo(b.z));
            var origin=new Vector3(chunk.Key.x*ChunkSize+offset.x,0,chunk.Key.y*ChunkSize+offset.z);
            var near=new Drawing(materials.Length);var far=new Drawing(materials.Length);
            // Runs are sorted independently of HashSet iteration, including negative map coordinates.
            for(int dir=0;dir<4;dir++){
                var n=Directions[dir];bool xAxis=n.z!=0;
                var lines=new SortedDictionary<int,SortedSet<int>>();
                foreach(var tile in chunk.Value){
                    if(walls.Contains(tile+n)||!emitted.Add(new Vector3Int(tile.x,dir,tile.z)))continue;
                    int plane=xAxis?tile.z:tile.x,along=xAxis?tile.x:tile.z;
                    if(!lines.TryGetValue(plane,out var values)){values=new();lines.Add(plane,values);}values.Add(along);
                }
                foreach(var line in lines){int start=int.MinValue,last=int.MinValue;bool previousExposed=false;
                    foreach(int coordinate in line.Value){
                        var at=xAxis?new Vector3Int(coordinate,0,line.Key):new Vector3Int(line.Key,0,coordinate);bool exposed=floors.Contains(at+n);
                        if(start!=int.MinValue&&(coordinate!=last+1||exposed!=previousExposed)){Run(start,last+1,line.Key,dir);start=int.MinValue;}
                        if(start==int.MinValue)start=coordinate;last=coordinate;previousExposed=exposed;
                    }
                    if(start!=int.MinValue)Run(start,last+1,line.Key,dir);
                }
                void Run(int start,int end,int plane,int direction){
                    var tile=xAxis?new Vector3Int(start,0,plane):new Vector3Int(plane,0,start);
                    var normal=(Vector3)n;var tangent=xAxis?Vector3.right:Vector3.forward;
                    var basePoint=(Vector3)(tile+offset)+new Vector3(.5f,0,.5f)+normal*.5f-tangent*.5f-origin;
                    bool exposed=floors.Contains(tile+n);
                    var step=xAxis?Vector3Int.right:Vector3Int.forward;
                    var lastTile=tile+step*(end-start-1);
                    bool Continuous(Vector3Int p)=>walls.Contains(p)&&!walls.Contains(p+n)&&floors.Contains(p+n);
                    bool closeStart=exposed&&!Continuous(tile-step),closeEnd=exposed&&!Continuous(lastTile+step);
                    // At an inside corner the two recessed faces must overlap inside the solid wall.
                    // Extending only into an occupied neighbour preserves doorway/passable dimensions.
                    float joinLeft=exposed&&walls.Contains(tile-step)&&walls.Contains(tile-step+n)?Recess:0;
                    float joinRight=exposed&&walls.Contains(lastTile+step)&&walls.Contains(lastTile+step+n)?Recess:0;
                    // Two-metre UV period doubles the old brick scale and stays continuous across chunks.
                    for(int y=0;y<=height;y++){
                        int mat=materialIndex(new Vector3Int(tile.x,y,tile.z),direction==2?4:direction==3?5:direction,materials.Length);
                        var a=basePoint+Vector3.up*y;var b=a+tangent*(end-start);var c=b+Vector3.up;var d=a+Vector3.up;
                        var ua=new Vector2(start/textureSize,y/textureSize);var ub=new Vector2(end/textureSize,y/textureSize);var uc=ub+Vector2.up/textureSize;var ud=ua+Vector2.up/textureSize;
                        // Also overlap the flat LOD: adjacent chunks can select different LOD levels.
                        far.Quad(a-tangent*joinLeft,b+tangent*joinRight,c+tangent*joinRight,d-tangent*joinLeft,ua,ub,uc,ud,mat,normal);
                        var inset=exposed?normal*Recess:Vector3.zero;
                        near.Quad(a-inset-tangent*joinLeft,b-inset+tangent*joinRight,c-inset+tangent*joinRight,d-inset-tangent*joinLeft,ua,ub,uc,ud,mat,normal);
                        // Seal every real recessed end back to the original grid edge. Generated room
                        // corners often touch diagonally with NO wall in the cell behind the junction;
                        // extending faces into occupied neighbours alone cannot close those holes.
                        // These caps lie entirely within the original wall footprint, away from props.
                        var seamUV=Vector2.right*(Recess/textureSize);
                        if(closeStart)near.Quad(a-inset,a,d,d-inset,ua-seamUV,ua,ud,ud-seamUV,mat,tangent);
                        if(closeEnd)near.Quad(b,b-inset,c-inset,c,ub,ub+seamUV,uc+seamUV,uc,mat,-tangent);
                    }
                    if(!exposed)return;
                    // Find actual wall ends, not chunk/room boundaries. A stone belongs to one chunk,
                    // but may cross its boundary, so chunk splits never cut a straight seam into rocks.
                    int wallStart=start,wallEnd=end;
                    while(Continuous(tile+step*(wallStart-start-1)))wallStart--;
                    while(Continuous(tile+step*(wallEnd-start)))wallEnd++;
                    int stoneSeed=unchecked(seed^plane*19349663^direction*73856093);
                    var polygon=new List<Vector2>(16);var scratch=new List<Vector2>(16);
                    int rowMin=Mathf.FloorToInt(1/StoneHeight)-2,rowMax=Mathf.CeilToInt((height+1)/StoneHeight)+1;
                    int colMin=Mathf.FloorToInt(start/StoneWidth)-2,colMax=Mathf.CeilToInt(end/StoneWidth)+1;
                    // Neighbours are reused by adjacent cells; hash each site once per run.
                    var sites=new Vector2[colMax-colMin+7,rowMax-rowMin+7];
                    for(int y=rowMin-3;y<=rowMax+3;y++)for(int x=colMin-3;x<=colMax+3;x++)sites[x-colMin+3,y-rowMin+3]=Site(stoneSeed,x,y);
                    for(int row=rowMin;row<=rowMax;row++){
                        for(int column=colMin;column<=colMax;column++){
                            int sx=column-colMin+3,sy=row-rowMin+3;var site=sites[sx,sy];
                            float owner=Mathf.Clamp(site.x,wallStart+.0001f,wallEnd-.0001f);
                            if(owner<start||owner>=end)continue;
                            polygon.Clear();
                            polygon.Add(site+new Vector2(-2,-2));polygon.Add(site+new Vector2(2,-2));
                            polygon.Add(site+new Vector2(2,2));polygon.Add(site+new Vector2(-2,2));
                            // Jittered Voronoi cells: large and small angular rubble, without brick rows.
                            for(int dy=-3;dy<=3;dy++)for(int dx=-3;dx<=3;dx++){
                                if(dx==0&&dy==0)continue;
                                var other=sites[sx+dx,sy+dy];var delta=other-site;
                                Clip(ref polygon,ref scratch,delta,Vector2.Dot(delta,(site+other)*.5f));
                            }
                            Clip(ref polygon,ref scratch,Vector2.left,-wallStart);
                            Clip(ref polygon,ref scratch,Vector2.right,wallEnd);
                            Clip(ref polygon,ref scratch,Vector2.down,-1);
                            Clip(ref polygon,ref scratch,Vector2.up,height+1);
                            if(polygon.Count<3)continue;
                            int mat=materialIndex(new Vector3Int(tile.x,Mathf.Clamp(Mathf.FloorToInt(site.y),1,height),tile.z),direction==2?4:direction==3?5:direction,materials.Length);
                            Stone(near,basePoint-tangent*start,tangent,normal,polygon,column,row,stoneSeed,mat);
                        }
                    }
                }
            }
            // Horizontal caps close wall tops and doorway ends without hidden internal cube faces.
            foreach(var tile in chunk.Value){
                if(!emitted.Add(new Vector3Int(tile.x,4,tile.z)))continue;
                var a=(Vector3)(tile+offset)-origin+Vector3.up*(height+1);
                var ua=new Vector2(tile.x/textureSize,tile.z/textureSize);var ub=ua+Vector2.right/textureSize;var uc=ub+Vector2.up/textureSize;var ud=ua+Vector2.up/textureSize;
                int mat=materialIndex(new Vector3Int(tile.x,height,tile.z),2,materials.Length);
                near.Quad(a,a+Vector3.right,a+Vector3.right+Vector3.forward,a+Vector3.forward,ua,ub,uc,ud,mat,Vector3.up);
                far.Quad(a,a+Vector3.right,a+Vector3.right+Vector3.forward,a+Vector3.forward,ua,ub,uc,ud,mat,Vector3.up);
            }
            var detail=near.Build("Rough stone wall");var simple=far.Build("Stone wall distant surface");if(detail==null)continue;
            resources.Own(detail);resources.Own(simple);
            var root=new GameObject($"StoneChunk_{chunk.Key.x}_{chunk.Key.y}");root.transform.SetParent(parent,false);root.transform.localPosition=origin;root.layer=parent.gameObject.layer;
            var high=Renderer(root.transform,"Stone relief",detail,materials);var low=Renderer(root.transform,"Distant masonry",simple,materials);
            // NavMesh sees the flat LOD source. Decorative relief cannot change passage widths.
            high.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild=true;
            var lod=root.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.18f,new UnityEngine.Renderer[]{high}),new LOD(.008f,new UnityEngine.Renderer[]{low})});lod.RecalculateBounds();
            // Runtime NavMesh baking reads the small flat mesh; only relief discards its CPU copy.
            if(Application.isPlaying)detail.UploadMeshData(true);
        }
    }
    static MeshRenderer Renderer(Transform parent,string name,Mesh mesh,Material[] materials){var go=new GameObject(name);go.transform.SetParent(parent,false);go.layer=parent.gameObject.layer;go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterials=materials;return r;}

    static Vector2 Site(int seed,int column,int row)=>new Vector2(
        (column+(row&1)*.5f+(Random(seed,column,row,113)-.5f)*.84f)*StoneWidth,
        (row+(Random(seed,column,row,127)-.5f)*.84f)*StoneHeight);

    // Convex half-plane clipping; buffers are reused for all stones in a wall run.
    static void Clip(ref List<Vector2> polygon,ref List<Vector2> scratch,Vector2 normal,float distance)
    {
        scratch.Clear();if(polygon.Count==0)return;
        var previous=polygon[polygon.Count-1];float previousDistance=Vector2.Dot(previous,normal)-distance;
        foreach(var point in polygon){float pointDistance=Vector2.Dot(point,normal)-distance;
            if((pointDistance<=0)!=(previousDistance<=0))scratch.Add(Vector2.LerpUnclamped(previous,point,previousDistance/(previousDistance-pointDistance)));
            if(pointDistance<=0)scratch.Add(point);
            previous=point;previousDistance=pointDistance;
        }
        (polygon,scratch)=(scratch,polygon);
    }

    static void Stone(Drawing mesh,Vector3 origin,Vector3 tangent,Vector3 normal,List<Vector2> polygon,int column,int row,int seed,int mat)
    {
        // Discard coincident clipping corners and sub-pixel slivers before triangulation.
        for(int i=polygon.Count-1;i>=0;i--)if((polygon[i]-polygon[(i+1)%polygon.Count]).sqrMagnitude<.000001f)polygon.RemoveAt(i);
        if(polygon.Count<3)return;
        var center=Vector2.zero;float left=float.MaxValue,right=float.MinValue,bottom=float.MaxValue,top=float.MinValue,area=0;
        for(int i=0;i<polygon.Count;i++){var p=polygon[i];var q=polygon[(i+1)%polygon.Count];center+=p;
            left=Mathf.Min(left,p.x);right=Mathf.Max(right,p.x);bottom=Mathf.Min(bottom,p.y);top=Mathf.Max(top,p.y);area+=p.x*q.y-q.x*p.y;}
        if(Mathf.Abs(area)<.008f)return;
        center/=polygon.Count;float w=right-left,h=top-bottom;
        float bevel=.12f+Random(seed,column,row,91)*.13f;
        float depth=.018f+Random(seed,column,row,71)*.065f;
        float tilt=(Random(seed,column,row,83)-.5f)*.04f;
        Vector3 Point(Vector2 p,float recession)=>origin+tangent*p.x+Vector3.up*p.y-normal*recession;
        // Sample the interior of one authored stone: no second tiny brick grid over the real geometry.
        int textureRow=(int)(Random(seed,column,row,17)*4.99f);float u=(textureRow&1)==0?.015f:.265f,v=textureRow*.2f+.018f;
        Vector2 UV(Vector2 p)=>new Vector2(u+Mathf.Clamp01((p.x-left)/w)*.46f,v+Mathf.Clamp01((p.y-bottom)/h)*.164f);
        // Separate bevel normals preserve angular edges; varying rim depths break the flat slab look.
        int count=polygon.Count,start=mesh.vertices.Count;mesh.Vertex(Point(center,depth*.4f),UV(center));
        for(int ring=0;ring<2;ring++)for(int i=0;i<count;i++){
            var p=Vector2.Lerp(polygon[i],center,bevel+.035f);
            float chip=Random(seed,column+i*13,row,149)*.025f;
            mesh.Vertex(Point(p,Mathf.Max(.004f,depth+tilt*(p.x-center.x)/w+chip)),UV(p));
        }
        for(int i=0;i<count;i++){var p=Vector2.Lerp(polygon[i],center,.035f);mesh.Vertex(Point(p,Recess),UV(p));}
        for(int i=0;i<count;i++){int j=(i+1)%count;
            mesh.IndexedTriangle(start,start+1+i,start+1+j,mat,normal);
            mesh.IndexedTriangle(start+1+count*2+i,start+1+count*2+j,start+1+count+j,mat,normal);
            mesh.IndexedTriangle(start+1+count*2+i,start+1+count+j,start+1+count+i,mat,normal);
        }mesh.FinishPatch(start);
    }
}
