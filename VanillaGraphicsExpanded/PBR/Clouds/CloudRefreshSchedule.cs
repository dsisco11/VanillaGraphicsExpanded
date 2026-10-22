using System;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Optimizes cyclic phase labels without changing the spatial rank masks or four-frame coverage.</summary>
internal static class CloudRefreshSchedule
{
    #region Public API
    /// <summary>Relabels eight independent spatial masks to reduce repeated orders between adjacent sweeps.</summary>
    internal static float[] Create(ReadOnlySpan<float> masks, int side, int sweeps)
    {
        int pixels=checked(side*side);
        if(side<=0||sweeps!=8||masks.Length!=pixels*sweeps)throw new ArgumentException("Expected eight complete spatial masks.");
        var result=masks.ToArray();var rotations=new int[sweeps];
        // Coordinate descent includes the periodic last-to-first boundary. A global cyclic label
        // rotation preserves each slice's spatial rank ordering and balanced phase populations.
        // This objective decorrelates orders; it does not certify spatiotemporal blue-noise spectra.
        for(int pass=0;pass<4;pass++)for(int sweep=1;sweep<sweeps;sweep++)
        {
            int best=rotations[sweep],bestCost=int.MaxValue;
            for(int rotation=0;rotation<4;rotation++)
            {
                int cost=0;
                for(int pixel=0;pixel<pixels;pixel++)
                {
                    int phase=((int)(masks[sweep*pixels+pixel]*4)+rotation)&3;
                    for(int direction=0;direction<2;direction++)
                    {
                        int neighbor=(sweep+(direction==0?sweeps-1:1))%sweeps;
                        int other=((int)(masks[neighbor*pixels+pixel]*4)+rotations[neighbor])&3;
                        if(phase==other)cost++;
                    }
                }
                if(cost<bestCost){best=rotation;bestCost=cost;}
            }
            rotations[sweep]=best;
        }
        for(int sweep=1;sweep<sweeps;sweep++)for(int pixel=0;pixel<pixels;pixel++)
        {
            float shifted=masks[sweep*pixels+pixel]+rotations[sweep]*.25f;
            result[sweep*pixels+pixel]=shifted-MathF.Floor(shifted);
        }
        return result;
    }
    #endregion
}
