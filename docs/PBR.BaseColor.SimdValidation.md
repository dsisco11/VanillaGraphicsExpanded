# Base-color histogram SIMD validation

The scalar, Vector128 and Vector256 kernels use identical sample selection, lookup-table RGB
conversion, alpha filtering and ordered histogram writes. Each SIMD kernel handles incomplete
vectors with the scalar tail. Histogram collisions remain ordered per sample.

Production retains scalar dispatch: measurements did not establish a consistent benefit from
automatically choosing the widest available vector. The requested specialization methods remain
available for future measured tuning.

## Correctness

The focused Release selection initially passed **33/33** tests, including 11 direct histogram
comparisons. Cases cover random colors, alpha thresholds, 1/3/7/9/257-sample tails, repeated
destinations, transfer-function byte boundaries, and reduced stratified sampling. Every histogram
count and accepted-sample count is compared. RGB sums are subsequently checked for exact equality,
because the kernels preserve LUT values and accumulation order.

After retaining scalar production dispatch and strengthening RGB comparisons, the final focused
selection passed **33/33**, including **11/11 exact kernel-parity cases**.

Receipts are under `artifacts/BRDFFaces/`: `albedo-simd.trx`, `albedo-simd-exact.trx`, and the final
selection `albedo-simd-final.trx`. No game process or GPU test suite was needed.

## Bounded CPU comparison

The temporary Release harness `artifacts/AlbedoKernelComparison/` links the actual three kernel
implementations. It uses the same decoded game textures as `PBR.BaseColor.Validation.md`, 2,000
warmup calls per kernel, then 20,000 calls per segment in scalar/vector/vector/scalar order. Each
call clears equivalent histogram arrays. Timings include kernel accumulation and that clearing;
they exclude histogram reduction, image decoding, caching and game startup.

Both vector widths were hardware accelerated on this machine. Microseconds per call:

| Input | Scalar paired with 128 | Vector128 | Scalar paired with 256 | Vector256 |
|---|---:|---:|---:|---:|
| andesite1, 1024 samples | 6.839* | 3.231 | 3.679 | 3.231 |
| flint, 1024 samples | 3.800 | 3.314 | 5.393* | 3.235 |
| moss, 1024 samples | 3.007 | 3.036 | 3.035 | 3.290 |
| mesh1, 1024 samples/424 accepted | 1.914 | 2.330 | 1.722 | 2.039 |
| repeated stone, 4096 of 1024-square image | 38.049 | 53.204 | 40.967 | 43.067 |

`*` These scalar pairs were unstable: andesite measured 9.975 then 3.703; flint measured 6.951
then 3.836. Do not interpret these inflated means as SIMD speedups. The large-image vector256
segments also varied substantially (37.075 and 49.060). This single bounded measurement is enough
to reject an unconditional performance claim, not to establish a cross-machine dispatch policy.

SIMD modestly helped some opaque contiguous examples and regressed alpha-heavy or reduced-sampling
examples. It still performs scalar LUT reads and collision-safe writes, and rejected-alpha lanes
can incur vector processing that the scalar path skips. Raw evidence:
`artifacts-albedo-kernel-comparison.log`.
