# QuickLook CADImport Plugin - Detailed Changes & Architecture Review

This document provides a comprehensive breakdown of **all code modifications, architectural changes, build configurations, and packaging steps** implemented compared to the original upstream repository (`f065f79 Original File`).

Use this document to review the changes and highlight any issues, unexpected behaviors, or adjustments needed.

---

## 1. Summary of Changes Overview

| Area | Original State (`f065f79`) | Current State (`HEAD`) | Purpose / Impact |
| :--- | :--- | :--- | :--- |
| **CAD Loading** | Synchronous load on UI thread | Parses files on a worker and applies the loaded image on the UI thread | Keeps QuickLook responsive while preserving UI-thread rendering |
| **Panning & Zoom Redraw** | Re-rendered vector geometry on every mouse pixel movement or wheel event | Uses a captured frame during drag and scales it on every zoom invalidation, then redraws CAD geometry after wheel input pauses | Keeps pan/zoom responsive without limiting the frame rate |
| **Mouse Navigation** | Right mouse button drag only; double click ignored | Left, Middle (wheel drag), and Right drag for panning; double-click resets view | Matches AutoCAD / standard viewer UX conventions |
| **Engine Optimizations** | Original preview drawing settings | Recomputes fit extents around tiny origin proxy outliers and discovers installed Autodesk SHX font folders | Prevents Civil 3D sample geometry from shrinking into a corner and improves SHX label rendering |
| **Project Dependencies** | Required local `QuickLook.Common.csproj` to exist | Conditional fallback: uses `QuickLook.Common.dll` directly if submodule isn't cloned | Enables building without cloning the full QuickLook source tree |
| **Packaging Script** | Path relative issues when invoked outside `Scripts/` | Uses `$PSScriptRoot` for deterministic paths and `.FullName` in `Compress-Archive` | Generates valid `.qlplugin` regardless of current working directory |
| **Packaging & Metadata** | Missing release metadata config in archive | Includes `QuickLook.Plugin.Metadata.config` in build output and final archive | Required by QuickLook for plugin discovery and versioning |

---

## 2. File-by-File Detailed Code Changes

### 2.1. Project Build Configuration (`QuickLook.Plugin.CADImport.csproj`)
* **File:** `QuickLook.Plugin.CADImport/QuickLook.Plugin.CADImport.csproj`

#### What Changed:
```xml
<!-- BEFORE: Hard dependency on submodule project -->
<ProjectReference Include="..\QuickLook.Common\QuickLook.Common.csproj">
    <Project>{85FDD6BA-871D-46C8-BD64-F6BB0CB5EA95}</Project>
    <Name>QuickLook.Common</Name>
    <Private>False</Private>
</ProjectReference>

<!-- AFTER: Conditional project reference with standalone DLL fallback -->
<ProjectReference Include="..\QuickLook.Common\QuickLook.Common.csproj" Condition="Exists('..\QuickLook.Common\QuickLook.Common.csproj')">
    <Project>{85FDD6BA-871D-46C8-BD64-F6BB0CB5EA95}</Project>
    <Name>QuickLook.Common</Name>
    <Private>False</Private>
</ProjectReference>
<Reference Include="QuickLook.Common" Condition="!Exists('..\QuickLook.Common\QuickLook.Common.csproj')">
    <HintPath>..\QuickLook.Common\QuickLook.Common.dll</HintPath>
    <Private>False</Private>
</Reference>
```
* **Why:** In typical standalone plugin checkouts, the git submodule `QuickLook.Common` is not cloned or its project file is missing. The fallback allows compilation using the bundled binary `QuickLook.Common.dll`.

---

### 2.2. Plugin Lifecycle & Entry Point (`Plugin.cs`)
* **File:** `QuickLook.Plugin.CADImport/Plugin.cs`

#### What Changed:
1. **Loading & Busy Indicator in `View()`**:
   ```csharp
   context.ViewerContent = _rp;
   context.IsBusy = true;
   _rp.LoadCadFileAsync(path, () => context.IsBusy = false);
   ```
   The CADImport parse runs in the background; the loaded image is installed and rendered on the WPF dispatcher. Failed or unsupported files show an in-preview error message instead of leaving a blank viewer.
2. **Null-Safe Cleanup**:
   Added `if (_rp != null)` check prior to accessing `ActualWidth`/`ActualHeight` and disposing the control.

---

### 2.3. User Control & Async Wrapper (`RenderPanel.xaml.cs`)
* **File:** `QuickLook.Plugin.CADImport/RenderPanel.xaml.cs`

#### What Changed:
1. **Loading**:
   - Parses files on a worker thread and marshals image installation back to the WPF dispatcher.
   - Cancellation and disposal prevent a completed load from being applied after the preview closes.
   - Displays a clear message when parsing fails or the engine cannot create an image for the file.
2. **Disposal**:
   - Disposes the imaging engine and hosted CAD control during plugin cleanup.

---

### 2.4. CAD Rendering & Panning Engine (`CADImaging.cs` & Partial Files)

#### A. CAD Rendering Defaults (`CADImaging.cs`)
- Uses the original direct `CADImage.Draw(e.Graphics, ...)` path.
- Enables SHX and TrueType fonts and searches installed AutoCAD/TrueView font directories.
- Before fitting, detects tiny layer-0 proxy outliers close to the origin only when the rest of the model is clearly displaced and recomputing extents substantially reduces both dimensions. The proxies remain in the drawing and their original entity order is restored; only the fit extents change.
- Retains the original no-lineweight preview setting.

#### B. High-Performance Panning Engine (`CADImaging.cs`)
- **Problem in original code:** On every `MouseMove` event during drag, `cadImage.Draw(...)` was invoked. In complex drawings with 50,000+ entities, this caused severe UI lag (1–5 FPS).
- **Solution implemented:**
  1. Captures the control's already-rendered frame once at drag start.
  2. Blits the captured frame at the accumulated pan offset during the drag, avoiding repeated CAD geometry rendering.
  3. For zoom, scales a captured frame for each invalidation while wheel input continues, without a frame-rate timer or cap.
  4. Discards the temporary zoom frame after 120ms without wheel input and redraws using the original CADImport path.

#### C. Mouse Controls Expansion (`CADImaging.cs`)
- Enabled drag for:
  - **Left Mouse Button**
  - **Middle Mouse Button (Scroll wheel)**
  - **Right Mouse Button**
- Added double-click listener (`OnCADPictBoxMouseDoubleClick`) that calls `ResetScaling()` (fit to window).

#### D. Safe State Management (`CADImaging.Actions.cs` & `CADImaging.Properties.cs`)
- Added `ApplyLoadedImage(CADImage loadedImage, string fileName)` to cleanly replace an existing loaded image without memory leaks or race conditions.

---

### 2.5. Packaging Automation (`Scripts/pack-zip.ps1`)
* **File:** `Scripts/pack-zip.ps1`

#### What Changed:
```powershell
# BEFORE:
Remove-Item ..\QuickLook.Plugin.CADImport.qlplugin -ErrorAction SilentlyContinue
$files = Get-ChildItem -Path ..\Build\Release\ -Exclude *.pdb,*.xml
Compress-Archive $files ..\QuickLook.Plugin.CADImport.zip
Move-Item ..\QuickLook.Plugin.CADImport.zip ..\QuickLook.Plugin.CADImport.qlplugin

# AFTER:
Remove-Item $PSScriptRoot\..\QuickLook.Plugin.CADImport.qlplugin -ErrorAction SilentlyContinue
$files = Get-ChildItem -Path $PSScriptRoot\..\Build\Release\ -Exclude *.pdb,*.xml
Compress-Archive $files.FullName $PSScriptRoot\..\QuickLook.Plugin.CADImport.zip
Move-Item $PSScriptRoot\..\QuickLook.Plugin.CADImport.zip $PSScriptRoot\..\QuickLook.Plugin.CADImport.qlplugin
```
* **Why:** 
  - Using `$PSScriptRoot` guarantees execution from any current working directory.
  - Passing `$files.FullName` ensures `Compress-Archive` packs file paths correctly without nesting extraneous directory trees.

---

## 3. Current Build Pipeline & Artifacts

1. **Compilation Command**:
   ```powershell
   dotnet restore "QuickLook.Plugin.CADImport\QuickLook.Plugin.CADImport.csproj"
   dotnet build "QuickLook.Plugin.CADImport\QuickLook.Plugin.CADImport.csproj" --configuration Release
   ```
2. **Output Directory (`Build/Release/`)**:
   - `QuickLook.Plugin.CADImport.dll` (~2.27 MB, includes embedded `CADImport.dll` via Costura.Fody)
   - `QuickLook.Plugin.Metadata.config` (Plugin descriptor)
   - `QuickLook.Plugin.CADImport.pdb` (Debug symbols, omitted from final package)
3. **Packaging Command**:
   ```powershell
   powershell -ExecutionPolicy Bypass -File "Scripts\pack-zip.ps1"
   ```
4. **Final Package**:
   - `QuickLook.Plugin.CADImport.qlplugin` located at the root of the repo directory.

---

## 4. Potential Issues & Edge Cases for Review

When reviewing, consider whether any of these affect your specific use cases:

1. **Trial Watermark / Licensing**:
   - `CADImport.dll` is a third-party commercial library by CadSoftTools. In demo/unregistered mode, it may render a trial watermark banner on exported/rendered images.
2. **Line Weight Rendering**:
   - The plugin retains the original `IsShowLineWeight = false` preview setting.
3. **SHX Fonts**:
   - The plugin enables SHX/TrueType support and discovers installed Autodesk font folders. It does not redistribute Autodesk fonts; SHX labels still depend on the appropriate font being installed.
4. **Civil 3D objects**:
   - The bundled CADImport engine exposes generic proxy entities for this sample's Civil 3D objects. The plugin can show available proxy graphics, but it does not provide verified native surface/alignment/profile-view interpretation.
5. **Target Framework Compatibility**:
   - Built targeting `net462` to match standard QuickLook runtime assemblies. If running on a non-Windows or strictly modern .NET Core runtime, QuickLook requires its Windows-specific desktop bridge.
