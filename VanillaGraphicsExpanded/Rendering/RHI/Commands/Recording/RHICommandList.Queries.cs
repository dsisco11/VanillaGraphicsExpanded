using System;
namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Retains queries arguments without consulting executed state.</summary>
public sealed partial class RHICommandList
{
    private readonly CommandPages<BeginQueryPayload> payloadBeginQuery = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct BeginQueryPayload(int query);
    private readonly CommandPages<EndQueryPayload> payloadEndQuery = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct EndQueryPayload(int query);
    private readonly CommandPages<WriteTimestampPayload> payloadWriteTimestamp = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct WriteTimestampPayload(int query);
    private readonly CommandPages<RequestQueryResultPayload> payloadRequestQueryResult = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct RequestQueryResultPayload(int query, int ticket);
    private readonly CommandPages<EnqueueReadbackPayload> payloadEnqueueReadback = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct EnqueueReadbackPayload(int description, int ticket);
    private readonly CommandPages<BeginConditionalRenderPayload> payloadBeginConditionalRender = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct BeginConditionalRenderPayload(int query, ConditionalRenderPolicy mode);
    private readonly CommandPages<EndConditionalRenderPayload> payloadEndConditionalRender = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct EndConditionalRenderPayload();
    #region Public API
    #region Query execution
    /// <summary>Records begin query with supplied inputs only; native validation remains at execution.</summary>
    public void BeginQuery(GpuQueryRef query)
    {
        RequireRecording();
        if (query.IsEmpty || !queries.Add(query)) throw new InvalidOperationException("Query is empty or already active in this list.");

        int payload = payloadBeginQuery.Add(new(queryReferencesArguments.Add(query)));
        headers.Add(new(CommandOperation.BeginQuery, payload));
    }
    /// <summary>Records end query with supplied inputs only; native validation remains at execution.</summary>
    public void EndQuery(GpuQueryRef query)
    {
        RequireRecording();
        if (!queries.Remove(query)) throw new InvalidOperationException("No matching recorded query is active.");

        int payload = payloadEndQuery.Add(new(queryReferencesArguments.Add(query)));
        headers.Add(new(CommandOperation.EndQuery, payload));
    }
    /// <summary>Records write timestamp with supplied inputs only; native validation remains at execution.</summary>
    public void WriteTimestamp(GpuQueryRef query)
    {
        RequireRecording();
        if (query.IsEmpty) throw new ArgumentException("A query is required.");

        int payload = payloadWriteTimestamp.Add(new(queryReferencesArguments.Add(query)));
        headers.Add(new(CommandOperation.WriteTimestamp, payload));
    }
    #endregion
    #region Result requests
    /// <summary>Records request query result with supplied inputs only; native validation remains at execution.</summary>
    public RHIQueryTicket RequestQueryResult(GpuQueryRef query)
    {
        RequireRecording();
        if (query.IsEmpty || queries.Contains(query)) throw new ArgumentException("Query must be supplied and outside its active interval.");
        var ticket = new RHIQueryTicket(origin); requests.Add(ticket);
        int payload = payloadRequestQueryResult.Add(new(queryReferencesArguments.Add(query), queryTicketArguments.Add(ticket)));
        headers.Add(new(CommandOperation.RequestQueryResult, payload));
        return ticket;
    }
    /// <summary>Records enqueue readback with supplied inputs only; native validation remains at execution.</summary>
    public RHIReadbackTicket EnqueueReadback(RHIReadbackDescription description)
    {
        RequireRecording();
        if (description.IsTexture) ShapeImage(description.Texture, description.Region); else ShapeBuffer(description.Buffer.Buffer, description.Buffer.Offset, description.Buffer.Length);
        var ticket = new RHIReadbackTicket(origin); requests.Add(ticket);
        int payload = payloadEnqueueReadback.Add(new(readbackDescriptionArguments.Add(description), readbackTicketArguments.Add(ticket)));
        headers.Add(new(CommandOperation.EnqueueReadback, payload));
        return ticket;
    }
    #endregion
    #region Conditional execution
    /// <summary>Records begin conditional render with supplied inputs only; native validation remains at execution.</summary>
    public void BeginConditionalRender(GpuQueryRef query, ConditionalRenderPolicy mode)
    {
        RequireRecording();
        if (conditional || query.IsEmpty || !Enum.IsDefined(mode)) throw new InvalidOperationException("Invalid or nested conditional interval."); conditional = true;

        int payload = payloadBeginConditionalRender.Add(new(queryReferencesArguments.Add(query), mode));
        headers.Add(new(CommandOperation.BeginConditionalRender, payload));
    }
    /// <summary>Records end conditional render with supplied inputs only; native validation remains at execution.</summary>
    public void EndConditionalRender()
    {
        RequireRecording();
        if (!conditional) throw new InvalidOperationException("No recorded conditional interval is active."); conditional = false;

        int payload = payloadEndConditionalRender.Add(new());
        headers.Add(new(CommandOperation.EndConditionalRender, payload));
    }
    #endregion
    #endregion
    #region Internal API
    /// <summary>Interprets this operation family through the existing immediate context.</summary>
    internal void ExecuteQueries(RHICommand command, RHICommandContext context)
    {
        switch (command.Operation)
        {
            case CommandOperation.BeginQuery:
            {
                var value = payloadBeginQuery.Get(command.Payload);
                context.BeginQuery(queryReferencesArguments.Get(value.query));
                context.RecordedQueryStarted(queryReferencesArguments.Get(value.query));
                return;
            }
            case CommandOperation.EndQuery:
            {
                var value = payloadEndQuery.Get(command.Payload);
                try { context.EndQuery(queryReferencesArguments.Get(value.query)); }
                finally { context.RecordedQueryEnded(queryReferencesArguments.Get(value.query)); }
                return;
            }
            case CommandOperation.WriteTimestamp:
            {
                var value = payloadWriteTimestamp.Get(command.Payload);
                context.WriteTimestamp(queryReferencesArguments.Get(value.query));

                return;
            }
            case CommandOperation.RequestQueryResult:
            {
                var value = payloadRequestQueryResult.Get(command.Payload);
                if (queryTicketArguments.Get(value.ticket).Status == CompletionStatus.Pending) queryTicketArguments.Get(value.ticket).Adopt(context.RequestQueryResult(queryReferencesArguments.Get(value.query)));

                return;
            }
            case CommandOperation.EnqueueReadback:
            {
                var value = payloadEnqueueReadback.Get(command.Payload);
                if (readbackTicketArguments.Get(value.ticket).Status == CompletionStatus.Pending) readbackTicketArguments.Get(value.ticket).Adopt(context.EnqueueReadback(readbackDescriptionArguments.Get(value.description)));

                return;
            }
            case CommandOperation.BeginConditionalRender:
            {
                var value = payloadBeginConditionalRender.Get(command.Payload);
                context.BeginConditionalRender(queryReferencesArguments.Get(value.query), value.mode);
                context.recordedConditional = true;
                return;
            }
            case CommandOperation.EndConditionalRender:
            {
                var value = payloadEndConditionalRender.Get(command.Payload);
                try { context.EndConditionalRender(); } finally { context.recordedConditional = false; }
                return;
            }
            default: throw new InvalidOperationException("Unexpected operation family.");
        }
    }
    #endregion
}
