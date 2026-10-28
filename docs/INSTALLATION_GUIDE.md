# QuickLook CAD Preview - Installation & Uninstallation Guide

This guide covers installing and uninstalling the **QuickLook CAD Preview**
plugin for [QuickLook](https://github.com/QL-Win/QuickLook).

---

## Prerequisites

1. Ensure **QuickLook** is installed and running on your system.
   - [QuickLook from Microsoft Store](https://www.microsoft.com/store/apps/9nv4bs3l1h4s) or [GitHub Releases](https://github.com/QL-Win/QuickLook/releases).
2. Obtain the plugin package from a project release, or build it from source
   using the steps in [README.md](../README.md#build-from-source). The
   package file is:
   - `QuickLook.Plugin.CADImport.qlplugin`
   - The plugin uses the open-source ACadSharp library. No paid CAD engine or
     separate CAD application is required.

---

## Installation Methods

### Method 1: Automatic One-Click Installation (Recommended)

1. Ensure **QuickLook is running** (check the system tray at the bottom-right of your taskbar).
2. Open **File Explorer** and navigate to where `QuickLook.Plugin.CADImport.qlplugin` is located.
3. Select `QuickLook.Plugin.CADImport.qlplugin` and press the **Spacebar**.
4. QuickLook will display a popup window:
   > *"Click here to install this plugin"*
5. Click the button to confirm installation.
6. Restart QuickLook when prompted (or right-click the QuickLook icon in the system tray, select **Exit**, and reopen QuickLook).

---

### Method 2: Manual Installation

If you prefer to install manually or if one-click installation is unavailable:

#### For Microsoft Store Version of QuickLook:
1. Exit QuickLook from the system tray (right-click tray icon -> **Exit**).
2. Press `Win + R`, paste the following path, and hit **Enter**:
   ```cmd
   %LOCALAPPDATA%\Packages\21090PaddyXu.QuickLook_egxr34yet59cg\LocalCache\Roaming\pooi.moe\QuickLook\QuickLook.Plugin
   ```
3. Extract or copy the built plugin folder (`QuickLook.Plugin.CADImport` containing `QuickLook.Plugin.CADImport.dll` and `QuickLook.Plugin.Metadata.config`) into this directory.
4. Launch QuickLook again.

#### For Standard / MSI / Portable Version of QuickLook:
1. Exit QuickLook from the system tray.
2. Press `Win + R`, paste the following path, and hit **Enter**:
   ```cmd
   %APPDATA%\pooi.moe\QuickLook\QuickLook.Plugin
   ```
3. Copy or extract the `QuickLook.Plugin.CADImport` folder into this directory.
4. Launch QuickLook again.

---

## Uninstallation Methods

### Method 1: Manual Removal via File Explorer (Recommended)

1. **Exit QuickLook**:
   - Right-click the QuickLook icon in the Windows taskbar system tray.
   - Click **Exit**.
2. **Open the Plugin Folder**:
   - If using the **Microsoft Store** version, press `Win + R`, paste this path, and press **Enter**:
     ```cmd
     %LOCALAPPDATA%\Packages\21090PaddyXu.QuickLook_egxr34yet59cg\LocalCache\Roaming\pooi.moe\QuickLook\QuickLook.Plugin
     ```
   - If using the **Standard / MSI** version, press `Win + R`, paste this path, and press **Enter**:
     ```cmd
     %APPDATA%\pooi.moe\QuickLook\QuickLook.Plugin
     ```
3. **Delete the Plugin**:
   - Select and delete the `QuickLook.Plugin.CADImport` folder.
4. **Restart QuickLook**:
   - Launch QuickLook from the Start menu. The plugin is now completely uninstalled.

---

### Method 2: One-Line PowerShell Uninstallation

1. Close QuickLook.
2. Open PowerShell (`Win + X` -> **Terminal** or **PowerShell**) and run:
   ```powershell
   Get-Process QuickLook -ErrorAction SilentlyContinue | Stop-Process -Force
   Get-ChildItem -Path "$env:LOCALAPPDATA\Packages\*QuickLook*\LocalCache\Roaming\pooi.moe\QuickLook\QuickLook.Plugin\QuickLook.Plugin.CADImport", "$env:APPDATA\pooi.moe\QuickLook\QuickLook.Plugin\QuickLook.Plugin.CADImport" -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
   ```
3. Restart QuickLook.

---

## How to Verify It Works

1. In Windows File Explorer, highlight a `.dwg` or `.dxf` drawing.
2. Press the **Spacebar**.
3. If the DWG contains an embedded thumbnail, it appears while the interactive
   3D preview is prepared. Otherwise, the plugin shows the model after it is
   read and rendered.

Civil 3D proxy geometry is previewed when it is available in the drawing.
Native Civil 3D object interpretation is not supported; see
[README.md](../README.md#features) for the current limits.

---

## Navigation & Controls

| Action | Control |
| :--- | :--- |
| **Rotate 3D view** | Left-click and drag |
| **Pan View** | Right- or middle-click and drag |
| **Zoom In / Out** | Scroll mouse wheel forward / backward |
| **Reset View / Fit to Screen** | **Double-click** or press **R** |
| **Close Preview** | Press **Spacebar** or **Esc** |
