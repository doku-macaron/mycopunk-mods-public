# Uploads the zips made by packaging/package.ps1 to Thunderstore with tcli (Thunderstore CLI).
# Usage:
#   pwsh packaging/publish.ps1 -Only MacaronCore,ThrowPreview            # dry run: shows what would be uploaded
#   pwsh packaging/publish.ps1 -Only MacaronCore,ThrowPreview -Publish   # uploads
# Needs: tcli (dotnet tool install -g tcli) and the team's service-account token in $env:TCLI_AUTH_TOKEN.
# The token is never written to disk. Upload MacaronCore first: the others depend on it.
param(
    [Parameter(Mandatory = $true)][string[]]$Only,
    [switch]$Publish
)

$ErrorActionPreference = "Stop"
# "pwsh -File ... -Only A,B" passes one string "A,B": split it.
$Only = @($Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Only.Count -eq 0) { throw "Specify at least one package with -Only." }
$root = Split-Path -Parent $PSScriptRoot
$spec = Get-Content (Join-Path $root "packaging/packages.json") -Raw | ConvertFrom-Json
$dist = Join-Path $root "dist"
$unknown = @($Only | Where-Object { $_ -notin $spec.packages.name })
if ($unknown.Count -gt 0) { throw "Unknown package(s): $($unknown -join ', ')" }

if ($Publish) {
    if (-not $env:TCLI_AUTH_TOKEN) { throw "Set `$env:TCLI_AUTH_TOKEN to the team's service-account token first." }
    if (-not (Get-Command tcli -ErrorAction SilentlyContinue)) { throw "tcli not found (dotnet tool install -g tcli)." }
}

# Upload in the order of packages.json (MacaronCore first), whatever the order of -Only.
foreach ($pkg in $spec.packages) {
    if ($Only -notcontains $pkg.name) { continue }
    $project = Get-ChildItem -LiteralPath (Join-Path $root "mods/$($pkg.folder)/src") -Recurse -Filter *.csproj |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | Select-Object -First 1
    if (-not $project) { throw "No project for $($pkg.name)." }
    [xml]$projectXml = Get-Content -LiteralPath $project.FullName -Raw
    $version = ($projectXml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
    if (-not $version) { throw "No version for $($pkg.name)." }
    $zipPath = Join-Path $dist "$($spec.namespace)-$($pkg.name)-$version.zip"
    if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) { throw "No zip for $($pkg.name) $version in dist/ (run packaging/package.ps1 first)." }
    $zip = Get-Item -LiteralPath $zipPath

    # tcli takes the community and categories from a config file; the zip already holds the manifest.
    $cats = ($pkg.categories | ForEach-Object { '"' + $_ + '"' }) -join ", "
    $toml = @"
[config]
schemaVersion = "0.0.1"

[package]
namespace = "$($spec.namespace)"
name = "$($pkg.name)"
versionNumber = "$version"
description = "$($pkg.description.Replace('"', '\"'))"
websiteUrl = "$($spec.website_url)"
containsNsfwContent = false

[package.dependencies]

[publish]
repository = "https://thunderstore.io"
communities = ["$($spec.community)"]

[publish.categories]
$($spec.community) = [$cats]
"@
    $tomlPath = Join-Path $dist "thunderstore-$($pkg.name).toml"
    Set-Content -Encoding utf8NoBOM $tomlPath $toml

    if (-not $Publish) {
        Write-Output "would upload $($zip.Name) to $($spec.community) [$cats]"
        continue
    }
    Write-Output "uploading $($zip.Name) ..."
    & tcli publish --config-path $tomlPath --file $zip.FullName --token $env:TCLI_AUTH_TOKEN
    if ($LASTEXITCODE -ne 0) { throw "tcli publish failed for $($pkg.name)" }
}
