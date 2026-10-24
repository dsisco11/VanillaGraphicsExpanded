using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Resolves and restores configurable raster fields without flattening disabled parameters.</summary>
internal sealed partial class StateCache
{
    #region Private
    /// <summary>Queries only unknown requested fields supported by the current profile.</summary>
    private void ResolveConfigurableRaster(RasterizerStateKnowledge coverage)
    {
        var caps = GpuSupport.Graphics;
        // Unavailable native fields have explicit neutral snapshot values, never invalid zero enums.
        if (!caps.ClipControl && coverage.HasFlag(RasterizerStateKnowledge.ClipControl))
        {
            rasterizer.ClipOrigin = ClipOrigin.LowerLeft;
            rasterizer.ClipDepth = ClipDepthMode.NegativeOneToOne;
            rasterizerKnown |= RasterizerStateKnowledge.ClipControl;
        }
        if (coverage.HasFlag(RasterizerStateKnowledge.LineSmooth) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.LineSmooth))
        {
            rasterizer.LineSmooth = QueryBoundary(() => GL.IsEnabled(EnableCap.LineSmooth));
            rasterizerKnown |= RasterizerStateKnowledge.LineSmooth;
        }
        if (coverage.HasFlag(RasterizerStateKnowledge.PolygonSmooth) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonSmooth))
        {
            rasterizer.PolygonSmooth = QueryBoundary(() => GL.IsEnabled(EnableCap.PolygonSmooth));
            rasterizerKnown |= RasterizerStateKnowledge.PolygonSmooth;
        }
        if (coverage.HasFlag(RasterizerStateKnowledge.ClipControl) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.ClipControl) && caps.ClipControl)
        {
            rasterizer.ClipOrigin = QueryBoundary(() => (ClipOrigin)GL.GetInteger(GetPName.ClipOrigin));
            rasterizer.ClipDepth = QueryBoundary(() => (ClipDepthMode)GL.GetInteger(GetPName.ClipDepthMode));
            rasterizerKnown |= RasterizerStateKnowledge.ClipControl;
        }
        if (coverage.HasFlag(RasterizerStateKnowledge.PointSpriteOrigin) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.PointSpriteOrigin))
        {
            rasterizer.PointSpriteOrigin = QueryBoundary(() => (PointSpriteCoordOriginParameter)GL.GetInteger((GetPName)All.PointSpriteCoordOrigin));
            rasterizerKnown |= RasterizerStateKnowledge.PointSpriteOrigin;
        }
        if (coverage.HasFlag(RasterizerStateKnowledge.ClipDistances))
        {
            if (caps.MaxClipDistances > 32) throw new NotSupportedException("Clip-distance mask exceeds 32 bits.");
            for (int i = 0; i < caps.MaxClipDistances; i++)
            {
                uint bit = 1u << i;
                if ((clipDistancesKnown & bit) != 0) continue;
                int index = i;
                bool enabled = QueryBoundary(() => GL.IsEnabled((EnableCap)((int)EnableCap.ClipDistance0 + index)));
                if (enabled) rasterizer.ClipDistances |= bit; else rasterizer.ClipDistances &= ~bit;
                clipDistancesKnown |= bit;
            }
            rasterizerKnown |= RasterizerStateKnowledge.ClipDistances;
        }
    }
    /// <summary>Restores fields independently so one failure cannot suppress remaining cleanup attempts.</summary>
    private void RestoreConfigurableRaster(PipelineStateSnapshot snapshot, List<Exception> failures)
    {
        var coverage = snapshot.Coverage.Rasterizer;
        var saved = snapshot.Rasterizer;
        var caps = GpuSupport.Graphics;
        if (coverage.HasFlag(RasterizerStateKnowledge.LineSmooth) && (!rasterizerKnown.HasFlag(RasterizerStateKnowledge.LineSmooth) || rasterizer.LineSmooth != saved.LineSmooth))
            RestoreBoundaryField(() => SetLineSmooth(saved.LineSmooth), () => rasterizerKnown &= ~RasterizerStateKnowledge.LineSmooth, failures);
        if (coverage.HasFlag(RasterizerStateKnowledge.PolygonSmooth) && (!rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonSmooth) || rasterizer.PolygonSmooth != saved.PolygonSmooth))
            RestoreBoundaryField(() => SetPolygonSmooth(saved.PolygonSmooth), () => rasterizerKnown &= ~RasterizerStateKnowledge.PolygonSmooth, failures);
        if (coverage.HasFlag(RasterizerStateKnowledge.ClipControl) && caps.ClipControl && (!rasterizerKnown.HasFlag(RasterizerStateKnowledge.ClipControl) || rasterizer.ClipOrigin != saved.ClipOrigin || rasterizer.ClipDepth != saved.ClipDepth))
            RestoreBoundaryField(() => SetClipControl(saved.ClipOrigin, saved.ClipDepth), () => rasterizerKnown &= ~RasterizerStateKnowledge.ClipControl, failures);
        if (coverage.HasFlag(RasterizerStateKnowledge.PointSpriteOrigin) && (!rasterizerKnown.HasFlag(RasterizerStateKnowledge.PointSpriteOrigin) || rasterizer.PointSpriteOrigin != saved.PointSpriteOrigin))
            RestoreBoundaryField(() => SetPointSpriteOrigin(saved.PointSpriteOrigin), () => rasterizerKnown &= ~RasterizerStateKnowledge.PointSpriteOrigin, failures);
        if (coverage.HasFlag(RasterizerStateKnowledge.ClipDistances))
            for (int i = 0; i < caps.MaxClipDistances; i++)
            {
                int index = i; uint bit = 1u << i;
                if ((clipDistancesKnown & bit) != 0 && (rasterizer.ClipDistances & bit) == (saved.ClipDistances & bit)) continue;
                RestoreBoundaryField(() => SetClipDistance(index, (saved.ClipDistances & bit) != 0),
                    () => { clipDistancesKnown &= ~bit; rasterizerKnown &= ~RasterizerStateKnowledge.ClipDistances; }, failures);
            }
    }
    #endregion
}

