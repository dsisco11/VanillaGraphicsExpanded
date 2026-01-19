using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering;

internal sealed class StablePriorityUploadScheduler : IUploadScheduler
{
    private static readonly IComparer<UploadCommand> Comparer = new UploadCommandComparer();

    public void Sort(UploadCommand[] commands, int count)
    {
        Array.Sort(commands, 0, count, Comparer);
    }

    private sealed class UploadCommandComparer : IComparer<UploadCommand>
    {
        public int Compare(UploadCommand x, UploadCommand y)
        {
            int byPriority = y.Priority.CompareTo(x.Priority);
            if (byPriority != 0)
            {
                return byPriority;
            }

            return x.SequenceId.CompareTo(y.SequenceId);
        }
    }
}
