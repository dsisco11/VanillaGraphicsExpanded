using System;
namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Retains capture arguments without consulting executed state.</summary>
public sealed partial class RHICommandList
{
    private readonly CommandPages<BeginTransformFeedbackPayload> payloadBeginTransformFeedback = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct BeginTransformFeedbackPayload(int feedback, CapturePrimitive primitive);
    private readonly CommandPages<EndTransformFeedbackPayload> payloadEndTransformFeedback = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct EndTransformFeedbackPayload();
    private readonly CommandPages<PauseTransformFeedbackPayload> payloadPauseTransformFeedback = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct PauseTransformFeedbackPayload();
    private readonly CommandPages<ResumeTransformFeedbackPayload> payloadResumeTransformFeedback = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct ResumeTransformFeedbackPayload();
    #region Public API
    /// <summary>Records begin transform feedback with supplied inputs only; native validation remains at execution.</summary>
    public void BeginTransformFeedback(GpuTransformFeedbackRef feedback, CapturePrimitive primitive)
    {
        RequireRecording();
        if (capture || feedback.IsEmpty || !Enum.IsDefined(primitive)) throw new InvalidOperationException("Invalid or nested capture."); capture = true; paused = false;

        int payload = payloadBeginTransformFeedback.Add(new(transformFeedbackReferencesArguments.Add(feedback), primitive));
        headers.Add(new(CommandOperation.BeginTransformFeedback, payload));
    }
    /// <summary>Records end transform feedback with supplied inputs only; native validation remains at execution.</summary>
    public void EndTransformFeedback()
    {
        RequireRecording();
        if (!capture) throw new InvalidOperationException("No recorded capture is active."); capture = paused = false;

        int payload = payloadEndTransformFeedback.Add(new());
        headers.Add(new(CommandOperation.EndTransformFeedback, payload));
    }
    /// <summary>Records pause transform feedback with supplied inputs only; native validation remains at execution.</summary>
    public void PauseTransformFeedback()
    {
        RequireRecording();
        if (!capture || paused) throw new InvalidOperationException("Capture is not running."); paused = true;

        int payload = payloadPauseTransformFeedback.Add(new());
        headers.Add(new(CommandOperation.PauseTransformFeedback, payload));
    }
    /// <summary>Records resume transform feedback with supplied inputs only; native validation remains at execution.</summary>
    public void ResumeTransformFeedback()
    {
        RequireRecording();
        if (!capture || !paused) throw new InvalidOperationException("Capture is not paused."); paused = false;

        int payload = payloadResumeTransformFeedback.Add(new());
        headers.Add(new(CommandOperation.ResumeTransformFeedback, payload));
    }
    #endregion
    #region Internal API
    /// <summary>Interprets this operation family through the existing immediate context.</summary>
    internal void ExecuteCapture(RHICommand command, RHICommandContext context)
    {
        switch (command.Operation)
        {
            case CommandOperation.BeginTransformFeedback:
            {
                var value = payloadBeginTransformFeedback.Get(command.Payload);
                context.BeginTransformFeedback(transformFeedbackReferencesArguments.Get(value.feedback), value.primitive);
                context.recordedCapture = true;
                return;
            }
            case CommandOperation.EndTransformFeedback:
            {
                var value = payloadEndTransformFeedback.Get(command.Payload);
                try { context.EndTransformFeedback(); } finally { context.recordedCapture = false; }
                return;
            }
            case CommandOperation.PauseTransformFeedback:
            {
                var value = payloadPauseTransformFeedback.Get(command.Payload);
                context.PauseTransformFeedback();

                return;
            }
            case CommandOperation.ResumeTransformFeedback:
            {
                var value = payloadResumeTransformFeedback.Get(command.Payload);
                context.ResumeTransformFeedback();

                return;
            }
            default: throw new InvalidOperationException("Unexpected operation family.");
        }
    }
    #endregion
}
