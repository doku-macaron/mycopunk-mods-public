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
$root = Split-Path -Parent $PSScriptRoot
$spec = Get-Content (Join-Path $root "packaging/packages.json") -Raw | ConvertFrom-Json
$dist = Join-Path $root "dist"

if ($Publish) {
    if (-not $env:TCLI_AUTH_TOKEN) { throw "Set `$env:TCLI_AUTH_TOKEN to the team's service-account token first." }
    if (-not (Get-Command tcli -ErrorAction SilentlyContinue)) { throw "tcli not found (dotnet tool install -g tcli)." }
}

# Upload in the order of packages.json (MacaronCore first), whatever the order of -Only.
foreach ($pkg in $spec.packages) {
    if ($Only -notcontains $pkg.name) { continue }
    $zip = Get-ChildItem $dist -Filter "$($spec.namespace)-$($pkg.name)-*.zip" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $zip) { throw "No zip for $($pkg.name) in dist/ (run packaging/package.ps1 first)." }
    $version = $zip.BaseName.Substring("$($spec.namespace)-$($pkg.name)-".Length)

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
