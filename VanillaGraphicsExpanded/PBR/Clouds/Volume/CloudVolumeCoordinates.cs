using System;
using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Maps current camera/native placement into a retained domain's bounded material coordinates.</summary>
internal static class CloudVolumeCoordinates
{
    #region Public API
    /// <summary>Applies wind/placement once while allowing the old domain to remain visible during replacement.</summary>
    internal static Vector3 Origin(CloudVolumeDomain domain,CloudMapInput input)
    {
        double anchorX=50.0*((long)input.Placement.CenterX-input.Placement.WindTileX-input.Storage.Width/2.0);
        double anchorZ=50.0*((long)input.Placement.CenterZ-input.Placement.WindTileZ-input.Storage.Width/2.0);
        var origin=new Vector3((float)(anchorX-domain.AnchorX+25*input.Storage.Width-input.Placement.Offset.X),
            -input.Placement.Offset.Y,(float)(anchorZ-domain.AnchorZ+25*input.Storage.Width-input.Placement.Offset.Z));
        if(!float.IsFinite(origin.X)||!float.IsFinite(origin.Y)||!float.IsFinite(origin.Z))throw new ArgumentOutOfRangeException(nameof(input));
        return origin;
    }
    /// <summary>Converts rotation-only camera planes into the same retained-domain coordinates used by view rays.</summary>
    internal static void Frustum(VgeFrameUniformBuffer camera,Vector3 origin,Span<Vector4> planes)
    {
        if(planes.Length!=6)throw new ArgumentException("Six scheduling planes required.",nameof(planes));
        Span<float> view=stackalloc float[16],clip=stackalloc float[16];
        MemoryMarshal.Cast<byte,float>(camera.Bytes.Slice(64,64)).CopyTo(view);view[12]=view[13]=view[14]=0;
        MatrixHelper.Multiply(camera.Projection,view,clip);
        for(int axis=0;axis<3;axis++)for(int side=0;side<2;side++)
        {
            float sign=side==0?1:-1;
            Vector4 p=new(clip[3]+sign*clip[axis],clip[7]+sign*clip[4+axis],clip[11]+sign*clip[8+axis],clip[15]+sign*clip[12+axis]);
            float length=new Vector3(p.X,p.Y,p.Z).Length();
            if(!float.IsFinite(length)||length<=0)throw new ArgumentException("Invalid cloud frustum.");
            p/=length;p.W-=Vector3.Dot(new(p.X,p.Y,p.Z),origin);planes[axis*2+side]=p;
        }
    }
    #endregion
}
