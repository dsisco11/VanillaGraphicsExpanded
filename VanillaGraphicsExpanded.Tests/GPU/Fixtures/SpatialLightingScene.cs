using System.Numerics;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Authors fixed, neighboring voxel rooms and derives engine raster inputs from a movable camera.</summary>
internal sealed class SpatialLightingScene
{
    public Vector3 Position { get; set; } = new(0,36,5);
    public float Yaw { get; set; }
    public float Bob { get; set; }
    public float EyeOffsetX { get; set; }
    public bool AlternateDarkRooms { get; set; }
    public int BlockLight { get; set; } = 32;
    public bool DividedRoom { get; set; }
    public bool DoorOpen { get; set; } = true;
    public float Reflectance { get; set; } = 1;
    public float Emission { get; set; }
    /// <summary>Optional per-channel reflectance of the cached source surfaces; scalar scenes retain their original behavior.</summary>
    public Vector3? SourceAlbedo { get; set; }
    public System.Collections.Concurrent.ConcurrentDictionary<ChunkKey,byte> Unloaded { get; } = new();
    public Vector3[] VisiblePoints { get; private set; } = [];
    public Vector3[] VisibleNormals { get; private set; } = [];
    public LumOnCameraState Camera => new(Position.X,Position.Y,Position.Z,Position.X+EyeOffsetX,Position.Y+Bob,Position.Z,0);
    public int RoomOrigin => ((int)MathF.Floor((Position.X+4)/8))*8-4;

    #region Source geometry
    /// <summary>Defines two-voxel separators between repeating six-voxel-wide closed interiors.</summary>
    public bool Solid(int x,int y,int z) => ((x+4)%8+8)%8 is 0 or 7 || y<=32 || y>=39 || z<=0 || z>=7 || (DividedRoom && z==5 && (!DoorOpen || ((x+4)%8+8)%8 is <2 or >5 || y<34 || y>37));

    /// <summary>Supplies independent direct-light fields; a dark room remains adjacent to a bright one.</summary>
    public int Light(int x,int y,int z) => DividedRoom ? (z>=6 ? 32 : 0) : AlternateDarkRooms && (((int)Math.Floor((x+4)/8.0))&1)!=0 ? 0 : BlockLight;

    /// <summary>Uses actual chunk identities for source unload/reload without changing geometry elsewhere.</summary>
    public bool Loaded(ChunkKey key) => !Unloaded.ContainsKey(key);

    /// <summary>Enumerates visible terrain patch identities needed to illuminate the current room, including signed chunk crossings.</summary>
    public (VectorInt3 Chunk,uint Patch)[] Feedback()
    {
        var result=new HashSet<(VectorInt3,uint)>();
        foreach (var face in Enumerable.Range(0,6).Select(axis => (Axis:axis, Plane:axis%2==0?0:7))
            .Concat(DividedRoom ? new[]{(Axis:4,Plane:5),(Axis:5,Plane:5)} : []))
        for(int u=0;u<8;u++) for(int v=0;v<8;v++)
        {
            int axis=face.Axis, plane=face.Plane;
            int x=RoomOrigin+(axis<2?plane:u), y=32+(axis<2?v:axis<4?plane:v), z=axis<2?u:axis<4?v:plane;
            int px=x&31,py=y&31,pz=z&31;
            int p=axis<2?px:axis<4?py:pz, uc=axis<2?pz:px,vc=axis<2?py:axis<4?pz:py;
            result.Add((new(x>>5,y>>5,z>>5),1u+6u*(uint)(p*64+(vc/4)*8+uc/4)+(uint)axis));
        }
        if (DividedRoom)
        {
            // Rays starting inside the opening see these perpendicular faces. The broad
            // front/back door patches do not provide their independently addressed lighting.
            for (int y = 34; y <= 37; y++)
            {
                AddReveal(RoomOrigin + 1, y, 5, 0);
                AddReveal(RoomOrigin + 6, y, 5, 1);
            }
            for (int x = RoomOrigin + 2; x <= RoomOrigin + 5; x++)
            {
                AddReveal(x, 33, 5, 2);
                AddReveal(x, 38, 5, 3);
            }
        }
        return result.ToArray();

        /// <summary>Adds the independently addressed cache patch for one physical doorway reveal face.</summary>
        void AddReveal(int x, int y, int z, int axis)
        {
            int px = x & 31, py = y & 31, pz = z & 31;
            int plane = axis < 2 ? px : py, u = axis < 2 ? pz : px, v = axis < 2 ? py : pz;
            result.Add((new(x >> 5, y >> 5, z >> 5),
                1u + 6u * (uint)((plane << 6) + ((v >> 2) << 3) + (u >> 2)) + (uint)axis));
        }
    }
    #endregion

    #region Camera and raster
    /// <summary>Returns the engine's player-relative view transform; bob translation is part of the view, not the world origin.</summary>
    public float[] View()
    {
        var m=Matrix4x4.CreateTranslation(-EyeOffsetX,-Bob,0)*Matrix4x4.CreateRotationY(-Yaw);
        return [m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
    }

    /// <summary>Intersects each camera ray with the authored room boundary and encodes its exact depth and world normal.</summary>
    public (float[] Depth,float[] Normal) Raster(int edge,float[] projection)
    {
        var depths=new float[edge*edge]; var normals=new float[edge*edge*4];
        VisiblePoints=new Vector3[edge*edge]; VisibleNormals=new Vector3[edge*edge];
        var eye=Position+new Vector3(EyeOffsetX,Bob,0);
        var rotation=Matrix4x4.CreateRotationY(Yaw);
        Vector3 min=new(RoomOrigin+1,33,1),max=new(RoomOrigin+7,39,7);
        // The camera is inside a closed empty box; its first exit is the occupied voxel face.
        for(int y=0;y<edge;y++) for(int x=0;x<edge;x++)
        {
            int i=y*edge+x;
            var viewRay=new Vector3(((x+.5f)/edge*2-1)/projection[0],((y+.5f)/edge*2-1)/projection[5],-1);
            var ray=Vector3.TransformNormal(viewRay,rotation);
            float distance=float.PositiveInfinity; Vector3 normal=default;
            for(int axis=0;axis<3;axis++)
            {
                float d=ray[axis]; if(Math.Abs(d)<.00001f) continue;
                float t=((d>0?max[axis]:min[axis])-eye[axis])/d;
                if(t>=0 && t<distance) { distance=t; normal=Vector3.Zero; normal[axis]=d>0?-1:1; }
            }
            Assert.True(float.IsFinite(distance));
            VisiblePoints[i]=eye+ray*distance; VisibleNormals[i]=normal;
            float z=-distance;
            depths[i]=(projection[10]*z+projection[14])/(projection[11]*z+projection[15])*.5f+.5f;
            for(int c=0;c<3;c++) normals[i*4+c]=normal[c]*.5f+.5f;
        }
        return(depths,normals);
    }
    #endregion
}
