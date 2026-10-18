using System;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Separates physical trace and reconstruction extents and bounds their complete nominal payload allocation.</summary>
internal readonly record struct CloudResolutionLayout(int TraceWidth,int TraceHeight,int Width,int Height,CloudTraceScale Scale)
{
    #region Public API
    /// <summary>Reports all allocated RGBA32F payload layers without implying resident-memory usage.</summary>
    internal long NominalBytes => Scale==CloudTraceScale.Quarter
        ? 48L*TraceWidth*TraceHeight+96L*Width*Height : 144L*Width*Height;

    /// <summary>Rounds each screen scale independently and rejects unsupported extents before any GPU allocation.</summary>
    internal static CloudResolutionLayout Create(int viewWidth,int viewHeight,CloudTraceScale scale)
    {
        if(viewWidth is <1 or >16384 || viewHeight is <1 or >16384 || scale is not (CloudTraceScale.Half or CloudTraceScale.Quarter))
            throw new ArgumentOutOfRangeException(nameof(scale));
        int divisor=(int)scale;
        var layout=new CloudResolutionLayout((viewWidth+divisor-1)/divisor,(viewHeight+divisor-1)/divisor,
            (viewWidth+1)/2,(viewHeight+1)/2,scale);
        if(layout.NominalBytes>1024L*1024*1024)throw new ArgumentOutOfRangeException(nameof(viewWidth),"Cloud targets exceed the allocation limit.");
        return layout;
    }
    #endregion
}
