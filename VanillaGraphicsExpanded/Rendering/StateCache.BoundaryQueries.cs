using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Queries only missing covered fields and publishes knowledge only after successful native reads.</summary>
internal sealed partial class StateCache
{
    #region Private
    #region Resolution
    /// <summary>Resolves scalar categories and all effective output aliases before optional work begins.</summary>
    private void ResolveBoundaryState(PipelineStateCoverage coverage, int count)
    {
        if (coverage.CompleteGraphics) ResolveCompleteGraphics();
        ResolveBoundaryDepth(coverage.Depth);
        ResolveBoundaryRasterizer(coverage.Rasterizer);
        if ((coverage.Rasterizer & RasterizerStateKnowledge.ConfigurableRaster) != 0) ResolveConfigurableRaster(coverage.Rasterizer);
        if (coverage.Assembly.HasFlag(PrimitiveAssemblyStateKnowledge.PatchVertices) && GpuSupport.Graphics.Tessellation
            && !assemblyKnown.HasFlag(PrimitiveAssemblyStateKnowledge.PatchVertices))
        {
            assembly.PatchVertices = QueryBoundary(() => GL.GetInteger(GetPName.PatchVertices));
            assemblyKnown |= PrimitiveAssemblyStateKnowledge.PatchVertices;
        }
        if (coverage.Dynamic.HasFlag(DynamicDrawStateKnowledge.Viewport) && !dynamicKnown.HasFlag(DynamicDrawStateKnowledge.Viewport))
        {
            var viewport = QueryBoundary(() =>
            {
                int[] value = new int[4];
                GL.GetInteger(GetPName.Viewport, value);
                return new DynamicDrawState { X = value[0], Y = value[1], Width = value[2], Height = value[3] };
            });
            dynamicState.X = viewport.X; dynamicState.Y = viewport.Y;
            dynamicState.Width = viewport.Width; dynamicState.Height = viewport.Height;
            dynamicKnown |= DynamicDrawStateKnowledge.Viewport;
        }
        if (coverage.ClearColor && !clearColorKnown)
        {
            clearColor = QueryBoundary(() =>
            {
                float[] value = new float[4];
                GL.GetFloat(GetPName.ColorClearValue, value);
                return new Vector4(value[0], value[1], value[2], value[3]);
            });
            clearColorKnown = true;
        }
        for (int i = 0; i < count; i++) ResolveBoundaryBlend(i, coverage.BlendAt(i));
    }

    /// <summary>Resolves depth fields independently, retaining partial knowledge on a later read failure.</summary>
    private void ResolveBoundaryDepth(DepthStateKnowledge coverage)
    {
        if (coverage.HasFlag(DepthStateKnowledge.TestEnabled) && !depthKnown.HasFlag(DepthStateKnowledge.TestEnabled))
        {
            depth.TestEnabled = QueryBoundary(() => GL.IsEnabled(EnableCap.DepthTest));
            depthKnown |= DepthStateKnowledge.TestEnabled;
        }
        if (coverage.HasFlag(DepthStateKnowledge.Comparison) && !depthKnown.HasFlag(DepthStateKnowledge.Comparison))
        {
            depth.Comparison = QueryBoundary(() => (DepthFunction)GL.GetInteger(GetPName.DepthFunc));
            depthKnown |= DepthStateKnowledge.Comparison;
        }
        if (coverage.HasFlag(DepthStateKnowledge.WriteEnabled) && !depthKnown.HasFlag(DepthStateKnowledge.WriteEnabled))
        {
            depth.WriteEnabled = QueryBoundary(() => GL.GetBoolean(GetPName.DepthWritemask));
            depthKnown |= DepthStateKnowledge.WriteEnabled;
        }
    }

    /// <summary>Resolves only requested rasterizer fields; unrelated category values remain uncaptured.</summary>
    private void ResolveBoundaryRasterizer(RasterizerStateKnowledge coverage)
    {
        if (coverage.HasFlag(RasterizerStateKnowledge.CullEnabled) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.CullEnabled))
        {
            rasterizer.CullEnabled = QueryBoundary(() => GL.IsEnabled(EnableCap.CullFace));
            rasterizerKnown |= RasterizerStateKnowledge.CullEnabled;
        }
        if (coverage.HasFlag(RasterizerStateKnowledge.ScissorEnabled) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.ScissorEnabled))
        {
            rasterizer.ScissorEnabled = QueryBoundary(() => GL.IsEnabled(EnableCap.ScissorTest));
            rasterizerKnown |= RasterizerStateKnowledge.ScissorEnabled;
        }
        if (coverage.HasFlag(RasterizerStateKnowledge.LineWidth) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.LineWidth))
        {
            rasterizer.LineWidth = QueryBoundary(() => GL.GetFloat(GetPName.LineWidth));
            rasterizerKnown |= RasterizerStateKnowledge.LineWidth;
        }
        if (coverage.HasFlag(RasterizerStateKnowledge.PointSize) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.PointSize))
        {
            rasterizer.PointSize = QueryBoundary(() => GL.GetFloat(GetPName.PointSize));
            rasterizerKnown |= RasterizerStateKnowledge.PointSize;
        }
        if (coverage.HasFlag(RasterizerStateKnowledge.ProvokingVertex) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.ProvokingVertex))
        {
            rasterizer.ProvokingVertex = QueryBoundary(() => (ProvokingVertexMode)GL.GetInteger(GetPName.ProvokingVertex));
            rasterizerKnown |= RasterizerStateKnowledge.ProvokingVertex;
        }
    }

    /// <summary>Reads per-output enables, separate factors and masks without flattening incoming mixed state.</summary>
    private void ResolveBoundaryBlend(int index, BlendStateKnowledge coverage)
    {
        if (coverage.HasFlag(BlendStateKnowledge.Enabled) && !blendKnown[index].HasFlag(BlendStateKnowledge.Enabled))
        {
            blend[index].Enabled = QueryBoundary(() => GL.IsEnabled(IndexedEnableCap.Blend, index));
            blendKnown[index] |= BlendStateKnowledge.Enabled;
        }
        if (coverage.HasFlag(BlendStateKnowledge.Factors) && !blendKnown[index].HasFlag(BlendStateKnowledge.Factors))
        {
            // Publish the aggregate only after all four independent native reads succeed.
            var factors = new GlBlendFunc((BlendingFactorSrc)QueryBoundaryBlendParameter(GetPName.BlendSrcRgb, index),
                (BlendingFactorDest)QueryBoundaryBlendParameter(GetPName.BlendDstRgb, index),
                (BlendingFactorSrc)QueryBoundaryBlendParameter(GetPName.BlendSrcAlpha, index),
                (BlendingFactorDest)QueryBoundaryBlendParameter(GetPName.BlendDstAlpha, index));
            if (GpuSupport.Graphics.IndependentBlend)
            {
                blend[index].Factors = factors;
                blendKnown[index] |= BlendStateKnowledge.Factors;
            }
            else
            {
                // Without independent factors, the global value is authoritative for every output.
                for (int output = 0; output < blend.Length; output++)
                {
                    blend[output].Factors = factors;
                    blendKnown[output] |= BlendStateKnowledge.Factors;
                }
            }
        }
        if (coverage.HasFlag(BlendStateKnowledge.Equations) && !blendKnown[index].HasFlag(BlendStateKnowledge.Equations))
        {
            var equations = ((BlendEquationMode)QueryBoundaryBlendParameter(GetPName.BlendEquationRgb, index),
                (BlendEquationMode)QueryBoundaryBlendParameter(GetPName.BlendEquationAlpha, index));
            if (GpuSupport.Graphics.IndependentBlend)
            {
                blend[index].Equations = equations;
                blendKnown[index] |= BlendStateKnowledge.Equations;
            }
            else
            {
                for (int output = 0; output < blend.Length; output++)
                {
                    blend[output].Equations = equations;
                    blendKnown[output] |= BlendStateKnowledge.Equations;
                }
            }
        }
        if (coverage.HasFlag(BlendStateKnowledge.WriteMask) && !blendKnown[index].HasFlag(BlendStateKnowledge.WriteMask))
        {
            blend[index].WriteMask = QueryBoundary(() =>
            {
                bool[] value = new bool[4];
                GL.GetBoolean(GetIndexedPName.ColorWritemask, index, value);
                return GlColorMask.FromRgba(value[0], value[1], value[2], value[3]);
            });
            blendKnown[index] |= BlendStateKnowledge.WriteMask;
        }
    }
    #endregion

    #region Native reads
    /// <summary>Uses global factor/equation queries where indexed blending is unavailable.</summary>
    private int QueryBoundaryBlendParameter(GetPName name, int index) => GpuSupport.Graphics.IndependentBlend
        ? QueryBoundaryIndexed(name, index) : QueryBoundary(() => GL.GetInteger(name));

    /// <summary>Reads an indexed integer through the checked query path.</summary>
    private int QueryBoundaryIndexed(GetPName name, int index) => QueryBoundary(() =>
    {
        GL.GetInteger((GetIndexedPName)name, index, out int value);
        return value;
    });

    /// <summary>Rejects pending/native errors instead of promoting a failed read's default return value.</summary>
    private T QueryBoundary<T>(Func<T> read)
    {
        if (ReadBoundaryError() != ErrorCode.NoError) throw new InvalidOperationException("Native error before boundary query.");
        BoundaryQueries++;
        T value = read();
        if (ReadBoundaryError() != ErrorCode.NoError) throw new InvalidOperationException("Native boundary query failed.");
        return value;
    }

    /// <summary>Measures native error polling without conflating it with saved-value resolution.</summary>
    private ErrorCode ReadBoundaryError()
    {
        BoundaryErrorChecks++;
        return GL.GetError();
    }
    #endregion
    #endregion
}
