# Engine-owned shader compilation and recovery

VGE patches source with TinyAst but no longer performs a preliminary GPU compile.
The engine owns version selection, prefixes, stage compilation and linking. The
old `BuildValidationSource` path is removed.

Before publishing a patched program, VGE retains its original engine-loaded stage
bodies in memory. A failed engine compilation or link restores these bodies for
that program only and invokes its engine compiler once more. All stages are
restored together because their interfaces must agree. No global shader reload,
disk read or compilation of unrelated programs is requested.

Recovery consumes its opportunity before retrying, so a broken original cannot
cause recursion. A later source load establishes a fresh opportunity. Engine
prefixes remain owned by the engine and callers, including first-person variant
definitions applied after source loading.

Partial stage/program handles are retired through the GPU resource deletion
policy. Uniform lookup state and generated tessellation metadata are cleared
before retry; normal compilation hooks rebuild binding metadata. Source snapshots
are released on disposal. Failures are logged and reported in local client chat,
including failure of the original-source retry.

The engine can query uniforms after a failed compile and leave pending OpenGL
errors. Recovery records and consumes errors observed at that failure boundary
before retrying, preventing them from surviving a successful recovery and
triggering an unrelated render-stage error check. Successful first compilations
do not perform this error query.

This supersedes the temporary prefix/version validation solution documented in
PBR.ShaderValidation.Shadows.md. Runtime visual correctness after falling back to
an unpatched shader is not guaranteed; the error notification remains visible
even when the engine successfully compiles the original source.

Validation: Release build succeeded and all eight focused tests passed
(`artifacts/Transparency/shader-patch-recovery-fixed.trx`). Tests exercise the
installed engine's compile and link failures, source/prefix restoration, unrelated
program preservation, bounded retry, clean GL error state, error reporting and
the sky PatchId output. In-game shadow appearance remains user verification.
