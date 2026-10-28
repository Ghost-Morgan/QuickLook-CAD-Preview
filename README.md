<div align="center">

<img src="https://user-images.githubusercontent.com/1687847/29485863-8cd61b7c-84e2-11e7-97d5-eacc2ba10d28.png" width="104" alt="QuickLook">

# QuickLook CAD Preview

**Fast, interactive DWG and DXF previews for QuickLook on Windows.**

[![License: GPL-3.0-or-later](https://img.shields.io/badge/license-GPL--3.0--or--later-blue.svg)](LICENSE-GPL.txt)
![Platform: Windows](https://img.shields.io/badge/platform-Windows-0078D4.svg)
![.NET Framework 4.6.2](https://img.shields.io/badge/.NET_Framework-4.6.2-512BD4.svg)
![CAD formats: DWG and DXF](https://img.shields.io/badge/CAD-DWG%20%7C%20DXF-2E8B57.svg)

[Getting started](#getting-started) · [Features](#features) · [Build](#build-from-source) · [Documentation](#documentation)

</div>

---

Preview technical drawings in [QuickLook](https://github.com/QL-Win/QuickLook)
without opening a full CAD application. This project uses the open-source
[ACadSharp](https://github.com/DomCR/ACadSharp) reader and its own 3D renderer;
it does not require the former commercial CADImport engine.

## Features

| Interactive navigation | High-resolution previews | CAD-aware rendering |
| --- | --- | --- |
| Rotate, pan, and zoom the model directly in QuickLook. | Generate 1920 × 1080 cached previews for clear, readable drawings. | Preview common geometry, annotations, dimensions, hatch boundaries, and available proxy graphics. |

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
preview. Larger cached previews are generated at low priority after opening a
drawing and reused on later opens. The plugin does not scan folders in the
background.

> [!NOTE]
> Civil 3D surfaces, alignments, corridors, and profiles are not reconstructed
> as native objects. Available proxy graphics and supported CAD geometry can
> be previewed; unsupported entities, external references, specialized fonts,
> and paper-space plotting may differ from AutoCAD. Large scenes are bounded
> to 250,000 geometry elements.

## Getting started

1. Install [QuickLook](https://github.com/QL-Win/QuickLook/releases).
2. Obtain `QuickLook.Plugin.CADImport.qlplugin` from a project release or build
   it from source.
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
from source control; attach the package to a GitHub Release when distributing
it.

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
