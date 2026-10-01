# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-10-01

### Added
- **Quick Export as PNG** (`Ctrl + Alt + G` or `Alt + F, G`): Instant flattened 32-bit PNG export with full alpha transparency preserved.
- **Quick Export as JPEG** (`Ctrl + Alt + J` or `Alt + F, J`): Instant flattened JPEG export over a clean white background at 95% quality.
- **Smart Destination Pathing**: Saves automatically alongside the active document (e.g. `image.pdn` &rarr; `image.png` / `image.jpg`), falling back to Desktop for unsaved documents.
- **Automated Patcher CLI (`QuickExportPatcher.exe`)**:
  - Automatic Paint.NET installation discovery (current folder, parent folder, `Program Files`, and Windows Registry).
  - Transparent UAC elevation support when installed in protected system directories.
  - Safe backup creation (`paintdotnet.orig.dll`) before applying any modifications.
  - Integrated unpatcher (`[2] Restore original Paint.NET`) to cleanly revert Paint.NET to vanilla state at any time.
  - Update detection: Refreshes vanilla backup automatically if Paint.NET has updated since initial patching.

### Compatibility
- **Tested and verified with Paint.NET version 5.1.12** on Windows x64.
