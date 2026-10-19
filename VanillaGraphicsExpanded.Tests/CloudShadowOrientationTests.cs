using System.Numerics;
using VanillaGraphicsExpanded.PBR.Clouds;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks transported frame geometry without imposing image-quality thresholds.</summary>
public sealed class CloudShadowOrientationTests
{
    #region Public API
    /// <summary>Small rotations preserve the tangent across both poles; antipodal jumps remain well defined.</summary>
    [Fact]
    public void TransportFollowsShortestRotationAcrossPoles()
    {
        var orientation=new CloudShadowOrientation();
        Vector3 previous=default;
        // This great circle crosses both poles and the former reference-axis seam.
        // Independent Rodrigues rotation supplies the finite geometric oracle.
        Vector3 axis=Vector3.Normalize(new(1,0,2));
        const float step=MathF.PI/180;
        for(int i=0;i<=360;i++)
        {
            Vector3 light=Vector3.Transform(Vector3.UnitY,Quaternion.CreateFromAxisAngle(axis,i*step));
            Vector3 u=orientation.Capture(light);
            Assert.InRange(Math.Abs(u.Length()-1),0,2e-6f);
            Assert.InRange(Math.Abs(Vector3.Dot(u,light)),0,2e-6f);
            if(i>0)
            {
                Vector3 expected=Vector3.Transform(previous,Quaternion.CreateFromAxisAngle(axis,step));
                Assert.InRange(Vector3.Distance(expected,u),0,3e-6f);
            }
            Assert.InRange(Vector3.Distance(u,orientation.Capture(light)),0,2e-6f);
            previous=u;
        }
        Vector3 antipodal=orientation.Capture(-Vector3.UnitY);
        Assert.InRange(Vector3.Distance(previous,antipodal),0,3e-6f);
        Assert.InRange(Math.Abs(Vector3.Dot(antipodal,-Vector3.UnitY)),0,2e-6f);
    }
    #endregion
}
