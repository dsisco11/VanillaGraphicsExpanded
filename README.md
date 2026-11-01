# VanillaGraphicsExpanded

## Development game data

All game launch profiles use `VGE_DATA_PATH` for settings, saves, logs, and mod
configuration. `VINTAGE_STORY` still identifies the game installation and the
assemblies used when building. On this workstation the development data directory
is `D:\CODE\VintageStory\VintagestoryAutomation\Data`, beside the repository.

Set the environment variable before starting your IDE. For Windows PowerShell:

```powershell
[Environment]::SetEnvironmentVariable('VGE_DATA_PATH', 'D:\CODE\VintageStory\VintagestoryAutomation\Data', 'User')
$env:VGE_DATA_PATH = [Environment]::GetEnvironmentVariable('VGE_DATA_PATH', 'User')
```

Restart already-running IDEs and terminals so their launches inherit the value.
On other systems, export `VGE_DATA_PATH` as an absolute path before starting the IDE.
The log tasks and development package script reject a missing or relative path.
The workspace Logs folder points to `../VintagestoryAutomation/Data/Logs`; update
that folder entry if you choose a different directory.

Debug and RenderDoc profiles add the current build output with `--addModPath`.
Keep that output directory out of the persistent `clientsettings.json` mod paths.
The install/uninstall tasks manage the development archive in
`VGE_DATA_PATH/Mods`. Uninstall that archive before using a debug profile to avoid
loading two copies of VGE. To test the installed archive, launch the game with
`--dataPath "<VGE_DATA_PATH>"` and omit `--addModPath` and `--addOrigin`.

The automation profile enables `boolSettings.multipleInstances`. Use separate
saves for concurrent game instances. These profiles select the data directory;
they do not automatically load a world or close the game on a timer.

## Timed automation

Build Debug, then run the Windows PowerShell 7 launcher from the repository root:

```powershell
./Run-Automation.ps1 -World TestWorld -SecondsAfterReady 30
```

The VS Code **Run automation** task builds Debug before running the same default
session. The script itself uses the existing build. It resolves `VINTAGE_STORY`
and `VGE_DATA_PATH` from explicit `-GamePath` / `-DataPath` arguments, then the
process environment, then `.env.local`, then `.env`. Local files accept literal
`KEY=value` assignments and quoted values; they are never executed as scripts.

Automation creates the client hidden, then shows it windowed behind existing
windows without requesting foreground focus.
Use `-Foreground` for normal activation, for example during first-time character
selection. This policy is scoped to the launched automation process. Debug builds
also build the small startup hook used to apply the policy before the game creates
its window; it stays outside the mod loader's search directory.

The selected save is `<VGE_DATA_PATH>/Saves/TestWorld.vcdbs`. A missing save is an
error unless `-CreateWorld` is supplied. For a first interactive setup run:

```powershell
./Run-Automation.ps1 -CreateWorld -Foreground -StartupTimeoutSeconds 600
```

Complete login and character selection if prompted. The server-side readiness
observer logs `[VGE.Automation] PlayerReady RunId=<id>` after character selection.
It starts observing at `PlayerNowPlaying` and waits for `EnumClientState.Playing`;
the installed 1.22.7 engine does not forward the public `PlayerReady` event to mods.
The launcher starts its wall-clock timer only after finding that run's marker.
Normal singleplayer launches log the same marker with `RunId=manual`; dedicated
servers do not register the observer.

Each run retains its logs in `<VGE_DATA_PATH>/Logs/Automation/<id>`. Startup has a
300-second default timeout. On completion or startup failure, the launcher asks
only its own client window to close and allows 60 seconds for normal shutdown.
`-ShutdownTimeoutSeconds` changes this limit. If closure fails, it reports the
remaining PID instead of killing the process. `-ForceOnTimeout` explicitly permits
forced termination of that process; forced closure still reports a failed run.

Do not open the automation profile manually while the script uses it. Concurrent
script runs against the same profile are rejected by an exclusive file handle.
Your ordinary game can keep running with its separate data directory. This does
not guarantee isolated performance measurements because both games share hardware.

## Graphics Debugging with RenderDoc

This project includes launch configurations for debugging graphics with [RenderDoc](https://renderdoc.org/).

### Setup

1. **Install RenderDoc** from <https://renderdoc.org/>
2. **Configure the RenderDoc path** in VS Code:
   - Open `.vscode/settings.json`
   - Set `renderdoc.path` to your RenderDoc installation folder
   - Default: `C:/Program Files/RenderDoc`

   For Visual Studio, set the `RENDERDOC_PATH` system environment variable instead.

### Using the Launch Profile

**Visual Studio:**

- Select **"Client (RenderDoc)"** from the debug profile dropdown
- Press F5 to launch

**VS Code:**

- Open the Run and Debug panel (Ctrl+Shift+D)
- Select **"Launch Client (RenderDoc)"** from the dropdown
- Press F5 to launch

The game will launch with RenderDoc capture enabled. Press **F12** (default) or **Print Screen** in-game to capture a frame.

### Alternative: Manual RenderDoc Workflow

If you prefer using the RenderDoc GUI directly:

1. Open **RenderDoc**
2. Go to **File → Launch Application**
3. Configure:
   - **Executable Path**: `<VINTAGE_STORY>/Vintagestory.exe`
   - **Working Directory**: `<VINTAGE_STORY>`
   - **Command-line Arguments**: `--tracelog --dataPath "<VGE_DATA_PATH>" --addModPath "<path-to-mod>/bin/Debug/Mods" --addOrigin "<path-to-mod>/assets"`
4. Click **Launch**
5. Play the game and press **F12** to capture frames
6. Close the game to load captures in RenderDoc for analysis

## Graphics Debugging with NVIDIA Nsight Graphics

The Nsight profiles launch the client through `ngfx.exe` using the **OpenGL Frame
Debugger** activity and wait for a manual capture triggered in Nsight.

### Setup

1. Install [NVIDIA Nsight Graphics](https://developer.nvidia.com/nsight-graphics).
2. Configure the installation root (not the `host` subdirectory):
   - **VS Code:** Set `nsightGraphics.path` in
     `.vscode/settings.json`. The default is
     `C:\Program Files\NVIDIA Corporation\Nsight Graphics 2026.3.1`.
   - **Visual Studio:** Set the `NSIGHT_GRAPHICS_PATH` system environment variable
     to that installation root, then restart Visual Studio.
3. Keep `VINTAGE_STORY` and the development data directory configured as described
   above.

### Using the Launch Profile

- **VS Code:** Select **Launch Client (Nsight Graphics)** in Run and Debug, then
  press F5. Like the existing RenderDoc launch, this builds the mod, tails the
  client log, opens `TestWorld`, and scans the log when the debug session ends.
- **Visual Studio:** Select **Client (Nsight Graphics)** and press F5. This uses
  the same data directory and mod output arguments as **Client (RenderDoc)**.

Use the Nsight Graphics UI to capture and inspect an OpenGL frame. These profiles
launch the graphics debugger; they do not attach a managed C# debugger to the game.

## Testing

This project includes CPU unit tests and GPU integration tests. The test suite uses **xUnit v3** with support for conditional test skipping when GPU resources are unavailable.

### Running Tests

By default the VGE project uses `UnitTests.runsettings`, which excludes GPU integration tests and diagnostic workloads. CPU tests that touch engine globals use a serialized collection without creating a GL context. Headless renderer scenarios are integration tests; in-game visual/runtime acceptance remains separate.

**Run combined VGE correctness tests locally (requires GPU):**

```bash
dotnet test VanillaGraphicsExpanded.Tests --settings VanillaGraphicsExpanded.Tests/AllTests.runsettings
```

**Run only non-GPU tests (CI environments without GPU):**

```bash
dotnet test VanillaGraphicsExpanded.Tests --settings VanillaGraphicsExpanded.Tests/UnitTests.runsettings --filter "Category!=GpuIntegration&Purpose!=Diagnostic"
```

**Run only GPU tests:**

```bash
dotnet test VanillaGraphicsExpanded.Tests --settings VanillaGraphicsExpanded.Tests/AllTests.runsettings --filter "Category=GpuIntegration&Purpose!=Diagnostic"
```

**Run explicitly selected diagnostics:**

```bash
dotnet test VanillaGraphicsExpanded.Tests --settings VanillaGraphicsExpanded.Tests/DiagnosticTests.runsettings --filter "Purpose=Diagnostic&FullyQualifiedName~BloomRadialDiagnosticsTests"
```

`AllTests.runsettings` selects combined correctness tests (`Purpose!=Diagnostic`); `DiagnosticTests.runsettings`
selects `Purpose=Diagnostic`. The runner combines explicit command-line filters with the settings filter;
select the matching settings file and include the purpose predicate when selecting individual classes.
Diagnostics requiring a capture or an additional workload opt-in
report a skip when it is missing; they never return a successful no-op. `Scenario=HeadlessRuntime` identifies
production renderer/lifecycle execution, independently of the CPU/GPU and diagnostic classifications.

Mixed workloads retain their numerical, scheduling and lifecycle assertions in ordinary correctness
methods. Separate diagnostic entries enable warmed timing samples, allocation telemetry and cost-only
native queries. Correctness execution retains required repeated updates and GPU completion, without
running those optional measurement brackets. CPU registry tests that construct resource wrappers use
the serialized CPU-state collection because wrapper construction observes the process-wide lifecycle service.

VGE persistence and export subjects use the existing in-memory filesystem provider. Immutable shader inputs
are frozen from the build manifest once; explicitly requested diagnostic captures/binary overrides are also
snapshotted once before their consumers read the provider. Missing inputs and bad capture lengths/digests
fail validation. Diagnostic reports and BMP/raw exports use memory sinks, not files at environment-selected
paths. Text reports and binary payload identities are emitted through test output. Required assembly loading and build/test-runner receipts retain their normal bootstrap I/O. These tests
verify serialization and replacement protocols, not operating-system filesystem atomicity.

`HeadlessGLFixture` enables the existing driver executable cache against one bounded in-memory
`ProgramBinaryStore` retained across cases. Its default follows the fixture across runner threads;
nested context setup does not reset it. A suite/case that must observe fresh specialization,
linking, reload or allocation uses `DriverProgramCache.UseStoreForTesting(null)` for that interval.
Cache-subject tests instead supply their own isolated store through the same scoped API. Explicit scopes
override the fixture default, follow awaited thread changes, and restore it on disposal. No policy reaches the production disk store
while a headless fixture is alive, including during teardown and context changes.

Production/component/compute shader helpers reuse digest-validated asset binaries. Combined
`ShaderTestHelper`, declared fixture-program loading and assertion compute programs use that same asset route.
Resource-free state shaders and source-authored numerical raster helpers use `TestShaderCache`
with exact expanded source, stage kind and driver/profile identity, storing bytes in the existing store.
The vertex-only particle color-feedback helper also includes its varying name and interleaved mode.
A hit creates a new native
program with fresh uniform state. Subject owners, binding tables and assertion results remain per case;
the GPU fixture retains production checker shader owners for repeated observations in its physical context.
Explicit stage integration and native creation/mutation subjects still execute their authored native
operations. The SPIR-V assignment and shader-define compile matrices explicitly bypass cached programs.

### Compact GPU assertions

`GPU/Assertions/GpuImageAssertions` compares complete selected image domains against independent CPU
expectations or a retained `GpuImageSnapshot`. It also provides finite/range/extrema/positive counters,
double sums and squared sums, channel ratios, coverage predicates and rank-permutation/slice checks.
Selections retain their original extent, mip, region, channels, explicit sparse coordinates and layers or
cube faces. Packed stencil comparisons change the sampling mode only on an owned GPU copy and retain
the source depth aspect and selector. The checker returns thirty-two words containing counters, moments and failure coordinates/rank identities; it does not download
image pixels. Keep existing local tuple reads and tests of readback APIs independent from this checker.

Generic/statistics, authored-predicate, projection and layer-conversion programs are declared test shader assets compiled by the normal
shader build. Authored predicates and projections select finite declared operations; changing numeric inputs
are data, rather than runtime shader source. The fixture retains production compute shader owners and
disposes them before their originating manager/context retires. Each observation explicitly supplies its
own resources, uniform bytes and initialized results; snapshots remain caller-owned within that context.
There is no additional test executable cache or global native-name cache. Explicit native compiler failure
subjects remain fresh native operations outside this ordinary checker preparation path.
Integer image outputs leave SSBO base/range bindings untouched, preserve program/texture/sampler/image
selectors, restore the checker uniform-buffer binding and restore pixel-pack state. A completion
marker and exact processed-texel count reject absent or partial dispatch; histogram validation and both
statistics reduction stages have independent completion evidence. Synchronous result transfer supplies
completion without a blanket `Finish`. Exact float comparisons equate signed zero and NaNs deliberately;
opposite infinities differ and subnormals retain their value. Tolerance is absolute and evaluated from the
represented values in double precision. Finite/range checks remain explicit independent predicates.
Double CPU expectations keep their original precision. Sparse comparisons prove the exact number of
authored components compared. Existing scalar probes use `GpuLocalRead` to read only their nominated texel.
Authored GPU predicates correlate retained images and independently count selected witnesses, preserving
conditional checks without a vacuous pass. Explicit mapped-source predicates can inspect cross-resolution
inputs at independently authored coordinates. `GpuImageTransform` authors history copies and projections
on the GPU; expectations remain separate. When an oracle requires float products with double sums,
products round into RGBA32F storage before the double reduction. `OrderedFloatSum` retains explicitly
required sequential float addition; `WeightedStatistics` retains independent double quadrature weights.
`PredicateAt` checks every nominated coordinate and layer, while `CompareRgba8` reconstructs normalized
metadata bytes before comparing independently authored values. Local float, integer and byte tuples use
the selected RHI readback region directly, preserving their original representations. Shared surface
fixtures publish their authored CPU cell/table inputs directly into production scene storage; history
seeding and later comparisons stay on the GPU.

`LayeredTestTexture.Create` uses compatible GPU copies or GPU nearest conversion with zero channel
expansion, including alpha. Immutable authored inputs/catalogs may be shared; mutable test subjects and
native resources retain explicit fixture ownership. Fault-injection construction callbacks require an existing disposal service so interrupted allocations can retire on the original context, without installing a manager implicitly. Routine nested helpers preserve intercepted state and
native errors; initialization, retirement and deliberate fault-injection boundaries retain their own contracts.

### GPU Test Categories

Tests are organized using the `[Trait("Category", "GpuIntegration")]` attribute:

- **GPU integration tests** - Require OpenGL 4.5+ and `GL_ARB_gl_spirv` (core in OpenGL 4.6)
  - Precompiled shader specialization and linking tests
  - Uniform validation tests
  - Render output tests
- **Non-GPU tests** - Run anywhere without graphics hardware
  - GLSL parsing/AST tests
  - Unit tests for CPU-side logic

### CI/CD Configuration

#### GitHub Actions / Azure DevOps (No GPU)

For CI runners without GPU access, exclude GPU tests:

```yaml
- name: Run Tests
  run: dotnet test VanillaGraphicsExpanded.Tests --settings VanillaGraphicsExpanded.Tests/UnitTests.runsettings --filter "Category!=GpuIntegration&Purpose!=Diagnostic"
```

#### Linux CI with Software Rendering (Mesa llvmpipe)

To run GPU tests on Linux CI without hardware acceleration, use Mesa's software renderer:

```bash
export LIBGL_ALWAYS_SOFTWARE=1
dotnet test VanillaGraphicsExpanded.Tests --settings VanillaGraphicsExpanded.Tests/AllTests.runsettings
```

This enables **llvmpipe**, Mesa's software OpenGL implementation, which supports OpenGL 4.5 and runs entirely on CPU. Note that software rendering is significantly slower than hardware rendering.
The selected Mesa build must also expose `GL_ARB_gl_spirv`. Linux CI is a separate environment;
Windows headless receipts do not qualify it.

**Requirements for Mesa llvmpipe:**

- Install Mesa drivers: `apt-get install mesa-utils libosmesa6`
- Ensure `LIBGL_ALWAYS_SOFTWARE=1` is set before running tests

### Test Infrastructure

The GPU test infrastructure includes:

- **HeadlessGLFixture** - Creates a hidden GLFW window with OpenGL 4.5 context; throws `SkipException` if context creation fails
- **ShaderTestHelper** - Loads and links built SPIR-V; separately processes imports for CPU source assertions
- **RenderTestBase** - Abstract base class for render tests with FBO setup and pixel readback utilities

Immutable shader, engine-source, schema and baseline inputs are captured from the build's
`fixture-inputs.txt` manifest once per test process. Removed files left in incremental output are
excluded. Fixture overrides, missing/corrupt assets and logical read notifications remain independent.
Cache and explicit file-loader tests use in-memory filesystem providers; these verify storage policy
and failure contracts, not the physical filesystem. `Scenario=HeadlessRuntime` identifies tagged
renderer scenarios; live appearance and runtime acceptance remain user-run checks.

## Normal/Height Sidecar Bake

VGE can bake a tileable height field (and derived normals) from albedo into a sidecar atlas during loading.

See [docs/NormalDepthBake.md](docs/NormalDepthBake.md) for details and tuning.

Async throttling knobs (in `ModConfig/vanillagraphicsexpanded-lumon.json`):

- `MaterialAtlasAsyncBudgetMs` / `MaterialAtlasAsyncMaxUploadsPerFrame` (material params)
- `NormalDepthAtlasAsyncBudgetMs` / `NormalDepthAtlasAsyncMaxUploadsPerFrame` (normal/height)

## PBR Explicit Texture Overrides

VGE mapping rules support optional explicit overrides for:

- Packed material params (`values.overrides.materialParams`)
- Packed normal+height (`values.overrides.normalHeight`)

See [docs/PBRMaterialRegistry.ModderGuide.md](docs/PBRMaterialRegistry.ModderGuide.md) for format, packing, and examples.

## Clouds

Native Off, Simple and Volumetric modes remain authoritative. Volumetric uses stored density and
progressive lighting. Cloud feature and resolution controls are documented in [Cloud rendering](docs/PBR.Clouds.md).
