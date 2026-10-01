# PaintDotNet-QuickExport

> **One-click flattened PNG & JPEG export directly from Paint.NET's File menu.**

Paint.NET natively requires multiple clicks, flattening confirmations, format dialogs, and navigation steps just to export a flattened preview of a layered document. **PaintDotNet-QuickExport** integrates directly into Paint.NET's native File menu, adding instant, zero-dialog export options:

- **Quick Export as PN<u>G</u>** (`Ctrl + Alt + G`  or  `Alt + F, G`)
- **Quick Export as <u>J</u>PEG** (`Ctrl + Alt + J`  or  `Alt + F, J`)

---

## Features

- **Instant Flattening**: Composites all visible layers into a buffer using Paint.NET's native rendering engine and saves immediately.
- **Smart Path Detection**: Automatically saves alongside your active file (e.g. `MyProject.pdn` &rarr; `MyProject.png` / `MyProject.jpg`), or to your Desktop if the document hasn't been saved yet.
- **Alpha & Color Aware**:
  - **PNG**: Full 32-bit alpha transparency preserved.
  - **JPEG**: Automatically composites transparent layers over a clean white background and encodes at 95% quality.
- **Safe & Fully Reversible**: The patcher automatically preserves `paintdotnet.orig.dll` so you can restore your vanilla installation at any time with a single click.
- **Automated Patcher CLI**:
  - Auto-locates your Paint.NET installation (Current folder, Parent folder, `C:\Program Files\paint.net`, or Windows Registry).
  - Supports automatic Windows UAC elevation if Paint.NET is in `Program Files`.
  - Staging folder and side-by-side File Explorer opening fallback if you prefer manual copy/pasting.

---

## Quick Start / Usage

### Option 1: Using QuickExportPatcher (Recommended)

1. Download the latest release from [GitHub Releases](https://github.com/nhogle/paintdotnet-quick-export/releases) and extract `QuickExportPatcher.exe` (or build from source).
2. Run `QuickExportPatcher.exe`.
3. The tool will auto-detect your Paint.NET installation:
   - If located in `C:\Program Files\paint.net`, the tool will offer to elevate via UAC to install the patch cleanly.
   - Alternatively, it stages the files and opens Explorer windows to both folders for easy drag-and-drop.
4. Launch Paint.NET, open an image with layers, and check the **File** menu!

### Option 2: Restoring Vanilla Paint.NET (Unpatching)

Run `QuickExportPatcher.exe` and select option `[2] Restore original Paint.NET (Unpatch)`. Your original `paintdotnet.orig.dll` will be restored immediately.

---

## Repository Structure

```
paintdotnet-quick-export/
|-- PaintDotNet.QuickExport.sln            # Master solution file
\-- src/
    |-- PaintDotNet.QuickExport/           # Companion DLL plugin (QuickExport)
    |   |-- PaintDotNet.QuickExport.csproj (Outputs PaintDotNet.QuickExport.dll)
    |   \-- Exporter.cs                    # UI injection and image flattening logic
    \-- PaintDotNet.QuickExport.Patcher/   # Standalone automated patcher CLI
        |-- PaintDotNet.QuickExport.Patcher.csproj (Outputs QuickExportPatcher.exe)
        \-- Program.cs
```

---

## Technical Details: How It Works

Paint.NET v5.x runs on modern .NET 9 and uses Ahead-Of-Time (ReadyToRun) native compilation and strict menu item integrity checks. Standard plugin architectures only allow `FileType` and `Effect` plugins, neither of which can add hotkeyed actions to the root File menu.

To achieve seamless File menu integration, **PaintDotNet-QuickExport** utilizes a two-part hybrid design:

1. **Lightweight Binary Hook (`paintdotnet.dll`)**:
   - Injects a dynamic reflection hook at the end of `PaintDotNet.Menus.FileMenu.OnDropDownOpening`. This dynamically loads `PaintDotNet.QuickExport.dll` from the application directory without introducing brittle static assembly references.
   - Bypasses internal menu membership checks (`FileMenu.OnDropDownItemClicked`, `PdnMenuItem.OnDropDownItemClicked`, and `PdnMainMenu.OnItemClicked`), allowing standard `ToolStripMenuItem` instances to dispatch click events without throwing `InvalidOperationException: Invalid menu item detected`.
   - Preserves Win32 PE resources, GUI subsystem flags, and switches the CLR header to `ILOnly` to bypass stale native ReadyToRun offsets.
2. **Companion Library (`PaintDotNet.QuickExport.dll`)**:
   - Contains all the UI and export logic.
   - Dynamically inspects `AppWorkspace` and `ActiveDocumentWorkspace`.
   - Calls `curDoc.Flatten(surface)` to perform native multi-layer compositing.
   - Encodes to PNG or high-quality JPEG and writes directly to disk.

---

## Building from Source

### Prerequisites
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Paint.NET 5.x installed (required for reference assemblies)

---

### Option 1: Visual Studio 2022
1. Open `PaintDotNet.QuickExport.sln`.
2. Press **F5** (or **Ctrl+F5**) to build and launch the patcher.  
   *(The solution is pre-configured to launch the Patcher project automatically. You can switch the configuration dropdown to **Release** for an optimized build).*

---

### Option 2: Visual Studio Code
1. Open the repository root folder in VS Code.
2. **To Build:** Press **`Ctrl+Shift+B`** and choose **`Build (Debug)`** or **`Build (Release)`**.
3. **To Run:** Switch to the Run & Debug panel (**`Ctrl+Shift+D`**), select **`QuickExportPatcher (Debug)`** or **`QuickExportPatcher (Release)`**, and press **`F5`** (or **`Ctrl+F5`** to run without debugging).

---

### Option 3: Command Line (.NET CLI)
```powershell
# Build the entire solution (Debug)
dotnet build

# Build in Release mode
dotnet build -c Release

# Run the patcher directly
dotnet run --project src/PaintDotNet.QuickExport.Patcher
```

---

## Packaging a Release (.zip)

To create a standalone, distribution-ready release zip containing the single-file executable, documentation, and a SHA-256 checksum:

- **Via PowerShell:**
  ```powershell
  ./package.ps1
  ```
- **Via VS Code:** Press `Ctrl+Shift+P`, select `Tasks: Run Task`, and choose `Package Release (.zip)`.

The output package will be placed in `dist/QuickExportPatcher-v<Version>-win-x64.zip` alongside `dist/checksums.txt`.

---

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for release history and version notes.

---

## License

This project is licensed under the [MIT License](LICENSE).

---

## Acknowledgments

- [dnlib](https://github.com/0xd4d/dnlib) (0xd4d) - High-performance .NET module reader/writer used by the patcher for IL bytecode injection (MIT License).
- [Paint.NET](https://www.getpaint.net/) (Rick Brewster / dotPDN LLC) - The image editor this tool extends.

