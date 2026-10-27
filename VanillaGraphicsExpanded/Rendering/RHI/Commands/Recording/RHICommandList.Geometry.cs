using System;
namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Retains geometry arguments without consulting executed state.</summary>
public sealed partial class RHICommandList
{
    private readonly CommandPages<DrawPayload> payloadDraw = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct DrawPayload(int geometry, RHIDraw draw);
    private readonly CommandPages<DrawIndexedPayload> payloadDrawIndexed = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct DrawIndexedPayload(int geometry, RHIDrawIndexed draw);
    private readonly CommandPages<DrawIndexedBatchPayload> payloadDrawIndexedBatch = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct DrawIndexedBatchPayload(int geometry, (int Offset, int Length) draws);
    private readonly CommandPages<DrawIndirectPayload> payloadDrawIndirect = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct DrawIndirectPayload(int geometry, int draw);
    private readonly CommandPages<DrawIndexedIndirectPayload> payloadDrawIndexedIndirect = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct DrawIndexedIndirectPayload(int geometry, int draw);
    private readonly CommandPages<DispatchPayload> payloadDispatch = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct DispatchPayload(RHIDispatch dispatch);
    private readonly CommandPages<DispatchIndirectPayload> payloadDispatchIndirect = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct DispatchIndirectPayload(int arguments);
    #region Public API
    #region Direct draws
    /// <summary>Records draw with supplied inputs only; native validation remains at execution.</summary>
    public void Draw(RHIGeometryInputs geometry, RHIDraw draw)
    {
        RequireRecording();
        ShapeDraw(geometry); if (draw.FirstVertex < 0 || draw.VertexCount < 0 || draw.InstanceCount < 0) throw new ArgumentOutOfRangeException(nameof(draw));

        int payload = payloadDraw.Add(new(geometryInputsArguments.Add(geometry), draw));
        headers.Add(new(CommandOperation.Draw, payload));
    }
    /// <summary>Records draw indexed with supplied inputs only; native validation remains at execution.</summary>
    public void DrawIndexed(RHIGeometryInputs geometry, RHIDrawIndexed draw)
    {
        RequireRecording();
        ShapeDraw(geometry); ShapeIndexed(draw);

        int payload = payloadDrawIndexed.Add(new(geometryInputsArguments.Add(geometry), draw));
        headers.Add(new(CommandOperation.DrawIndexed, payload));
    }
    /// <summary>Records draw indexed batch with supplied inputs only; native validation remains at execution.</summary>
    public void DrawIndexedBatch(RHIGeometryInputs geometry, ReadOnlySpan<RHIDrawIndexed> draws)
    {
        RequireRecording();
        ShapeDraw(geometry); foreach (var draw in draws) ShapeIndexed(draw);

        int payload = payloadDrawIndexedBatch.Add(new(geometryInputsArguments.Add(geometry), indexedDraws.Add(draws)));
        headers.Add(new(CommandOperation.DrawIndexedBatch, payload));
    }
    #endregion
    #region Indirect draws
    /// <summary>Records draw indirect with supplied inputs only; native validation remains at execution.</summary>
    public void DrawIndirect(RHIGeometryInputs geometry, RHIIndirectDraw draw)
    {
        RequireRecording();
        ShapeDraw(geometry); ShapeIndirect(draw);

        int payload = payloadDrawIndirect.Add(new(geometryInputsArguments.Add(geometry), indirectDrawArguments.Add(draw)));
        headers.Add(new(CommandOperation.DrawIndirect, payload));
    }
    /// <summary>Records draw indexed indirect with supplied inputs only; native validation remains at execution.</summary>
    public void DrawIndexedIndirect(RHIGeometryInputs geometry, RHIIndirectDraw draw)
    {
        RequireRecording();
        ShapeDraw(geometry); ShapeIndirect(draw);

        int payload = payloadDrawIndexedIndirect.Add(new(geometryInputsArguments.Add(geometry), indirectDrawArguments.Add(draw)));
        headers.Add(new(CommandOperation.DrawIndexedIndirect, payload));
    }
    #endregion
    #region Compute
    /// <summary>Records dispatch with supplied inputs only; native validation remains at execution.</summary>
    public void Dispatch(RHIDispatch dispatch)
    {
        RequireRecording();
        ShapeCompute(); if (dispatch.X < 0 || dispatch.Y < 0 || dispatch.Z < 0) throw new ArgumentOutOfRangeException(nameof(dispatch));

        int payload = payloadDispatch.Add(new(dispatch));
        headers.Add(new(CommandOperation.Dispatch, payload));
    }
    /// <summary>Records dispatch indirect with supplied inputs only; native validation remains at execution.</summary>
    public void DispatchIndirect(RHIBufferRange arguments)
    {
        RequireRecording();
        ShapeCompute(); ShapeBuffer(arguments.Buffer, arguments.Offset, arguments.Length);

        int payload = payloadDispatchIndirect.Add(new(bufferRangeArguments.Add(arguments)));
        headers.Add(new(CommandOperation.DispatchIndirect, payload));
    }
    #endregion
    #endregion
    #region Internal API
    /// <summary>Interprets geometry through the existing immediate context.</summary>
    internal void ExecuteGeometry(RHICommand command, RHICommandContext context)
    {
        switch (command.Operation)
        {
            case CommandOperation.Draw:
            {
                var value = payloadDraw.Get(command.Payload);
                context.Draw(geometryInputsArguments.Get(value.geometry), value.draw);

                return;
            }
            case CommandOperation.DrawIndexed:
            {
                var value = payloadDrawIndexed.Get(command.Payload);
                context.DrawIndexed(geometryInputsArguments.Get(value.geometry), value.draw);

                return;
            }
            case CommandOperation.DrawIndexedBatch:
            {
                var value = payloadDrawIndexedBatch.Get(command.Payload);
                context.DrawIndexedBatch(geometryInputsArguments.Get(value.geometry), indexedDraws.Read(value.draws));

                return;
            }
            case CommandOperation.DrawIndirect:
            {
                var value = payloadDrawIndirect.Get(command.Payload);
                context.DrawIndirect(geometryInputsArguments.Get(value.geometry), indirectDrawArguments.Get(value.draw));

                return;
            }
            case CommandOperation.DrawIndexedIndirect:
            {
                var value = payloadDrawIndexedIndirect.Get(command.Payload);
                context.DrawIndexedIndirect(geometryInputsArguments.Get(value.geometry), indirectDrawArguments.Get(value.draw));

                return;
            }
            case CommandOperation.Dispatch:
            {
                var value = payloadDispatch.Get(command.Payload);
                context.Dispatch(value.dispatch);

                return;
            }
            case CommandOperation.DispatchIndirect:
            {
                var value = payloadDispatchIndirect.Get(command.Payload);
                context.DispatchIndirect(bufferRangeArguments.Get(value.arguments));

                return;
            }
            default: throw new InvalidOperationException("Unexpected operation family.");
        }
    }
    #endregion
}
