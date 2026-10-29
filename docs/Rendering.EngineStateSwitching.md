# Engine state integration

Supported calls through the installed legacy and OpenGL4 binding tables reach generated native
callbacks, independently of the calling assembly. `OpenGlStateCache` owns executed knowledge;
`RHIResourceManager` owns canonical resource facts and lifetime. `RHICommandContext` is the managed
rendering execution boundary.

## Native observation boundary

[OpenGLBindingInterception](../VanillaGraphicsExpanded/Rendering/RHI/Backends/OpenGL/Interception/OpenGLBindingInterception.cs)
installs callbacks in already loaded tables and participates in subsequent binding loads. The generator
reads the compiler-selected OpenTK reference to derive entrypoints, native signatures and saved-original
managed dispatch methods. OpenTK performs its normal managed argument marshalling before native dispatch.
Coverage therefore does not depend on static call-site rewriting, a caller scan, concrete generic
instantiations or whether an engine method was compiled before installation.

The same supported table calls from project code, the engine and other mods receive observation.
Other native libraries and independently cached driver pointers are outside this boundary. The
[API coverage](Rendering.GpuResourceHandles.ApiCoverage.md) defines supported GL/Core, EXT and ARB
families, aliases and explicit auxiliary-owner/legacy exclusions. Current engine call frequency does
not narrow those obligations.

## Execution and reconciliation

Generated callbacks forward original native arguments exactly once and preserve return/output values
and native errors. Cache-owned post-call methods under `State/Observation/Native` reconcile effects.
They never suppress, cancel, retry, defer a failure into the caller or substitute managed validation
for native rejection. Observer failures are contained and withdraw only affected uncertain facts.

Scalar state handlers declare compiler-checked domain withdrawal methods and optional acceptance
classifiers. Proven rejection preserves known facts; accepted effects publish established values;
uncertain outcomes withdraw affected knowledge. Context selection and originating-lifetime checks
prevent publication into a replacement context. Native acceptance rules remain domain-owned.

Managed operations use core OpenGL4 4.5 implementations through generated `OpenGLDriverCalls`,
bypassing callbacks while retaining their existing operation/publication owner. There is one cache
and one manager registry, with no duplicate native execution or metadata authority.

Bindings distinguish active texture selection, per-target slots, samplers, images and indexed buffer
ranges. Resource creation and configuration maintain original registrations, defaults and facts;
content-only writes do not become storage metadata. Reserved names, proxies, native-width arguments
and per-object deletion outcomes preserve their semantics. Recorded initial discovery and explicit
original-entry recovery retain their bounded query contracts; missing covered tracking is a defect.
See [resource ownership](Rendering.GpuResources.md).

## Lifetime and selective restoration

Installation retains original pointers and callback delegates in `OpenGLNativeSlot`. Uninstall disables
new observation and atomically restores only entries still pointing at this installation's callbacks.
Previously fetched callbacks remain callable through retained originals; an observer already admitted
may finish. Resource/cache destruction follows the existing rendering lifecycle rather than a callback
entry/exit counter or blocking uninstall protocol.

Actual context retirement and uncertain effects retain targeted knowledge withdrawal. Ordinary frame,
resize and publication callbacks do not purge valid intercepted state. Managed known-equal transitions
can suppress native work; intercepted external calls always execute.

RHI boundaries declare permitted pipelines and helper-state coverage before mutation. Selective
capture/restoration preserves engine state and original associations, rejects undeclared managed
changes and withdraws failed restoration fields. See
[boundary lifetime](Rendering.AuthoritativePipelineState.md#shader-ownership-and-boundary-lifetime).

## Verification boundary

Generator signature/compilation tests and native forwarding tests cover ABI, aliases, results and
failure containment. Headless regressions verify native effects/errors, exactly-once dispatch, selectors,
metadata publication, suppression, context replacement and uninstall. Global interception fixtures
serialize installation and restore their owned table entries during cleanup. Installed-client rendering,
package identity and live appearance require their separate qualification evidence.
