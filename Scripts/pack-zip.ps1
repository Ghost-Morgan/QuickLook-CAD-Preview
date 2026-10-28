$releasePath = Join-Path $PSScriptRoot '..\Build\Release'
$packagePath = Join-Path $PSScriptRoot '..\QuickLook.Plugin.CADImport.qlplugin'
$temporaryZip = Join-Path $PSScriptRoot ("..\QuickLook.Plugin.CADImport.{0}.tmp.zip" -f [Guid]::NewGuid().ToString('N'))
$files = @(
    (Join-Path $releasePath 'QuickLook.Plugin.CADImport.dll'),
    (Join-Path $releasePath 'QuickLook.Plugin.Metadata.config')
)

foreach ($file in $files) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Required QuickLook package file is missing: $file"
    }
}

try {
    Compress-Archive -Path $files -DestinationPath $temporaryZip
    Move-Item -LiteralPath $temporaryZip -Destination $packagePath -Force
}
finally {
    if (Test-Path -LiteralPath $temporaryZip) {
        Remove-Item -LiteralPath $temporaryZip
    }
}