# CAD preview architecture

This project is a QuickLook preview plugin for DWG and DXF drawings. It uses
the MIT-licensed ACadSharp reader and a project-owned renderer. The plugin does
not use or bundle the former commercial CADImport engine.

## Preview pipeline

1. DWG/DXF parsing and geometry extraction run on a worker thread.
2. For DWG files, the reader extracts the embedded drawing thumbnail first
   when one is present. QuickLook can display it while the interactive view is
   being prepared.
3. Model-space entities, block inserts, 3D faces, polylines, meshes, curves,
   and supported proxy graphics are converted into a bounded scene.
   When a few extreme coordinates dwarf the main drawing, the initial fit uses
   the dominant geometry rather than those outliers; this only changes the
   starting view and does not remove any drawing geometry.
4. The renderer projects the scene into a cached bitmap on a worker thread.
   Painting the cached frame keeps QuickLook responsive while the view is
   rotated, panned, or zoomed; view updates are coalesced.
5. Proxy mesh and shell geometry is used where a DWG contains it, including
   model transforms embedded in proxy graphics.

## Navigation

- Left-drag: orbit the 3D view.
- Right- or middle-drag: pan.
- Mouse wheel: zoom.
- Double-click or press `R`: reset to the default fitted isometric view.

## Supported content and limitations

- The plugin handles `.dwg` and `.dxf` files. PLT and CGM are not supported by
  the open-source renderer.
- It previews model-space geometry. Common lines, polylines, circles, arcs,
  ellipses, splines, faces, solids, meshes, block inserts, and available proxy
  graphics are rendered.
- Civil 3D objects are application-specific. When a drawing contains usable
  proxy graphics, those graphics can be previewed; the plugin does not
  interpret Civil 3D surface, alignment, corridor, or profile semantics.
- Unsupported entity types, missing fonts, external references, and complex
  paper-space/plot settings may not match AutoCAD. No redistributed Autodesk
  fonts or proprietary components are required.
- To keep large models bounded, scene construction stops after 250,000
  geometry elements and indicates when the preview is limited.

## Build and package

The project targets .NET Framework 4.6.2 to match QuickLook's plugin host.
ACadSharp is restored from NuGet and its MIT license is recorded in
`THIRD-PARTY-NOTICES.md`.

```powershell
powershell -ExecutionPolicy Bypass -File Scripts\update-version.ps1
dotnet build QuickLook.Plugin.CADImport\QuickLook.Plugin.CADImport.csproj --configuration Release
powershell -ExecutionPolicy Bypass -File Scripts\pack-zip.ps1
```

The pack script includes only the plugin assembly and QuickLook metadata. It
does not sweep old build outputs into the installable `.qlplugin` archive.

## Licensing

Plugin source is GPL-3.0-or-later. Third-party notices and licenses are listed
in `THIRD-PARTY-NOTICES.md`.
