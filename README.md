![QuickLook icon](https://user-images.githubusercontent.com/1687847/29485863-8cd61b7c-84e2-11e7-97d5-eacc2ba10d28.png)

# QuickLook CAD Preview

An open-source [QuickLook](https://github.com/QL-Win/QuickLook) plugin for
previewing DWG and DXF drawings. It uses the MIT-licensed ACadSharp reader and
a project-owned 3D preview renderer. It does not depend on the commercial
CADImport engine.

## Supported files

- `.dwg` — AutoCAD drawing
- `.dxf` — AutoCAD Drawing Exchange Format

## Preview controls

- **Left-drag:** rotate the 3D view
- **Right- or middle-drag:** pan
- **Mouse wheel:** zoom
- **Double-click** or press **R:** reset to a fitted isometric view

For DWG files with an embedded thumbnail, the thumbnail appears while the
interactive model view is prepared in the background.
When extreme isolated coordinates dominate a large drawing's extents, the
initial view fits the main geometry instead. Geometry is not removed, but
outliers may lie outside the initial view.

## Civil 3D and compatibility

Civil 3D drawings can contain application-specific objects. The plugin renders
available proxy graphics and common CAD geometry, but it does not interpret
Civil 3D surfaces, alignments, corridors, or profiles as native Civil 3D
objects. Entity support and visual fidelity can differ from AutoCAD; external
references, specialized fonts, and some paper-space settings may be missing.
Large scenes are limited to 250,000 geometry elements to keep previews bounded.

## Build and installation

See [INSTALLATION_GUIDE.md](INSTALLATION_GUIDE.md) for installing the
`.qlplugin` package and building from source. Architecture details and known
limits are in [CHANGES_AND_ARCHITECTURE.md](CHANGES_AND_ARCHITECTURE.md).

## License

Plugin source is GPL-3.0-or-later. Third-party dependencies and their licenses
are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
