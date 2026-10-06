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
        if (caps.CoreProfile)
        {
            rasterizer.AlphaTest = rasterizer.PointSmooth = rasterizer.LineStipple = rasterizer.PolygonStipple = false;
            rasterizer.AlphaComparison = AlphaFunction.Always; rasterizer.AlphaReference = 0;
            rasterizer.LineStippleFactor = 1; rasterizer.LineStipplePattern = ushort.MaxValue;
            rasterizer.PolygonStipplePattern = Pipeline.Descriptions.RasterizerDesc.FullPolygonStipple;
            rasterizerKnown |= coverage & (RasterizerStateKnowledge.AlphaTest | RasterizerStateKnowledge.PointSmooth
                | RasterizerStateKnowledge.LineStipple | RasterizerStateKnowledge.PolygonStipple
                | RasterizerStateKnowledge.AlphaFunction | RasterizerStateKnowledge.LineStippleParameters
                | RasterizerStateKnowledge.PolygonStipplePattern);
        }
        if (coverage.HasFlag(RasterizerStateKnowledge.AlphaTest) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.AlphaTest) && !caps.CoreProfile)
        {
            rasterizer.AlphaTest = QueryBoundary(() => GL.IsEnabled(EnableCap.AlphaTest));
            rasterizerKnown |= RasterizerStateKnowledge.AlphaTest;
        }
        if (coverage.HasFlag(RasterizerStateKnowledge.PointSmooth) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.PointSmooth) && !caps.CoreProfile)
        {
            rasterizer.PointSmooth = QueryBoundary(() => GL.IsEnabled(EnableCap.PointSmooth));
            rasterizerKnown |= RasterizerStateKnowledge.PointSmooth;
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
        if (coverage.HasFlag(RasterizerStateKnowledge.LineStipple) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.LineStipple) && !caps.CoreProfile)
        {
            rasterizer.LineStipple = QueryBoundary(() => GL.IsEnabled(EnableCap.LineStipple));
            rasterizerKnown |= RasterizerStateKnowledge.LineStipple;
        }
        if (coverage.HasFlag(RasterizerStateKnowledge.PolygonStipple) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonStipple) && !caps.CoreProfile)
        {
            rasterizer.PolygonStipple = QueryBoundary(() => GL.IsEnabled(EnableCap.PolygonStipple));
            rasterizerKnown |= RasterizerStateKnowledge.PolygonStipple;
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
        if (coverage.HasFlag(RasterizerStateKnowledge.AlphaFunction) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.AlphaFunction) && !caps.CoreProfile)
        {
            rasterizer.AlphaComparison = QueryBoundary(() => (AlphaFunction)GL.GetInteger(GetPName.AlphaTestFunc));
            rasterizer.AlphaReference = QueryBoundary(() => GL.GetFloat(GetPName.AlphaTestRef));
            rasterizerKnown |= RasterizerStateKnowledge.AlphaFunction;
        }
        if (coverage.HasFlag(RasterizerStateKnowledge.LineStippleParameters) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.LineStippleParameters) && !caps.CoreProfile)
        {
            rasterizer.LineStippleFactor = QueryBoundary(() => GL.GetInteger(GetPName.LineStippleRepeat));
            rasterizer.LineStipplePattern = QueryBoundary(() => (ushort)GL.GetInteger(GetPName.LineStipplePattern));
            rasterizerKnown |= RasterizerStateKnowledge.LineStippleParameters;
        }
        if (coverage.HasFlag(RasterizerStateKnowledge.PolygonStipplePattern) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonStipplePattern) && !caps.CoreProfile)
        {
            rasterizer.PolygonStipplePattern = ReadPolygonStipplePattern();
            rasterizerKnown |= RasterizerStateKnowledge.PolygonStipplePattern;
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
        if (coverage.HasFlag(RasterizerStateKnowledge.AlphaTest) && !caps.CoreProfile)
            RestoreBoundaryField(() => SetAlphaTest(saved.AlphaTest), () => rasterizerKnown &= ~RasterizerStateKnowledge.AlphaTest, failures);
        if (coverage.HasFlag(RasterizerStateKnowledge.PointSmooth) && !caps.CoreProfile)
            RestoreBoundaryField(() => SetPointSmooth(saved.PointSmooth), () => rasterizerKnown &= ~RasterizerStateKnowledge.PointSmooth, failures);
        if (coverage.HasFlag(RasterizerStateKnowledge.LineSmooth))
            RestoreBoundaryField(() => SetLineSmooth(saved.LineSmooth), () => rasterizerKnown &= ~RasterizerStateKnowledge.LineSmooth, failures);
        if (coverage.HasFlag(RasterizerStateKnowledge.PolygonSmooth))
            RestoreBoundaryField(() => SetPolygonSmooth(saved.PolygonSmooth), () => rasterizerKnown &= ~RasterizerStateKnowledge.PolygonSmooth, failures);
        if (coverage.HasFlag(RasterizerStateKnowledge.LineStipple) && !caps.CoreProfile)
            RestoreBoundaryField(() => SetLineStipple(saved.LineStipple), () => rasterizerKnown &= ~RasterizerStateKnowledge.LineStipple, failures);
        if (coverage.HasFlag(RasterizerStateKnowledge.PolygonStipple) && !caps.CoreProfile)
            RestoreBoundaryField(() => SetPolygonStipple(saved.PolygonStipple), () => rasterizerKnown &= ~RasterizerStateKnowledge.PolygonStipple, failures);
        if (coverage.HasFlag(RasterizerStateKnowledge.ClipControl) && caps.ClipControl)
            RestoreBoundaryField(() => SetClipControl(saved.ClipOrigin, saved.ClipDepth), () => rasterizerKnown &= ~RasterizerStateKnowledge.ClipControl, failures);
        if (coverage.HasFlag(RasterizerStateKnowledge.PointSpriteOrigin))
            RestoreBoundaryField(() => SetPointSpriteOrigin(saved.PointSpriteOrigin), () => rasterizerKnown &= ~RasterizerStateKnowledge.PointSpriteOrigin, failures);
        if (coverage.HasFlag(RasterizerStateKnowledge.AlphaFunction) && !caps.CoreProfile)
            RestoreBoundaryField(() => SetAlphaFunction(saved.AlphaComparison, saved.AlphaReference), () => rasterizerKnown &= ~RasterizerStateKnowledge.AlphaFunction, failures);
        if (coverage.HasFlag(RasterizerStateKnowledge.LineStippleParameters) && !caps.CoreProfile)
            RestoreBoundaryField(() => SetLineStipple(saved.LineStippleFactor, saved.LineStipplePattern), () => rasterizerKnown &= ~RasterizerStateKnowledge.LineStippleParameters, failures);
        if (coverage.HasFlag(RasterizerStateKnowledge.PolygonStipplePattern) && !caps.CoreProfile)
            RestoreBoundaryField(() => SetPolygonStipplePattern(saved.PolygonStipplePattern!), () => rasterizerKnown &= ~RasterizerStateKnowledge.PolygonStipplePattern, failures);
        if (coverage.HasFlag(RasterizerStateKnowledge.ClipDistances))
            for (int i = 0; i < caps.MaxClipDistances; i++)
            {
                int index = i; uint bit = 1u << i;
                RestoreBoundaryField(() => SetClipDistance(index, (saved.ClipDistances & bit) != 0),
                    () => { clipDistancesKnown &= ~bit; rasterizerKnown &= ~RasterizerStateKnowledge.ClipDistances; }, failures);
            }
    }
    #endregion
}

