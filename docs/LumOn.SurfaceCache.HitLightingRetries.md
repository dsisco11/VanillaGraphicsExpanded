# Dependency-aware Surface Cache hit lighting retries

The indirect producer previously stopped at the first missing hit-lighting lookup and discarded the
geometric sample. Its next scheduled attempt traced new rays even if the known hit surface still had
no initialized lighting. CPU fallback likewise discarded complete geometry when its asynchronous
cache query was unready.

Both paths can now retain complete geometric samples and resolve them with cache-only queries.
Ordinary indirect dispatches skip retained texels; initial seeding and direct refresh do not consult
the retained list. Failed or delayed lookup never clears displayed lighting or ages its history.

## Complete geometric samples

The GPU selects bounded capture opportunities using the existing rotating page/texel selection
pattern. An admitted texel records each geometric hit, including hits whose lighting is already ready,
and finishes its original ray batch after a lighting miss. Sky rays remain in the original denominator
but need no hit query. A later geometry failure invalidates the whole retained batch; it cannot turn
partial traversal into a completed estimator. Non-admitted texels keep their existing early exit.

Only a batch with completely resolved geometry and at least one missing hit-lighting result is
retained. Missing material, unavailable geometry and traversal exhaustion are not retained as valid
hits. They keep their existing fallback or bounded traversal-retry paths.

GPU descriptors retain integer hit cells, axial normals, local fractions and block identity. CPU
fallback contributes the same descriptors after its collision worker has completed. Descriptors and
cross-boundary collections are immutable. The GPU capture uses an existing buffer abstraction and
fence; render-thread collection polls before mapping and never overwrites an unknown completion.

## Dependency progress and validity

Each retained sample observes the exact hit pages' physical/virtual ownership, chunk generation,
capture revision, capture availability and per-page lighting publication serial. A first lookup
closes the race between original capture and CPU collection. After an unready lookup, only changes
to **missing** hit dependencies wake that sample. Publication on unrelated pages or already ready
hits does not cause another lookup.

Every retry queries all hits together from one published lighting snapshot, rather than mixing old
successful radiance with newly available radiance. Once all answers are finite and valid, the full
sample uses the existing estimator, temporal weighting and combine/swap publication path.

An initially absent page may become resident, captured and seeded without invalidating the retained
ray. Once a captured hit identity has been bound, eviction, slot reuse, recapture or generation change
rejects that retained sample. Source-page identity is checked as well. Scene identity/invalidation,
world edit epoch and lighting resource revision are checked before query/commit; CPU samples also
recheck their observed chunk objects before those operations.

These geometry invalidations are conservative: an unrelated edit, or a dirty notification whose
geometry cannot be proven unchanged, can retire retained samples and allow ordinary tracing again.
The implementation does not retain an exact dependency for every cell along a GPU ray. Temporary
producer unavailability can postpone queue retirement, but cannot publish a delayed result; ownership
is checked when processing resumes. World leave and resource replacement explicitly retire ownership.

## Work and storage bounds

| Resource or operation | Bound |
| --- | --- |
| Retained texel batches | 32 |
| Hits per retained texel | 64 |
| Retained hit descriptors | 2,048 (128 KiB of query records, plus bounded metadata) |
| GPU capture opportunities | Up to 16 texels per admitted dispatch, reduced by free retained slots |
| GPU capture/suppression storage | 67,360 bytes: 544-byte header/list and sixteen 4,176-byte records |
| Outstanding capture fences | One |
| Retained lighting lookup | One outstanding batch, at most 64 hit queries; at most one submission per producer frame |
| Hit dependency observations | At most 2,048 per processing frame |
| Retention age | 256 producer frames |
| Combined delayed commits | At most 16 texels, sharing existing page publication credit with CPU fallback |

CPU chunk dependency arrays retain the fallback's existing maximum of 512 observed chunks per
sample batch. Arrays are shared when entries originate from the same fallback result. Checks occur
only before lookup and commit, not for every waiting sample every frame. The original CPU worker
and its initial cache-query bounds remain unchanged; the retained lookup allowance is additional.

Ready entries are selected round-robin. Unchanged missing dependencies sleep, and expired entries
return to ordinary tracing so permanently missing pages cannot monopolize retention. A full queue
rejects new retention and disables additional capture work while keeping suppression active. Other
texels remain eligible. Suppression metadata is refreshed only after prior capture ownership retires;
there can be a short handoff delay before newly collected or retired entries appear in that list.

An admitted GPU sample can trace more rays than the former first-miss early exit, but never more than
its configured ray count. At most sixteen such samples are admitted per capture. These bounds are
not a frame-time or in-game convergence guarantee.

## Diagnostics and validation

Self-check counters expose current `hitPending`, cumulative `hitRetained`, `hitQueries`,
`hitCommitted` and `hitRejected`, plus CPU-origin `hitCpuRetained` and `hitCpuCommitted`.
Commit counters count validated submissions through the normal publication path. GPU ray counters
do not include cache-only queries, so useful completion can increase without additional ray counts.

Subagent validation passed **175 distinct cases**, including 30 new retry cases. The focused suite
passed 98 cases. Another 74 existing regression cases passed, and
the final nine GPU/runtime cases passed, including the added geometry-invalidation case. Coverage
includes complete multi-ray retention, cache-only completion without increased ray counts, direct
refresh beside suppressed texels, storage canaries, incomplete geometry rejection, missing page
residency/initial seeding, explicit CPU-origin retention and completion, and resource lifetimes.

Two test setup assumptions were corrected without changing production behavior: CPU fallback needs
supported air beside the source for initial direct seeding, so the fixture marks only interior air
unsupported; geometry invalidation may make the producer temporarily unavailable, so the test waits
for obsolete ownership rejection on resumed processing instead of requiring immediate queue removal.
The implementation review also corrected cursor adjustment when removing/pruning entries to preserve
the next entry's round-robin turn.
The final pure suite passed 21 cases, including both removal and pruning cursor regressions.

Receipts are `artifacts/surface-hit-retry-{focused,regression,final,fairness,build}.log` and matching
TRX files under `artifacts/TestResults`. The initial regression log retains its corrected fixture
failure; the final runtime log records the passing replacement. Production build passed with zero
warnings/errors; shader generation compiled 105 stages and 250 variants. Gameplay acceptance remains
separate. The 19 pre-existing broader consumer/runtime failures remain tracked by their own task.
