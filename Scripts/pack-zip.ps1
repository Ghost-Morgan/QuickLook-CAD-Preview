Remove-Item $PSScriptRoot\..\QuickLook.Plugin.CADImport.qlplugin -ErrorAction SilentlyContinue

$files = Get-ChildItem -Path $PSScriptRoot\..\Build\Release\ -Exclude *.pdb,*.xml
Compress-Archive $files.FullName $PSScriptRoot\..\QuickLook.Plugin.CADImport.zip
Move-Item $PSScriptRoot\..\QuickLook.Plugin.CADImport.zip $PSScriptRoot\..\QuickLook.Plugin.CADImport.qlplugin