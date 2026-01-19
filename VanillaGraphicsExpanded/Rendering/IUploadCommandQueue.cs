using System;

namespace VanillaGraphicsExpanded.Rendering;

internal interface IUploadCommandQueue
{
    int Count { get; }

    bool TryAcquireEnqueueSlot(out UploadEnqueueToken token);

    bool TryEnqueue(in UploadEnqueueToken token, in UploadCommand command);

    void ReleaseEnqueueSlot(in UploadEnqueueToken token);

    int Drain(Span<UploadCommand> destination);

    bool TryEnqueue(in UploadCommand command)
    {
        if (!TryAcquireEnqueueSlot(out UploadEnqueueToken token))
        {
            return false;
        }

        if (TryEnqueue(token, command))
        {
            return true;
        }

        ReleaseEnqueueSlot(token);
        return false;
    }
}
