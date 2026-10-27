# QuickLook CADImport Plugin - Detailed Changes & Architecture Review

This document provides a comprehensive breakdown of **all code modifications, architectural changes, build configurations, and packaging steps** implemented compared to the original upstream repository (`f065f79 Original File`).

Use this document to review the changes and highlight any issues, unexpected behaviors, or adjustments needed.

---

## 1. Summary of Changes Overview

| Area | Original State (`f065f79`) | Current State (`HEAD`) | Purpose / Impact |
| :--- | :--- | :--- | :--- |
| **CAD Loading** | Synchronous blocking load on UI thread | Asynchronous background loading (`Task.Run` + `CancellationTokenSource`) | Prevents QuickLook UI freeze when loading heavy `.dwg` / `.dxf` files |
| **Panning & Redraw** | Re-rendered vector geometry on every mouse pixel movement (high CPU lag) | Captures the already-rendered control once per drag and blits that frame while moving; redraws through the normal CAD path on release | Keeps dragging responsive without changing how CAD geometry is rendered |
| **Mouse Navigation** | Right mouse button drag only; double click ignored | Left, Middle (wheel drag), and Right drag for panning; double-click resets view | Matches AutoCAD / standard viewer UX conventions |
| **Engine Optimizations** | Default CADImport rendering settings | Retains the CADImport defaults to preserve the original rendering and font behavior | Avoids orientation, font, and line-weight changes from non-default flags |
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
1. **Asynchronous Load & Busy Indicator in `View()`**:
   ```csharp
   // BEFORE:
   _rp.LoadCadFile(path);
   context.ViewerContent = _rp;
   context.IsBusy = false;

   // AFTER:
   context.ViewerContent = _rp;
   context.IsBusy = true; // Shows QuickLook loading spinner

   _rp.LoadCadFileAsync(path, () =>
   {
       context.IsBusy = false; // Hides spinner once ready
   });
   ```
2. **Null-Safe Cleanup**:
   Added `if (_rp != null)` check prior to accessing `ActualWidth`/`ActualHeight` and disposing the control.

---

### 2.3. User Control & Async Wrapper (`RenderPanel.xaml.cs`)
* **File:** `QuickLook.Plugin.CADImport/RenderPanel.xaml.cs`

#### What Changed:
1. **Added Cancellation Token Management**:
   ```csharp
   private CancellationTokenSource _cts;
   ```
   Cancels any in-flight background parsing if the user switches files rapidly in QuickLook or closes the preview.
2. **Added `LoadCadFileAsync(string path, Action onLoaded)`**:
   - Offloads `CADImage.CreateImageByExtension(path)` and `image.LoadFromFile(path)` to a background thread (`Task.Run`).
   - Marshals back to UI thread via `TaskScheduler.FromCurrentSynchronizationContext()` and calls `cadImaging.ApplyLoadedImage(...)`.
3. **Disposal**:
   Cancels and disposes `_cts` on control disposal to eliminate background thread leaks.

---

### 2.4. CAD Rendering & Panning Engine (`CADImaging.cs` & Partial Files)

#### A. CAD Rendering Defaults (`CADImaging.cs`)
- Uses the original direct `CADImage.Draw(e.Graphics, ...)` path and CADImport defaults, preserving the plugin's established geometry, SHX text, and line-weight rendering behavior.

#### B. High-Performance Panning Engine (`CADImaging.cs`)
- **Problem in original code:** On every `MouseMove` event during drag, `cadImage.Draw(...)` was invoked. In complex drawings with 50,000+ entities, this caused severe UI lag (1–5 FPS).
- **Solution implemented:**
  1. Captures the control's already-rendered frame once at drag start.
  2. Blits the captured frame at the accumulated pan offset during the drag, avoiding repeated CAD geometry rendering.
  3. Discards the temporary frame on release and redraws using the original CADImport path.

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
   - `IsShowLineWeight = false` is enforced for rendering speed. If your drawings rely heavily on visual line thickness distinction, this can be made optional or restored.
3. **SHX Fonts**:
   - The plugin retains CADImport's default SHX font handling so text remains consistent with the original preview behavior.
4. **Target Framework Compatibility**:
   - Built targeting `net462` to match standard QuickLook runtime assemblies. If running on a non-Windows or strictly modern .NET Core runtime, QuickLook requires its Windows-specific desktop bridge.
