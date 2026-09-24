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

## Graphics Debugging with RenderDoc

This project includes launch configurations for debugging graphics with [RenderDoc](https://renderdoc.org/).

### Setup

1. **Install RenderDoc** from <https://renderdoc.org/>
2. **Configure the RenderDoc path** in VS Code:
   - Open `.vscode/settings.json`
   - Set `vanillaGraphicsExpanded.renderdocPath` to your RenderDoc installation folder
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

## Testing

This project includes both unit tests and GPU-based shader tests. The test suite uses **xUnit v3** with support for conditional test skipping when GPU resources are unavailable.

### Running Tests

**Run all tests locally (requires GPU):**

```bash
dotnet test
```

**Run only non-GPU tests (CI environments without GPU):**

```bash
dotnet test --filter "Category!=GPU"
```

**Run only GPU tests:**

```bash
dotnet test --filter "Category=GPU"
```

### GPU Test Categories

Tests are organized using the `[Trait("Category", "GPU")]` attribute:

- **GPU tests** - Require an OpenGL 4.3+ compatible GPU and driver
  - Shader compilation tests
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
  run: dotnet test --filter "Category!=GPU"
```

#### Linux CI with Software Rendering (Mesa llvmpipe)

To run GPU tests on Linux CI without hardware acceleration, use Mesa's software renderer:

```bash
export LIBGL_ALWAYS_SOFTWARE=1
dotnet test
```

This enables **llvmpipe**, Mesa's software OpenGL implementation, which supports OpenGL 4.5 and runs entirely on CPU. Note that software rendering is significantly slower than hardware rendering.

**Requirements for Mesa llvmpipe:**

- Install Mesa drivers: `apt-get install mesa-utils libosmesa6`
- Ensure `LIBGL_ALWAYS_SOFTWARE=1` is set before running tests

### Test Infrastructure

The GPU test infrastructure includes:

- **HeadlessGLFixture** - Creates a hidden GLFW window with OpenGL 4.3 context; throws `SkipException` if context creation fails
- **ShaderTestHelper** - Compiles shaders with `@import` directive resolution and extracts compilation errors
- **RenderTestBase** - Abstract base class for render tests with FBO setup and pixel readback utilities

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
