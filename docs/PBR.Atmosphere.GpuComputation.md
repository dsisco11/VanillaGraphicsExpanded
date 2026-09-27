# Atmospheric GPU computation

`AtmosphereBackend` selects compute once per world/shader generation. Admission uses `GpuSupport`:
compute shaders, SPIR-V ingestion, shader-storage buffers, at least 64 invocations and X workgroup
size, 192 X workgroups, three SSBO bindings, and a 196672-byte storage block. Shared scratch uses
two arrays of 64 `vec3` values, below the compute specification's minimum shared-memory allowance.
Unsupported devices retain the asynchronous TensorPrimitives CPU implementation. Initialization or
execution failure logs its cause once and switches that owner to CPU, retaining the previous display.
A shader reload permits a fresh capability/backend selection.

## Transport and scheduling

Three asset-backed compute programs are compiled by the normal SPIR-V build and use declared shader
contracts, `GpuComputePipeline`, `GpuShaderStorageBuffer`, `GpuFence`, and `GpuQueue<Vector4>`:

- `atmosphere_scattering.csh`: one 64-lane workgroup per source-table cell; lanes integrate angular
  rays and reduce source and isotropic feedback before computing S/(1-F). Each render update submits
  at most 64 cells and only after the previous batch fence signals. Quality budgets and the uniform
  ground albedo of 0.1 match the CPU reference.
- `atmosphere_sky.csh`: one invocation per sky texel, using the completed table with the same squared
  altitude interpolation, 24 view segments and 12 solar segments as CPU transport.
- `atmosphere_lighting.csh`: row-major reduction of that sky into environment and horizon illumination,
  plus direct solar irradiance and local extinction. The small reduction is serial to retain CPU ordering.

Storage barriers order source batches, sky sampling, and the lighting reduction. A buffer-update
barrier precedes the output queue's completion fence. Zero-time polling admits further work; no
positive-duration GPU wait runs in production. The source cache depends on quantized weather and
quality; sun and observer movement reuse it. Source construction requires 8/32/72/128 submissions
at quality 0/1/2/3, followed by sky/integral submission and completion consumption. These are lower
bounds on render updates, not promises of frame time or completion latency.

## Ownership and publication

One immutable request owns each admitted generation. Quantization matches CPU transport: sun components
at 1/256, altitude at 25 metres and cloud cover at 0.05. Work finishes before the newest request is
admitted, avoiding starvation from continuously changing inputs. Completed source storage is private
to the backend. Dimensions, sun, medium and quality cannot mix across its dependent passes.

After the final fence signals, `GpuQueue` maps the bounded output containing four lighting vectors and
the sky. The owner copies it into the existing immutable `AtmosphereLighting` snapshot. The existing
render publication uploads its sky before exposing the matching lighting and texture ID. This retains
CPU consumers and existing publication tests; it deliberately includes a small GPU-to-CPU readback
and sky re-upload, rather than adding a second texture-ownership contract. Neither partial source
tables nor incomplete output reaches the display. Until completion the prior snapshot stays active.

Maximum GPU buffer payload is 327808 bytes: 64-byte parameters, 131072-byte source table and
196672-byte output. Driver/program storage is additional. The queue reserves output without a CPU upload; each
completed snapshot owns one immutable CPU sky array (up to 196608 bytes). The displayed RGBA16F
sky texture is additional. There is no persistent per-ray transfer array.

Shader reload disposes the pending backend and its programs, preserving the displayed sky and
lighting until a new generation completes. World teardown also clears the displayed resources and
replaces the owner. GPU resources retire through existing resource abstractions; discarded owners
cannot publish later. CPU fallback cancellation follows its existing worker lifetime.

## Validation

The focused tests load the real packaged SPIR-V using production shader/resource abstractions.
Complete default-quality skies and all four shared lighting vectors are compared to CPU transport
at noon, haze, twilight and 99 km altitude, including every sky channel. Selected source cells at
all four quality budgets are compared against the scalar angular integrator; additional black and
white ground cases exercise its reflection/feedback boundary. The full-sky per-channel tolerance is
`1e-4 + 0.01 * abs(reference)`; source-cell vector error uses `1e-4 + 0.01 * length(reference)`.
These bounds allow different floating-point transcendental evaluation and angular reduction order;
they are numerical comparisons, not a physical accuracy claim.

Tests also cover source reuse across sun motion and sub-bucket weather jitter, weather invalidation,
quality changes during admitted work, odd sky dimensions, pending disposal/replacement, capability
rejection, failed shader initialization selecting asynchronous CPU fallback, and GPU-only queue
preparation bounds. Maximum-quality GPU publication checked all 49152 sky floats for finiteness.
Full quality-2/3 CPU sky tables were not compared; their source transport uses bounded cell references.

The Debug receipts passed 124 distinct tests: 94 existing atmosphere cases, ten GPU computation and
lifecycle cases, six source-table comparisons, 13 queue cases and one optional measurement. Four
older optional measurements were skipped. Evidence is in `artifacts/AtmosphereGpu/atmosphere.trx`,
`gpu-final.trx`, `lifecycle-measurements.trx`, `reload-environment.trx` and `final-selection.trx`.
Four initial source-fixture allocation failures in the broad receipt were corrected and superseded
by all six passing source comparisons. The actual render-owner reload/reset test verifies retained
display identity on reload and cleared display state on world reset, with pending backend retirement.

### Matched transport costs

The initial Debug-build receipt `artifacts/AtmosphereGpu/lifecycle-measurements.trx` records three warmed ABBA blocks with separate
CPU/GPU input-sequence counters, so both owners receive identical sun/weather inputs and cache policy.
The device was an RTX 4090 / OpenGL 4.3.0 NVIDIA 591.86, with .NET 10.0.12. Debug timings are retained
as preliminary evidence; production Release measurements are recorded separately below.
The test waits on actual GPU query completion between updates; these waits belong to the measurement
harness, not the production callback. Program creation/linking precedes timing. Six samples per path:

| Default-quality workload | CPU transport wall time | GPU elapsed sum | CPU time inside GPU updates | Harness completion wall time |
| --- | ---: | ---: | ---: | ---: |
| Source table plus sky/integrals | 725.896–811.931 ms | 0.693–0.702 ms | 0.163–0.472 ms | 0.958–4.552 ms |
| Cached source, sky/integrals only | 13.031–13.841 ms | 0.325–0.345 ms | 0.104–0.535 ms | 0.447–3.950 ms |

The CPU baseline is the current TensorPrimitives lookup at matching budgets, not the older scalar
implementation. Absolute timings from different prior sessions are not comparable. GPU timings are
query sums around individual updates, not wall time between frame submissions. Host timing includes
submission and final immutable readback creation, but excludes harness query waits and final sky
texture upload by the render service. Driver scheduling introduces observable wall-time variation.

Default source rebuilds consumed ten update calls (eight source batches, sky/integrals, completion);
cached-source refreshes consumed two. At a steady 60 render updates per second, those imply about
150 ms and 16.7 ms respectively from first admission to publication when every dependency is ready
on the next update. The tight headless completion timings above do not include that frame cadence.

One maximum-quality run measured its first source batch at 1.573 ms, then the remaining generation
at 382.764 ms summed device time and 425.887 ms harness wall time. The largest individual dispatch
was 8.332 ms. This is a material frame-budget cost despite bounded work and is not a recommendation
to enable maximum quality by default. Its 130-update completion requires at least about 2.15 seconds
at 60 updates per second. No matched full quality-3 CPU benchmark was run.

Live game appearance, shader-link startup cost and contention with a full terrain workload remain
unmeasured here. No game process was launched.

### Release production candidate

`artifacts/AtmosphereGpu/release.trx` passed all 30 targeted GPU computation, source-reference and
queue tests, including the measurement. This build uses the production optimized SPIR-V profile;
the numerical tolerances above also pass with those optimized binaries. On the same RTX 4090,
NVIDIA 591.86 and .NET 10.0.12, the matched warmed ABBA Release results were:

| Default-quality workload | CPU TensorPrimitives wall | GPU elapsed sum | CPU time inside GPU updates | Harness completion wall |
| --- | ---: | ---: | ---: | ---: |
| Source table plus sky/integrals | 123.947–177.799 ms | 0.721–0.778 ms | 0.147–1.073 ms | 0.951–2.327 ms |
| Cached source, sky/integrals only | 1.987–5.106 ms | 0.334–0.361 ms | 0.111–0.235 ms | 0.520–1.377 ms |

Update counts remain ten and two respectively. The full quality-3 GPU run measured its first batch
at 1.217 ms, then the remaining generation at 343.697 ms device time and 436.459 ms harness wall
time, with a maximum individual dispatch of 3.345 ms. This replaces the preliminary Debug figures
for production-cost assessment; it still measures an otherwise idle headless GPU, excludes linking
and final display upload, and does not simulate terrain contention or frame cadence.
