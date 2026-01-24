using System;

namespace VanillaGraphicsExpanded.DebugView;

internal sealed class ActionDisposable : IDisposable
{
    private Action? disposeAction;

    public ActionDisposable(Action disposeAction)
    {
        this.disposeAction = disposeAction ?? throw new ArgumentNullException(nameof(disposeAction));
    }

    public void Dispose()
    {
        Action? a = disposeAction;
        if (a is null)
        {
            return;
        }

        disposeAction = null;
        a();
    }
}

