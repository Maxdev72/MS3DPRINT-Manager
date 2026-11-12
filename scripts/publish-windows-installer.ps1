# Builds the installer (Setup.exe) and the update packages with Velopack.
# Usage: scripts\publish-windows-installer.ps1 -Version 1.0.1
# Then upload everything in outputs\velopack to a GitHub release tagged v<Version>: installed apps pick it up and update silently.
param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
dotnet publish "$root\src\MS3DPRINT.Manager.Windows.App" -c Release -r win-x64 --self-contained true -p:Version=$Version -o "$root\outputs\velopack-publish"
if ($LASTEXITCODE) { throw 'publish failed' }
# Previous release files (for delta packages): vpk download github --repoUrl https://github.com/Maxdev72/MS3DPRINT-Manager
vpk pack --packId MS3DPRINTManager --packTitle "MS3DPRINT Manager" --packVersion $Version `
    --packDir "$root\outputs\velopack-publish" --mainExe MS3DPRINT.Manager.Windows.App.exe `
    --icon "$root\src\MS3DPRINT.Manager.Windows.App\Assets\app-icon.ico" --outputDir "$root\outputs\velopack"
