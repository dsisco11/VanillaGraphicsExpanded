using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns cached raster modes transitions.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Establishes CullMode while suppressing a known identical native transition.</summary>
    internal void SetCullMode(CullFaceMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        ValidateCompleteMutation();
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.CullMode) && rasterizer.CullMode == mode) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.CullMode;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.CullFace(mode);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.CullMode = mode;
        rasterizerKnown |= RasterizerStateKnowledge.CullMode;
    }

    /// <summary>Establishes FrontFace while suppressing a known identical native transition.</summary>
    internal void SetFrontFace(FrontFaceDirection winding)
    {
        if (!Enum.IsDefined(winding)) throw new ArgumentOutOfRangeException(nameof(winding));
        ValidateCompleteMutation();
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.FrontFace) && rasterizer.FrontFace == winding) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.FrontFace;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.FrontFace(winding);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.FrontFace = winding;
        rasterizerKnown |= RasterizerStateKnowledge.FrontFace;
    }

    /// <summary>Establishes PolygonOffset while suppressing a known identical native transition.</summary>
    internal void SetPolygonOffset(float factor, float units)
    {
        if (!float.IsFinite(factor) || !float.IsFinite(units)) throw new ArgumentOutOfRangeException(nameof(factor));
        ValidateCompleteMutation();
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonOffset) && rasterizer.PolygonOffset == (factor, units)) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.PolygonOffset;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.PolygonOffset(factor, units);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.PolygonOffset = (factor, units);
        rasterizerKnown |= RasterizerStateKnowledge.PolygonOffset;
    }

    /// <summary>Sets both polygon faces without inheriting an undeclared native face mode.</summary>
    internal void SetPolygonModes(PolygonMode front, PolygonMode back)
    {
        if (!Enum.IsDefined(front) || !Enum.IsDefined(back)) throw new ArgumentOutOfRangeException(nameof(front));
        if (GpuSupport.Graphics.CoreProfile && front != back) throw new NotSupportedException("Core polygon modes must match.");
        ValidateCompleteMutation();
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonModes) && rasterizer.PolygonModes == (front, back)) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.PolygonModes;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (front == back) { GL.PolygonMode(MaterialFace.FrontAndBack, front); FixedFunctionCalls++; }
        else { GL.PolygonMode(MaterialFace.Front, front); GL.PolygonMode(MaterialFace.Back, back); FixedFunctionCalls += 2; }
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.PolygonModes = (front, back);
        rasterizerKnown |= RasterizerStateKnowledge.PolygonModes;
    }

    /// <summary>Changes a selected compatibility polygon face without guessing the other face's value.</summary>
    internal void SetPolygonMode(MaterialFace face, PolygonMode mode)
    {
        if (face is not (MaterialFace.Front or MaterialFace.Back or MaterialFace.FrontAndBack) || !Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(face));
        if (face == MaterialFace.FrontAndBack) { SetPolygonModes(mode, mode); return; }
        if (GpuSupport.Graphics.CoreProfile) throw new NotSupportedException("Single-face polygon modes require compatibility profile.");
        ValidateCompleteMutation();
        bool known = rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonModes);
        var previous = rasterizer.PolygonModes;
        if (known && (face == MaterialFace.Front ? previous.Front : previous.Back) == mode) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.PolygonModes;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.PolygonMode(face, mode); FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        // A single-face command cannot establish knowledge for an unobserved opposite face.
        if (known)
        {
            rasterizer.PolygonModes = face == MaterialFace.Front ? (mode, previous.Back) : (previous.Front, mode);
            rasterizerKnown |= RasterizerStateKnowledge.PolygonModes;
        }
    }
    #endregion
}
