using System;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Retains componentwise physical source envelopes with their diffuse representation.</summary>
internal sealed record CloudVolumeLightingBounds(Vector3 Solar,Vector3 Lunar,Vector3 Environment,
    Vector3 DirectCap,CloudVolumeEncoding Encoding)
{
    #region Public API
    /// <summary>Admits a conservative recurrence bound without discarding energy from an older publication.</summary>
    internal static CloudVolumeLightingBounds Admit(AtmosphereLighting light,CloudVolumeLightingBounds? previous,CloudVolumeEncoding retained)
    {
        Validate(light.Solar);Validate(light.Lunar);Validate(light.Environment);
        var solar=Vector3.Max(light.Solar,previous?.Solar??Vector3.Zero);
        var lunar=Vector3.Max(light.Lunar,previous?.Lunar??Vector3.Zero);
        var environment=Vector3.Max(light.Environment,previous?.Environment??Vector3.Zero);
        const double a=.9,g=.6;
        double maximumPhase=(1+g)/(4*Math.PI*(1-g)*(1-g));
        Vector3 q=new(LightingStorage.UpperBound(a*maximumPhase*((double)solar.X+lunar.X)),
            LightingStorage.UpperBound(a*maximumPhase*((double)solar.Y+lunar.Y)),
            LightingStorage.UpperBound(a*maximumPhase*((double)solar.Z+lunar.Z)));
        Vector3 cap=new(LightingStorage.UpperBound(a*((double)q.X+environment.X)/(1-a)),
            LightingStorage.UpperBound(a*((double)q.Y+environment.Y)/(1-a)),
            LightingStorage.UpperBound(a*((double)q.Z+environment.Z)/(1-a)));
        cap=Vector3.Max(cap,retained.Cap);
        // Every gather accumulates nonnegative physical sources before bounded half encoding.
        // Admit its float32 accumulation headroom before uploading any new source or copying a field.
        for(int i=0;i<3;i++)_=LightingStorage.UpperBound(((double)q[i]+cap[i]+environment[i])*(1+2048.0/(1<<23)));
        return new(solar,lunar,environment,q,new(cap));
    }
    #endregion
    #region Private
    /// <summary>Rejects unsupported physical inputs before mutating retained bounds.</summary>
    private static void Validate(Vector3 value)
    {
        if(!float.IsFinite(value.X)||!float.IsFinite(value.Y)||!float.IsFinite(value.Z)||Vector3.Min(value,Vector3.Zero)!=Vector3.Zero)
            throw new ArgumentOutOfRangeException(nameof(value));
    }
    #endregion
}
