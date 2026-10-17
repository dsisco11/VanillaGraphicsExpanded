# Rendering enablement

VGE uses System.Reactive for persistent, change-driven render enablement. The PBR composition
root owns one `PipelineEnablement` lifetime and shares it with its rendering consumers.
Render callbacks read cached values; settings and eligibility predicates execute when their
inputs change.

## Generic integration

`Rendering/Enablement/CachedObservable<T>` connects a source once for its owning lifetime.
Rx `DistinctUntilChanged` suppresses repeated values and `Replay(1)` supplies the latest value
immediately to late subscribers. Its `Value` property reads the same cached result without
evaluating upstream sources. Sources must synchronously provide an initial value. Consumers own
their subscriptions; disposing the cache disconnects its upstream computation. Reads and new
subscriptions after disposal are rejected.

`NativeSettingObservable.Observe` converts a typed native setting watcher into an observable.
It captures the setting at subscription, owns watcher removal and ignores callbacks after
unsubscription. `RenderThreadDispatch` applies notifications immediately on its construction
thread and queues notifications from other threads through the client's main-thread queue.
Disposal prevents queued notifications from entering a retired pipeline. These owners must be
constructed and disposed on the client rendering thread.

Derived policies use ordinary Rx operators such as `Select` and `CombineLatest`, wrapped in a
`CachedObservable<T>` when their result is shared or read synchronously. Sources and predicates
contain no GPU allocation policy. Renderers own their resources and subscribe to conditions for
enable/disable transitions.

## PBR inputs and conditions

`PipelineEnablement` owns one native watcher for each of `ssaoQuality`, `godRays`, `bloom` and
`fxaa`. Independent cached native sources feed their relevant conditions and a combined native
graphics snapshot. Changes to unrelated native controls do not trigger AO or HZB predicates.

`LiveConfigReload.Applied` signals a complete configuration application after the normal
configuration sanitization boundary. The matching client lifetime captures postprocessing and
local-exposure parameters in one immutable record. Related fields commit together, so a single
configuration application cannot expose intermediate combinations to an effect predicate.
Code that changes configuration directly must publish through `LiveConfigReload.NotifyAll`.

The active LumOn shader generation and actual volumetric-cloud ownership have independent
notifications. Pending LumOn configuration does not select the running shader generation.
Requested cloud mode does not establish ownership before a functioning draw is admitted.

| Condition | Determining inputs |
| --- | --- |
| Ambient occlusion | Native SSAO quality greater than zero |
| Shaft occlusion | Native shaft quality greater than zero |
| Depth hierarchy | Ambient occlusion OR active LumOn generation OR owned volumetric clouds |
| Bloom | Native bloom AND positive VGE bloom strength |
| Shaft radiance | Shaft occlusion enabled AND positive VGE shaft strength and limit |
| Local exposure | Applied VGE local-exposure enabled setting |

The depth renderer receives the combined condition through `DepthHierarchyEnablement`.
AO and shaft occlusion retire resources when their condition turns off. Postprocessing owns
subscriptions which retire bloom, shaft radiance and spatial exposure on their disabled
transitions. The camera uses the shared local-exposure notification to invalidate automatic
exposure history. Standalone renderer construction owns a private source lifetime; normal PBR
composition supplies the shared owner explicitly.

## Render boundaries and resource lifetime

Persistent enablement does not certify that a draw can execute. Native
`DoPostProcessingEffects` is a transient screen-pass gate and remains a draw-boundary check.
Current camera/frame identity, depth availability, shader readiness and framebuffer validity
also remain the responsibility of the relevant renderer. A skipped frame does not disable a
feature or release its persistent resources.

Enabled producers still publish fresh results for each eligible frame. Resize, world teardown
and shader reload independently invalidate their resources and publications. They do not modify
the persistent setting conditions. Composition disposes consumer subscriptions before the shared
source owner, which detaches native/static notifications and cancels queued callbacks.
