using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering.Enablement;

/// <summary>Adapts native settings to typed Rx sources with explicit watcher ownership.</summary>
internal static class NativeSettingObservable
{
    #region Public API
    /// <summary>Captures an initial setting and forwards changes on the rendering thread until unsubscribed.</summary>
    internal static IObservable<T> Observe<T>(ISettingsClass<T> settings, string key, RenderThreadDispatch dispatch)
    {
        return Observable.Create<T>(observer =>
        {
            bool subscribed = true;
            // A queued callback checks the subscription as well as the dispatch lifetime.
            OnSettingsChanged<T> changed = value => dispatch.Run(() => { if (subscribed) observer.OnNext(value); });
            settings.AddWatcher(key, changed);
            observer.OnNext(settings[key]);
            return Disposable.Create(() => { subscribed = false; settings.RemoveWatcher(key, changed); });
        }).DistinctUntilChanged();
    }
    #endregion
}
