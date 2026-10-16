# Transient lighting precision evaluation

Status: the user selected practical highlight containment instead of widening transient
formats. The approved policy below supersedes the Float32 implementation proposal at the
end of this evaluation. Allocation formats remain unchanged; earlier numerical results
are retained as evidence of the tradeoff rather than a requirement to preserve every peak.

## Approved highlight policy

Freeze P from the previous valid automatic exposure multiplier or the current manual seed,
with the retained minimum scale 7.147944e-6 and finite reciprocal/conversion arithmetic.
Do not lower P using the frame's estimated maximum specular response, or force P to the
persistent-cache scale C=1/16. This keeps a single exceptional highlight from reducing
precision across unrelated faint pixels. C and persistent incident/diffuse encoding remain
independent of camera exposure.

Retain format-specific scene containment at each narrowing destination: [0,65504] for
binary16 scene RGB and the common safe packed-channel ceiling 64512 for packed composition.
Postprocessing keeps its existing scene boundary handling. Finite values within range are
unchanged. Exceptional scene values may clip and lose energy/chromatic detail; scene NaN
components use finite neutral fallback and infinities are contained. This is a deliberate
rendering policy, not proof that all physical arithmetic or additive blending is incapable
of overflow. Invalid persistent/cache/history data must continue to be rejected, without
turning contained payloads into valid history publications.

Keep finite nonnegative source-envelope metadata and coherent frame/calibration checks.
That metadata detects participating source changes and retains compatible source history;
it no longer certifies an unclipped frame or controls the scale. Replace the old specular
coefficients with estimates for the selected response, without presenting them as universal
storage guarantees. Do not add a reflection-ray firefly threshold to ordinary direct light.
No emitter-size, material-roughness, source-calibration or display-grading change is included.

Validate CPU/GPU scale agreement, exceptional-highlight containment, separate faint-pixel
retention at the same camera exposure, invalid-input rejection and unchanged cache/history
validity. Live appearance remains separately required before accepting the broader task.

Focused implementation validation passed 146 distinct checks, with no failures or skips;
production 450 / fixture 528 shader catalogs pass. A GPU test uses the actual compensated
grazing response and scene encoding: its extreme highlight clamps to 65504 while a separate
faint pixel stays nonzero and bit-identical at P=0.001, for source metadata zero or Float32
maximum. Invalid metadata rejects before publication; overflowing/NaN automatic history
falls back to the manual seed. Persistent/history invalidity tests retain strict rejection.
Receipt: `artifacts/MicrofacetEnergy/CameraContainmentValidation.md`. The broader task's
nine registered-runtime fixture publication failures were subsequently resolved using a
real camera-owned freeze; the 52-test handoff batch passes after correcting its missing
persistent decode. No live appearance acceptance or frame-time result is claimed by these checks.

## Why the previous lossless-storage requirement conflicted with narrow formats

Height-correlated Smith visibility restores reflection that the previous masking and
denominator approximation suppressed. Modest integrated energy does not imply modest
peak radiance: a sharp grazing lobe can have a large peak over a very small solid angle.
The directional-albedo compensation is about 1.10 in the nominated smooth grazing case;
the extreme peaks are not a thousand-fold compensation multiplier.

The selected lookup and local numerical envelopes suggest rounded response coefficients
of 40 million per unit punctual intensity for opaque surfaces, 42 million for normalized
liquid surfaces, and 3 times disk radiance for finite emitters through 0.04 radians.
Float32 shader validation and full composition accounting remain required. The old
66,560 opaque and 200,000 liquid punctual coefficients cannot be reused.

Twenty co-located 44-lumen white point sources, at admitted attenuation one, can exceed
2.7 billion nits in an extremely smooth grazing reflection. The previous 16,000 stored-light
budget would require scale below 5.9e-6, whereas the retained faint-light retention floor
is 7.147944e-6. This is a supported-source counterexample, not a normal-scene performance
or appearance measurement. Reproduction and mathematical assumptions are recorded in
`artifacts/MicrofacetEnergy/SelectedLookupAndStorage.md`.

## Boundaries that carry specular scene light

| Owner | Current storage | Wider candidate / issue |
|---|---|---|
| DirectLightingTargets | Three RGBA16F array layers | RGBA32F array; the current allocation shares a format across diffuse, specular and emission |
| PBRCompositeRenderer | R11F_G11F_B10F | RGBA32F scene composition; packed precision is the current retention bottleneck |
| SceneColorAllocation primary color | RGBA16F | RGBA32F; native forward/late contributions and presentation handoff use this image |
| WaterRefractionScene source color | Full-resolution RGBA16F | RGBA32F to retain background scene light |
| WaterRefractionScene reduced color | Reduced-resolution RGBA16F | RGBA32F; keep depth/metadata formats independent |
| RetainedPostprocessRenderer luma RGB | Full-resolution RGBA16F | RGBA32F; RGB carries scene color, not only a luminance statistic |
| BloomRenderer levels and scratch | RGBA16F | Qualify RGBA32F throughout extraction, filtering and accumulation |
| Engine OIT accumulation | Three RGBA32F layers | Already wide; requalify additive headroom and resolve destinations |

This is a source inventory, not proof that changing these allocations alone is sufficient.
Destination clamps, pipeline target signatures, imported image admission, filtered history,
copies and postprocessing arithmetic must agree with each selected format. In particular,
`VgePostStoredLight` currently limits values to binary16 range. Widening textures while
retaining that limit would continue to clip highlights. UI/display conversion and bounded
material channels must retain their own contracts.

Persistent atmosphere, celestial source textures, incident-light probes and diffuse-only
SurfaceCache targets do not carry receiver-dependent grazing specular peaks. Their formats
should not be widened merely because transient surface reflection changes. Particle source
buffers and shaft images require separate bounds; they are not automatically part of the
specular format promotion.

## Allocation cost of a direct format promotion

RGBA16F to RGBA32F adds 8 bytes per texel; packed RGB to RGBA32F adds 12 bytes per texel.
At 1920x1080, one full-resolution RGBA promotion adds 15.82 MiB and the packed composition
promotion adds 23.73 MiB. At 3840x2160 these costs are 63.28 and 94.92 MiB respectively.

Promoting all three direct layers, primary color, full-resolution refraction source and luma
RGB, plus packed composition, adds approximately **118.65 MiB at 1080p**, or **474.61 MiB
at 4K**. This excludes reduced refraction color, optional allocation lifetimes and bloom.
The six-level bloom pyramid uses two images per level starting at half resolution; promotion
adds about **10.55 MiB at 1080p**, or **42.18 MiB at 4K**, subject to integer size rounding.
These are logical image payloads, not measured driver residency or bandwidth. Larger
formats increase traffic; no frame-time result is claimed.

## Historical alternatives evaluated before policy selection

Simply lowering the minimum frame scale while leaving intermediate binary16/packed images
would erase faint lighting. Promoting the complete scene-light chain permits a smaller
scale, but the old floor must be replaced by a measured retention criterion. An alternative
is to retain that floor and raise only the wide-target budget while separately bounding
remaining narrow-target contributions. That needs distinct destination constraints; raising
the shared 16,000 budget globally would invalidate the existing narrow-target guarantees.

Float32 preserves a much wider exponent range, but its mantissa is finite. Faint and bright
features at separate pixels can survive when a tiny contribution added to an enormous
highlight at the same pixel rounds away. Qualification must distinguish those cases and
must not promise arbitrary simultaneous precision or arbitrary source magnitudes.

These alternatives were superseded by the approved containment policy above. Keep
destination saturation and invalid-publication handling; these are safeguards, not evidence
that the old bounds remain conservative or all physical peaks can be preserved.

## Focused precision results and superseded Float32 proposal

The 104 CPU storage checks in `artifacts/MicrofacetEnergy/StoragePrecision.md` reuse the
existing warm-torch reference RGB (0.272479, 0.186037, 0.133421) and moonless background
0.0008/pi nits, including a 1% receiver. Float32 round trips retain these separate pixels
and a 13.566-billion-nit highlight at scales from 1e-5 through 1e-12, with maximum relative
error below 1.45e-7. At scale 1e-6 packed RGB loses the torch completely; binary16 gives
10.65% blue-channel error, and both narrow formats lose the moonless background.
These are CPU format-precision results; actual GPU filtering and storage remain unqualified.

Adding the faint terms to the same 13.566-billion-nit pixel loses them even in Float32.
The Float32 spacing there is about 977 nits. Pre-scaling cannot restore those co-located
terms; preserving separate dark terrain pixels is the relevant retention requirement.

The straightforward proposal is to promote the scene-light boundaries inventoried above
to RGBA32F, retain the 16,000 frame budget, and qualify a lower frame-scale floor through
1e-12. Keep existing destination safety handling, but use each actual destination's range.
Recompute bounds with the new lobe coefficients, retain all required composition terms,
and reject sources outside the qualified scale/arithmetic domain. Do not change persistent
formats, source intensities, material roughness, metering, camera exposure or display grading.

This trades approximately 129.2 MiB at 1080p / 516.8 MiB at 4K, plus reduced refraction
storage, for simple and coherent precision ownership. A more selective promotion may
reduce this cost but needs separate narrow/wide destination budgets and validation;
changing only the specular output would leave packed composition as a precision bottleneck.
Runtime implementation of this proposal requires explicit approval beyond the approved
evaluation, followed by GPU correctness and cost measurements.
