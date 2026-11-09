# Builds the Thunderstore packages listed in packaging/packages.json into dist/.
# Usage: pwsh packaging/package.ps1 [-Only ThrowPreview,MacaronCore] [-GamePath "<path to Mycopunk>"]
#
# Each package: mods/<folder>/thunderstore/{README.md, CHANGELOG.md, icon.png} + a generated manifest.json
# (version from the csproj <Version>) + plugins/<AssemblyName>/<AssemblyName>.dll. Every package except MacaronCore
# depends on doku_macaron-MacaronCore at its current version.
param(
    [string[]]$Only = @(),
    [string]$GamePath = ""
)

$ErrorActionPreference = "Stop"
# "pwsh -File ... -Only A,B" passes one string "A,B": split it.
$Only = @($Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$root = Split-Path -Parent $PSScriptRoot
$spec = Get-Content (Join-Path $root "packaging/packages.json") -Raw | ConvertFrom-Json
$dotnet = if (Get-Command dotnet -ErrorAction SilentlyContinue) { "dotnet" } else { "C:\Program Files\dotnet\dotnet.exe" }
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force $dist | Out-Null

function Get-Project([string]$folder) {
    $proj = Get-ChildItem (Join-Path $root "mods/$folder/src") -Recurse -Filter *.csproj |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | Select-Object -First 1
    if (-not $proj) { throw "No csproj under mods/$folder/src" }
    [xml]$xml = Get-Content $proj.FullName -Raw
    $props = $xml.Project.PropertyGroup
    $version = ($props | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
    $assembly = ($props | ForEach-Object { $_.AssemblyName } | Where-Object { $_ }) | Select-Object -First 1
    if (-not $version) { throw "No <Version> in $($proj.FullName)" }
    if (-not $assembly) { $assembly = [IO.Path]::GetFileNameWithoutExtension($proj.Name) }
    [pscustomobject]@{ Path = $proj.FullName; Version = $version; Assembly = $assembly }
}

$core = Get-Project "macaron-core"
$coreDependency = "$($spec.namespace)-MacaronCore-$($core.Version)"

foreach ($pkg in $spec.packages) {
    if ($Only.Count -gt 0 -and $Only -notcontains $pkg.name) { continue }
    $p = Get-Project $pkg.folder
    $ts = Join-Path $root "mods/$($pkg.folder)/thunderstore"
    foreach ($f in "README.md", "CHANGELOG.md", "icon.png") {
        if (-not (Test-Path (Join-Path $ts $f))) { throw "Missing mods/$($pkg.folder)/thunderstore/$f" }
    }

    # The plugin's own version string should match the package version (some plugins hard-code it).
    $plugin = Get-ChildItem (Split-Path $p.Path) -Filter Plugin.cs | Select-Object -First 1
    if ($plugin) {
        $src = Get-Content $plugin.FullName -Raw
        if ($pkg.core -ne $true -and $src -notmatch [regex]::Escape('"' + $p.Version + '"')) {
            Write-Warning "$($pkg.name): version $($p.Version) not found in $($plugin.Name) (BepInPlugin version may differ)."
        }
    }

    $buildArgs = @("build", $p.Path, "-c", "Release", "-p:DeployToGame=false", "-nologo", "-v:q")
    if ($GamePath -ne "") { $buildArgs += "-p:GamePath=$GamePath" }
    & $dotnet @buildArgs | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $($pkg.name)" }
    $dll = Join-Path (Split-Path $p.Path) "bin/Release/netstandard2.1/$($p.Assembly).dll"
    if (-not (Test-Path $dll)) { throw "Built DLL not found: $dll" }

    $deps = @($spec.bepinex)
    if ($pkg.core -ne $true) { $deps += $coreDependency }
    $manifest = [ordered]@{
        name           = $pkg.name
        version_number = $p.Version
        website_url    = $spec.website_url
        description    = $pkg.description
        dependencies   = $deps
    }

    $stage = Join-Path $dist "stage-$($pkg.name)"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force (Join-Path $stage "plugins/$($p.Assembly)") | Out-Null
    ($manifest | ConvertTo-Json -Depth 4) | Set-Content -Encoding utf8NoBOM (Join-Path $stage "manifest.json")
    foreach ($f in "README.md", "CHANGELOG.md", "icon.png") { Copy-Item (Join-Path $ts $f) $stage }
    Copy-Item $dll (Join-Path $stage "plugins/$($p.Assembly)")

    $zip = Join-Path $dist "$($spec.namespace)-$($pkg.name)-$($p.Version).zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    # Files must sit at the root of the zip, so compress the contents, not the folder.
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
    Remove-Item $stage -Recurse -Force
    Write-Output "Created $zip"
}
