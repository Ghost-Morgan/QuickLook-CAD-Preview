# CAD preview architecture

This project is a QuickLook preview plugin for DWG and DXF drawings. It uses
the MIT-licensed ACadSharp reader and a project-owned renderer. The plugin does
not use or bundle the former commercial CADImport engine.

## Preview pipeline

1. DWG/DXF parsing and geometry extraction run on a worker thread.
2. For DWG files, the reader extracts the embedded drawing thumbnail first
   when one is present. QuickLook can display it while the interactive view is
   being prepared.
3. Model-space entities, block inserts, 3D faces, polylines, polyface and
   polygon meshes, curves, annotation text, multileader labels, dimension
   block graphics, hatch boundaries, and supported proxy graphics are
   converted into a bounded scene.
   When a few extreme coordinates dwarf the main drawing, the initial fit uses
   the dominant geometry rather than those outliers; this only changes the
   starting view and does not remove any drawing geometry.
4. The initial camera uses the active saved model-space viewport direction and
   twist when ACadSharp exposes them.
5. The renderer projects the scene into a cached bitmap on a worker thread.
   Painting the cached frame keeps QuickLook responsive while the view is
   rotated, panned, or zoomed; view updates are coalesced, and drag renders use
   reduced geometry sampling.
6. Proxy mesh and shell geometry is used where a DWG contains it, including
   model transforms embedded in proxy graphics.
7. Large drawings with embedded thumbnails show that image immediately. For
   source files at least 2 MB, the plugin creates a 1600x1000 preview at low
   priority and caches it under the user's local application data, with a
   128 MB cache limit. A cached image is used on subsequent opens. No background
   folder scanner is installed, so the first render starts after QuickLook
   opens the file.

## Navigation

- Left-drag: orbit the 3D view.
- Right- or middle-drag: pan.
- Mouse wheel: zoom toward the pointer.
- Double-click or press `R`: reset to the active saved view when available.

## Supported content and limitations

- The plugin handles `.dwg` and `.dxf` files. PLT and CGM are not supported by
  the open-source renderer.
- It previews model-space geometry. Common lines, polylines, circles, arcs,
  ellipses, splines, faces, solids, polyface/polygon meshes, text, multileader
  labels, dimensions, hatch outlines, block inserts, and available proxy
  graphics are rendered.
- Civil 3D objects are application-specific. When a drawing contains usable
  proxy graphics or exported mesh geometry, those graphics can be previewed;
  the plugin does not reconstruct native Civil 3D surface, alignment, corridor,
  profile, or ground-line semantics. When surface triangles are unavailable,
  only supported outlines can be shown.
- Unsupported entity types, missing fonts, external references, and complex
  paper-space/plot settings may not match AutoCAD; the preview renders model
  space rather than composing a plotted paper-space layout. No redistributed
  Autodesk fonts or proprietary components are required.
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
