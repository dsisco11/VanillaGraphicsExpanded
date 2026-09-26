# Representative base color validation

The CPU estimator now uses a 5%-per-tail luminance-trimmed mean of linear RGB samples. A
256-bin histogram preserves whole RGB contributions, including fractional boundary-bin weights.
This is a robustness change, not a performance optimization. It suppresses sparse extreme pixels
while retaining more minority-color information than a single luminance-median pixel.

## Focused verification

Release tests: **22 passed, 0 failed**, recorded in
`artifacts/BRDFFaces/representative-base-color-final.trx` and
`artifacts-representative-base-color-final.log`.

Coverage includes sparse bright/dark outliers, balanced red/blue populations, single pixels,
uniform and transparent textures, alpha-threshold equality and equal weighting, deterministic
elongated-image sampling, periodic 1024-square stripes and alpha masks, fractional 6144-pixel
strata, invalid inputs, and cache identity/persistence. Cache algorithm version 2 produces a
different key from the previous arithmetic estimator.

Two initial elongated-image assertions targeted the superseded fixed-offset sampler. They were
updated to uniform strata when deterministic jitter was introduced; the final selection above
passes with the final continuous equal-area stratification implementation.

## Color comparison

Four installed game textures were decoded from
`G:/Vintagestory/assets/survival/textures/block/`. Each is 32 by 32 pixels. These are representative
examples, not a visual acceptance sample or a statistical survey of game materials. Values below
are linear RGB. The arithmetic column executes the previous implementation copied read-only
from Git HEAD, including its SIMD conversion approximation; the median reference sorts accepted
pixels by linear luminance and averages the central pair.

| Texture/population | Previous mean | Luminance median | Trimmed mean |
|---|---|---|---|
| stone/agedbrick/andesite1.png | .0835, .1038, .1224 | .0792, .0887, .0976 | .0797, .0998, .1183 |
| stone/flint.png | .1404, .1381, .0924 | .1329, .1301, .0897 | .1370, .1347, .0898 |
| plant/moss.png | .0590, .0524, .0058 | .0537, .0497, .0068 | .0580, .0517, .0073 |
| metal/mesh1.png | .0917, .0375, .0206 | .0855, .0369, .0223 | .0905, .0379, .0217 |
| 90% blue, 10% red | .1000, 0, .9000 | 0, 0, 1 | .0556, 0, .9444 |
| 92% gray, 4% white, 4% black | .2394 per channel | .2159 per channel | .2159 per channel |

The minority-red example shows that trimming still attenuates legitimate minority colors; it
does not remove them as aggressively as the median. Histogram boundary bins approximate exact
quantiles. Color differences also include replacing the old SIMD sRGB approximation with the
lookup-table conversion, so they cannot all be attributed to tail trimming alone.

## Processing cost

A small Release console harness warmed each candidate for 2,000 calls, then executed one ABBA
block with 10,000 calls per segment. Both candidates use their default 4,096-sample budget and
the same already-decoded input. This measures only estimator CPU time: no image decoding, disk
cache, GPU work, game startup or frame timing. It is a bounded microbenchmark, not an independent
multi-run performance qualification. Reported numbers average the two matching segments.

| Input | Previous estimator, microseconds | Trimmed estimator, microseconds |
|---|---:|---:|
| andesite1, 32 square | 0.486 | 3.576 |
| flint, 32 square | 0.465 | 3.574 |
| moss, 32 square | 0.433 | 3.456 |
| mesh1, 32 square | 0.435 | 1.885 |
| repeated stone, 1024 square | 18.208 | 34.972 |

The new estimator is slower than the previous SIMD arithmetic mean. The measured extra cost is
roughly 1.5–3.1 microseconds per small texture in this sample. Sampling positions differ for the
large image, so its comparison is equal-budget rather than identical sampled texels. The old
large-image ABBA segments vary noticeably (20.292 versus 16.123 microseconds); avoid treating
the ratio as a precise deployment prediction.

Reproduction inputs, the retained previous estimator and the temporary harness are under
`artifacts/BaseColorComparison/`; final raw output is `artifacts-base-color-comparison-final.log`.
No game process was launched. In-game material appearance remains user-run validation.
