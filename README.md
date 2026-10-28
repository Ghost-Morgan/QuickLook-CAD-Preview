<div align="center">

<img src="https://user-images.githubusercontent.com/1687847/29485863-8cd61b7c-84e2-11e7-97d5-eacc2ba10d28.png" width="104" alt="QuickLook">

<h1>
  <span style="color:#ff1744">Q</span><span style="color:#ff9100">u</span><span style="color:#ffea00">i</span><span style="color:#00e676">c</span><span style="color:#00b0ff">k</span><span style="color:#651fff">L</span><span style="color:#d500f9">o</span><span style="color:#ff1744">o</span><span style="color:#ff9100">k</span>
  <span style="color:#00b0ff">CAD</span>
  <span style="color:#00e676">Preview</span>
</h1>

**A technicolor pit stop for DWG and DXF: peek, orbit, pan, zoom.**

🔴 **DWG** &nbsp; 🟠 **DXF** &nbsp; 🟡 **ORBIT** &nbsp; 🟢 **PAN** &nbsp; 🔵 **ZOOM** &nbsp; 🟣 **QUICKLOOK**

[![License: GPL-3.0-or-later](https://img.shields.io/badge/License-GPL--3.0--or--later-ff1744?style=for-the-badge)](LICENSE-GPL.txt)
![Platform: Windows](https://img.shields.io/badge/Platform-Windows-00b0ff?style=for-the-badge)
![CAD formats: DWG and DXF](https://img.shields.io/badge/Formats-DWG%20%2B%20DXF-7c4dff?style=for-the-badge)

[Project site](https://ghost-morgan.github.io/QuickLook-CAD-Preview/) · [Getting started](#getting-started) · [Features](#features) · [Build](#build-from-source) · [Documentation](#documentation)

</div>

---

Preview technical drawings in [QuickLook](https://github.com/QL-Win/QuickLook)
without opening a full CAD application. This project uses the open-source
[ACadSharp](https://github.com/DomCR/ACadSharp) reader and its own 3D renderer;
it does not require the former commercial CADImport engine.

> **Big drawing? No sweat.** Lightweight detail while you navigate; a richer
> render after you pause. Keep exploring without waiting for every entity at once.

## Features

| Interactive navigation | High-resolution previews | CAD-aware rendering |
| --- | --- | --- |
| Rotate, pan, and zoom the model directly in QuickLook. | Generate 1920 × 1080 cached previews for clear, readable drawings. | Preview common geometry, annotations, dimensions, hatch boundaries, and available proxy graphics. |

<div align="center">

🟥━━━━━━━━🟧━━━━━━━━🟨━━━━━━━━🟩━━━━━━━━🟦━━━━━━━━🟪

</div>

### Supported formats

- **DWG** — embedded thumbnails are shown immediately when available.
- **DXF** — model-space geometry is parsed and rendered.

### Preview controls

| Action | Control |
| --- | --- |
| Rotate | Left-drag |
| Pan | Right- or middle-drag |
| Zoom toward pointer | Mouse wheel |
| Reset to saved view | Double-click or press **R** |

The renderer prepares the interactive view independently from the image
preview. It uses reduced detail during navigation, then increases the detail
budget after you pause. Larger cached previews are generated at low priority
for drawings within the rendering budget and reused on later opens. The plugin
does not scan folders in the background.

> [!NOTE]
> Civil 3D surfaces, alignments, corridors, and profiles are not reconstructed
> as native objects. Available proxy graphics and supported CAD geometry can
> be previewed; unsupported entities, external references, specialized fonts,
> and paper-space plotting may differ from AutoCAD. Large scenes are bounded
> to 250,000 geometry elements.

## Getting started

1. Install [QuickLook](https://github.com/QL-Win/QuickLook/releases).
2. Download `QuickLook.Plugin.CADImport.qlplugin` from the
   [latest release](https://github.com/Ghost-Morgan/QuickLook-CAD-Preview/releases/latest),
   or build it from source.
3. In File Explorer, select the `.qlplugin` file and press **Spacebar**.
4. Confirm the QuickLook installation prompt.

See the [installation guide](docs/INSTALLATION_GUIDE.md) for manual install,
uninstall, and verification instructions.

## Build from source

On Windows, run from the repository root:

```powershell
git submodule update --init --recursive
.\Scripts\update-version.ps1
dotnet build .\QuickLook.Plugin.CADImport.sln --configuration Release
.\Scripts\pack-zip.ps1
```

The installable `QuickLook.Plugin.CADImport.qlplugin` is generated at the
repository root. Build outputs, editor settings, and the package are excluded
from source control. Releases include both the installable plugin and a
source-code ZIP.

## Documentation

- [Installation and uninstallation](docs/INSTALLATION_GUIDE.md)
- [Architecture and supported content](docs/CHANGES_AND_ARCHITECTURE.md)
- [Third-party notices](docs/THIRD-PARTY-NOTICES.md)

## Repository layout

| Path | Contents |
| --- | --- |
| `src/QuickLook.Plugin.CADImport/` | Plugin source and project |
| `src/QuickLook.Common/` | QuickLook.Common Git submodule |
| `docs/` | Installation, architecture, and licensing documentation |
| `Scripts/` | Versioning and package scripts |
| `Build/` | Local build output (generated) |

## License

Plugin source is licensed under
[GPL-3.0-or-later](LICENSE-GPL.txt). Third-party licenses are listed in the
[notices](docs/THIRD-PARTY-NOTICES.md).
