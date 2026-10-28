$releasePath = Join-Path $PSScriptRoot '..\Build\Release'
$packagePath = Join-Path $PSScriptRoot '..\QuickLook.Plugin.CADImport.qlplugin'
$temporaryZip = Join-Path $PSScriptRoot '..\QuickLook.Plugin.CADImport.zip'
$files = @(
    (Join-Path $releasePath 'QuickLook.Plugin.CADImport.dll'),
    (Join-Path $releasePath 'QuickLook.Plugin.Metadata.config')
)

foreach ($file in $files) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Required QuickLook package file is missing: $file"
    }
}

Remove-Item -LiteralPath $packagePath -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $temporaryZip -ErrorAction SilentlyContinue
Compress-Archive -Path $files -DestinationPath $temporaryZip
Move-Item -LiteralPath $temporaryZip -Destination $packagePath