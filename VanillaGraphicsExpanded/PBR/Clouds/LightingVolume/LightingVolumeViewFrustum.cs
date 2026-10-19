using System;
using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Converts the shared rotation-only view into normalized native-local scheduling planes.</summary>
internal static class LightingVolumeViewFrustum
{
    #region Public API
    /// <summary>Includes native placement once and leaves density support independent of visibility priority.</summary>
    internal static void Capture(VgeFrameUniformBuffer camera,CloudMapInput input,Span<Vector4> planes)
    {
        if(planes.Length!=6) throw new ArgumentException("Six frustum planes required.",nameof(planes));
        Span<float> view=stackalloc float[16],clip=stackalloc float[16];
        MemoryMarshal.Cast<byte,float>(camera.Bytes.Slice(64,64)).CopyTo(view);view[12]=view[13]=view[14]=0;
        MatrixHelper.Multiply(camera.Projection,view,clip);
        Vector3 origin=-input.Placement.Offset+new Vector3(25*input.Storage.Width,0,25*input.Storage.Width);
        for(int axis=0;axis<3;axis++) for(int side=0;side<2;side++)
        {
            float sign=side==0?1:-1;
            Vector4 p=new(clip[3]+sign*clip[axis],clip[7]+sign*clip[4+axis],
                clip[11]+sign*clip[8+axis],clip[15]+sign*clip[12+axis]);
            float length=new Vector3(p.X,p.Y,p.Z).Length();
            if(!float.IsFinite(length)||length<=0) throw new ArgumentException("Invalid scheduling frustum.");
            p/=length;p.W-=Vector3.Dot(new(p.X,p.Y,p.Z),origin);planes[axis*2+side]=p;
        }
    }
    #endregion
}
