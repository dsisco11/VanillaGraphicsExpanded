# Surface Cache update budget measurements

## Matched workload

`SurfaceUpdateBudgetMeasurementTests.MatchedLegacyAndSeparateBudgets` ran on an NVIDIA
GeForce RTX 4090, NVIDIA 591.86 / OpenGL 4.3, .NET 10.0.12. Separate diagnostics-off
and diagnostics-on runs each used three independent ABBA blocks comparing the
legacy four-page shared allowance with seed 8, direct 8 and indirect 4. Each leg
used a fresh captured 24-page voxel enclosure, warmed each operation eight times,
and performed 16 identical sweeps: 1,152 successful page statuses / 73,728 attempted
texels. Both sides used 64 texels per page, one ray, a 1,024-step ceiling, 512-block
distance and history limit 4.

Every sweep resets the 24 measured pages and explicitly waits outside the timed
interval. Seed therefore initializes those pages instead of returning early for
already-valid lighting. The instrumented legs assert 24,576 completed texels for
each of seed, direct and indirect, zero unchanged texels, and zero diagnostic
drops or read failures. Every indirect leg records 13,824 rays and 13,824 resolved
surface hits; each stage also completes 10,752 hidden texels. Diagnostics-off legs
report successful page statuses and attempted texels, not invented GPU completion
counter values. The matched instrumented workloads establish actual texel work.

The scheduling model seeds all pages, then services direct and indirect refresh.
Legacy refresh alternates four-page batches; the candidate submits indirect and
direct into separate work buffers before synchronously reading both results,
matching production dispatch ordering. GPU queries end before readback. Wall
intervals include dispatch, upload and synchronous work-record mapping. A bounded
diagnostic poll occurs after all maps and the test completion fence every simulated
frame; its wall cost is recorded separately. This keeps the diagnostic ring from
saturating. Any diagnostic polling within dispatch itself remains part of dispatch
wall cost.

## Results

Each table entry summarizes six legs per configuration. All instrumented legs
completed exactly 73,728 texels, and both modes produced 1,152 successful page
statuses. Simulated frames per complete sweep fell from 18 to 9 in both modes.

| Matched measurement per leg | Diagnostics off: legacy | Diagnostics off: separate | Diagnostics on: legacy | Diagnostics on: separate |
| --- | ---: | ---: | ---: | ---: |
| Median summed GPU interval | 9.80 ms | 9.20 ms | 23.31 ms | 17.32 ms |
| Median dispatch and readback wall time | 21.16 ms | 14.75 ms | 31.67 ms | 22.08 ms |
| Median explicit diagnostic poll wall time | 0.036 ms | 0.015 ms | 5.05 ms | 3.35 ms |
| Mean dispatch and readback wall per simulated frame | 0.073 ms | 0.108 ms | 0.113 ms | 0.155 ms |

Diagnostics are enabled by default. In that mode, mean candidate GPU costs were
12.69 microseconds per seed page, 11.31 per direct page and 21.84 per indirect page.
Eight direct pages therefore cost about 90 microseconds and four indirect pages
about 87 microseconds in this workload. This supports separate defaults of eight
direct and four indirect pages, rather than giving both stages the same allowance.
Eight seed pages cost about 102 microseconds and accelerate initial progress
independently of refresh service.

With diagnostics off, the candidate mean costs were 6.94, 6.27 and 11.62
microseconds per seed/direct/indirect page. GPU interval differences across the
three diagnostics-off ABBA blocks were +3.22%, -3.23% and -9.60%; wall differences
were -25.81%, -25.24% and -27.81%. With diagnostics on, GPU differences were
-28.87%, -23.40% and -23.44%, and wall differences were -35.53%, -31.17% and
-27.14%. The stronger instrumentation-on improvement includes fewer instrumented
dispatches; it must not be generalized into an instrumentation-independent shader
speedup. Separate budgets deliberately spend more work per frame to shorten the
modeled completion interval.

## Scope and reproduction

The fixture exercises real production lighting shaders and SSBO completion
readback. Its enclosed rays terminate quickly: the configured maximum distance
and traversal ceiling do not make this a worst-case long-ray measurement. It
does not include scheduler selection, reset/combine/carry-forward publication,
geometry upload, delayed CPU fallback or hit-dependency retries. The simulated
frames are a fixed-work service model, not a game frame rate or measured visual
convergence claim. Live diagnostics remain necessary for scene-specific tuning,
particularly with large pages or prolonged missing dependencies.

Set `NUGET_PACKAGES=C:\Users\Sisco\.nuget\packages`,
`VGE_RUN_SURFACE_BUDGET_MEASUREMENTS=1`, and
`VGE_SURFACE_BUDGET_MEASUREMENT_OUTPUT` to the desired JSON path, then run:

```powershell
dotnet test VanillaGraphicsExpanded.Tests/VanillaGraphicsExpanded.Tests.csproj --filter FullyQualifiedName~SurfaceUpdateBudgetMeasurementTests -p:NuGetAudit=false -p:RestoreIgnoreFailedSources=true --logger "trx;LogFileName=surface-update-budget-measurements.trx" --results-directory artifacts/TestResults
```

Receipts: `artifacts/surface-update-budget-measurements.json`,
`artifacts/surface-update-budget-measurements.log`, and
`artifacts/TestResults/surface-update-budget-measurements.trx`.
The corrected opt-in measurement passed in 17 seconds with zero failures or skips.
Compilation reported five existing unrelated test analyzer warnings.
