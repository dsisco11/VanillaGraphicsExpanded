using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Transfers immutable polygon masks through the shared pixel-layout and binding owners.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Preserves an engine upload's pixel layout while withdrawing knowledge of the affected canonical mask.</summary>
    internal void UploadEnginePolygonStipple(byte[] pattern)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.PolygonStipplePattern);
        rasterizerKnown &= ~RasterizerStateKnowledge.PolygonStipplePattern;
        CheckBoundaryNativeError();
        GL.PolygonStipple(pattern); FixedFunctionCalls++;
        CheckBoundaryNativeError();
        // A successful upload uses arbitrary engine unpack state. Resolve its canonical bitmap only when needed.
    }
    /// <summary>Uploads a canonical bitmap without leaking temporary unpack layout or buffer binding.</summary>
    internal void SetPolygonStipplePattern(PipelineValues<byte> pattern)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.PolygonStipplePattern);
        if (pattern is null || pattern.Count != 128) throw new ArgumentException("Polygon stipple requires 128 bytes.");
        if (GpuSupport.Graphics.CoreProfile) throw new NotSupportedException("Polygon stipple requires compatibility profile.");
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonStipplePattern) && pattern.Equals(rasterizer.PolygonStipplePattern)) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.PolygonStipplePattern;
        TransferPolygonStipple(false, () => { GL.PolygonStipple(pattern.ToArray()); FixedFunctionCalls++; });
        rasterizer.PolygonStipplePattern = pattern;
        rasterizerKnown |= RasterizerStateKnowledge.PolygonStipplePattern;
    }
    #endregion
    #region Private
    /// <summary>Reads the complete canonical payload; immutable storage can safely be retained by snapshots.</summary>
    private PipelineValues<byte> ReadPolygonStipplePattern()
    {
        byte[] bytes = new byte[128];
        TransferPolygonStipple(true, () => { BoundaryQueries++; GL.GetPolygonStipple(bytes); });
        return new(bytes);
    }
    /// <summary>Restores independent transfer owners even when setup, transfer or another restoration fails.</summary>
    private void TransferPolygonStipple(bool pack, Action operation)
    {
        var target = pack ? BufferTarget.PixelPackBuffer : BufferTarget.PixelUnpackBuffer;
        int buffer = ResolveBoundaryBuffer(target);
        var packed = pack ? GetPixelPackState() : default;
        var unpacked = !pack ? GetPixelUnpackState() : default;
        var failures = new List<Exception>();
        bool restorationFailed = false;
        try
        {
            BindBuffer(target, 0);
            if (pack) SetPixelPackState(new(1)); else SetPixelUnpackState(new(1));
            // Binding and layout owners verify setup before any managed pointer is submitted.
            CheckBoundaryNativeError();
            operation();
            CheckBoundaryNativeError();
        }
        catch (Exception error) { failures.Add(error); }
        finally
        {
            try { if (pack) SetPixelPackState(packed); else SetPixelUnpackState(unpacked); }
            catch (Exception error) { restorationFailed = true; failures.Add(error); }
            try { BindBuffer(target, buffer); }
            catch (Exception error) { restorationFailed = true; bufferBindingByTarget.Remove(target); failures.Add(error); }
        }
        if (restorationFailed)
            throw new EngineBoundaryRestoreException("Polygon-stipple pixel-transfer restoration failed.", new AggregateException(failures));
        if (failures.Count != 0) throw new AggregateException("Polygon-stipple transfer failed.", failures);
    }
    #endregion
}
