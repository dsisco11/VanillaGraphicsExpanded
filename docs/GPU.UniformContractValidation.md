# Uniform test consolidation

The user-approved coverage audit supersedes the broad reflection suite described in the historical
record below. `LumOnUniformTests`, its critical-name inventory, linked-interface helper, assertion
helper, custom SPIR-V reader and reader tests have been removed.

Current coverage lives with existing owners:

- `ShaderContractBindingTests` covers all nine former default programs plus the existing world-probe
  debug program. It checks initial active sampler units and actual linked UBO slots before rebinding.
- `LumOnUboBindingTests` retains its frame/world-probe coverage but now checks original bindings;
  assigning the expected binding before the assertion would conceal the relevant failure.
- `ProbeParameterPackingTests` writes distinct values through production temporal/filter setters in
  both orders and checks their raw packed components. This catches swapped components or clobbering
  that a getter round-trip or a vec4-at-offset reflection check would miss.
- Existing functional tests continue to check actual parameter effects, and SPIR-V inventory tests
  retain load/link coverage. No claim is made that every CPU field has exhaustive behavioral coverage.

The removed suite provided some structural shape checks, but its 124 labels were not 124 independent
behavioral guarantees. It did not execute setters, identify same-shaped fields semantically, or prove
which vector component the shader reads. The consolidation deliberately removes those limited static
checks rather than maintaining a custom binary parser solely for them.

Consolidation validation: Debug build and 81 focused/functional cases passed; Release build and
26 focused binding/packing cases passed, with no skips. Receipts: `artifacts/UniformConsolidation-*`
logs and matching TRX files. The final location-enumeration and partial-load cleanup edits were
made after the initial Debug build. The delegated run then hit the account usage limit; final Debug
restoration/revalidation and review confirmation were not completed. The initial review found no
remaining deleted-helper references and confirmed the packing test exercises production setters.

## Historical refactor evidence

The following describes the earlier implementation and measurements, not the current test inventory.

The LumOn uniform suite now validates the SPIR-V bytes selected by production contracts. It does
not read GLSL, expand imports, parse syntax trees, search macro names, or require debug names.
Production shader loading is unchanged.

## Assertion mapping

The former suite contained nine declaration/reporting cases, 124 critical-value rows and one
aggregate diagnostic report. Its executable workload linked 142 programs. The replacement has
nine grouped GPU cases, one per identical default program selection, with the same 124 named
critical values. Each case links once and aggregates failures from all critical values.

- Samplers: compare the binary variable's explicit location and binding with the owning contract;
  require critical samplers in the actual linked active-location set and check their initial unit.
- Packed values: map each semantic value to the layout written by `LumOnUniformBuffers` or the
  corresponding parameter UBO owner. Check the binary member offset, vector/matrix shape, scalar
  width and signedness, and matrix stride/column-major layout. Require the owning linked block.
  This checks layout, not whether an individual member is read by shader execution.
- Shader switches: use canonical typed shader-option declarations. These are not uniform slots.
  The selected binary is loaded and linked; existing functional branch tests retain responsibility
  for proving the option changes behavior. No macro-string fallback remains.
- General declarations: surviving numeric resource slots must belong to the contract. Noncritical
  declarations may be optimized out; they are not automatically required to be active. Critical
  values retain explicit presence checks rather than using the contract's diagnostic `Required`
  flag as a blanket optimization exemption.
- Diagnostics: each case reports its stage and critical-value counts. A failed group includes all
  collected named critical-value failures instead of stopping after the first failed row.

The driver interface is captured once per linked program, using numeric resource queries. The
checks do not consume the compatibility helper's contract-derived uniform-name dictionary.
A deliberately altered reflected sampler binding must fail against the unchanged contract and
linked program. This protects against accidentally checking expected metadata against itself.

The test-only SPIR-V reader supports the numeric instructions required for these interfaces and
ignores optional source/name records. It is not a complete SPIR-V validator. Synthetic tests cover
name-free resources, layouts, specialization IDs and bounded malformed-instruction rejection.
Program and shader handles remain owned by the individual test case and are released on failure.

## Validation

Subagent runs on 2026-09-25:

| Selection | Result |
| --- | --- |
| Original Debug uniform suite | 134/134 passed; 32.112s command wall |
| Final Debug uniform suite | 9/9 passed; 4.723s command wall |
| Release uniform and numeric-reader tests | 16/16 passed, no skips |
| Final Debug focused regression | 73/73 passed, no skips; 26s reported test duration |

The final regression includes temporal renderer (2), combine (11), atlas gather (18), SH9 projection
(17), upsample (9), grouped uniform (9) and numeric-reader (7) cases. The Release scan checked all
250 generated binaries: no `OpName`, `OpMemberName`, `OpString`, `OpLine` or `OpSource` instructions.
All critical resources remained present in both configurations; no optimization exemptions were
needed. Release checks include the deliberately changed-binding negative control.

The baseline and final timing commands used `FullyQualifiedName~LumOnUniformTests`, Debug,
`--no-build --no-restore`, one GPU test process at a time. Both cover the same nine default program
selections and 124 named critical values; xUnit case counts differ because assertions are grouped
and the redundant diagnostic-only linking is gone. This is a single baseline/final comparison,
not a controlled repeated benchmark or a claim about whole-suite or production performance.
An intermediate passing candidate took 6.183s before the final mutation assertion was added.

An initial validation failure identified built-in vertex output blocks being mistaken for buffer
resources. Restricting buffer checks to Uniform/StorageBuffer storage classes corrected it.
Final Debug and Release builds passed, and Debug assets were restored after Release validation.
Independent review verified all 124 old-to-new labels, CPU packing offsets, failure cleanup,
name-independent checks and assertion boundaries; review passed.

Receipts: `artifacts/uniform-contract-baseline.{json,log}`, `uniform-contract-final.{json,log}`,
`uniform-contract-release.log`, `uniform-contract-release-opcodes.json`,
`uniform-contract-regression.log`, corresponding build logs and TRX files under
`artifacts/TestResults/`. No full GPU suite or live-game validation was run for this test-only change.
