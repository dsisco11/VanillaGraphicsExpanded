# VanillaGraphicsExpanded

## Graphics Debugging with RenderDoc

This project includes launch configurations for debugging graphics with [RenderDoc](https://renderdoc.org/).

### Setup

1. **Install RenderDoc** from <https://renderdoc.org/>
2. **Set the `RENDERDOC_PATH` environment variable** using one of these methods:

   **Option A: Project-local `.env` file (recommended for VS Code)**

   - Copy `.env` to `.env.local` (or edit `.env` directly if not committing)
   - Set `RENDERDOC_PATH=C:\Program Files\RenderDoc` (adjust to your installation)

   **Option B: System environment variable**

   - Windows: `C:\Program Files\RenderDoc` (typical location)
   - Add as a system or user environment variable

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
   - **Command-line Arguments**: `--tracelog --addModPath "<path-to-mod>/bin/Debug/Mods" --addOrigin "<path-to-mod>/assets"`
4. Click **Launch**
5. Play the game and press **F12** to capture frames
6. Close the game to load captures in RenderDoc for analysis
