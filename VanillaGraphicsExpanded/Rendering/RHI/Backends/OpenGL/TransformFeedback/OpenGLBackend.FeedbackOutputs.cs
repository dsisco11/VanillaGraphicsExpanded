using System;
using System.Runtime.ExceptionServices;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Prepares the authored capture ranges on source changes without acquiring resource ownership.</summary>
internal sealed partial class OpenGLBackend
{
    #region Private
    /// <summary>Retains only derived native selections and a failed-refresh result for the capture owner.</summary>
    private sealed class FeedbackOutputs
    {
        private readonly OpenGLBackend backend;
        private bool dirty;
        private Exception? failure;
        internal readonly RHITransformFeedbackDescription Description;
        internal readonly int[] Names;

        /// <summary>Observes the actual sources after creation has established their initial ranges.</summary>
        internal FeedbackOutputs(OpenGLBackend backend, RHITransformFeedbackDescription description, int[] names)
        {
            this.backend = backend;
            Description = description;
            Names = names;
            foreach (var output in description.Outputs)
                output.Range.Buffer.RequireResource().ObservePreparation(this, Withdraw);
        }

        /// <summary>Rebuilds all related output selections together; a failure stays withdrawn until another change.</summary>
        internal void Prepare()
        {
            if (dirty)
            {
                dirty = false;
                failure = null;
                try
                {
                    for (int index = 0; index < Names.Length; index++)
                    {
                        var range = Description.Outputs[index].Range;
                        Names[index] = checked((int)backend.RequireRange(range.Buffer, range.Offset, range.Length).ResourceId);
                    }
                }
                catch (Exception error) { failure = error; }
            }
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        /// <summary>Withdraws derived ranges without performing native work inside change delivery.</summary>
        private void Withdraw() => dirty = true;
    }
    #endregion
}
