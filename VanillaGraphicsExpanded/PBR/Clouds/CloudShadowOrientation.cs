using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Transports one light-space tangent without retaining attenuation or source validity.</summary>
internal struct CloudShadowOrientation
{
    private Vector3 direction,tangent;
    private bool initialized;

    #region Public API
    /// <summary>Applies the shortest rotation between consecutive light directions, including vertical poles.</summary>
    internal Vector3 Capture(Vector3 light)
    {
        if(!initialized)
        {
            // The initial gauge is arbitrary. Select a well-conditioned axis only once,
            // so subsequent continuous motion never crosses a reference-axis boundary.
            Vector3 axis=Math.Abs(light.X)<.9f?Vector3.UnitX:Vector3.UnitZ;
            tangent=Vector3.Normalize(axis-light*Vector3.Dot(axis,light));
            initialized=true;
        }
        else
        {
            float cosine=Vector3.Dot(direction,light);
            if(cosine>-.9999f)
                tangent-= (direction+light)*(Vector3.Dot(tangent,light)/(1+cosine));
            // A discontinuous antipodal jump has no unique shortest rotation. Keep
            // the prior tangent's projection, equivalent to a half turn about it at
            // the exact antipode. Ordinary small motion never enters this case.
            tangent-=light*Vector3.Dot(tangent,light);
            tangent=Vector3.Normalize(tangent);
        }
        direction=light;
        return tangent;
    }
    #endregion
}
