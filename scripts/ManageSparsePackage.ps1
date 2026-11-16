param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Install", "Uninstall")]
    [string]$Mode,

    [Parameter(Mandatory = $true)]
    [string]$PackageName,

    [Parameter(Mandatory = $true)]
    [version]$PackageVersion,

    [string]$PackagePath,
    [string]$ExternalLocation
)

$ErrorActionPreference = "Stop"

if ($Mode -eq "Install") {
    if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
        throw "The signed context-menu package is missing."
    }

    if (-not (Test-Path -LiteralPath $ExternalLocation -PathType Container)) {
        throw "The application installation folder could not be found."
    }

    $currentPackage = Get-AppxPackage -Name $PackageName
    if ($currentPackage -and $currentPackage.Version -gt $PackageVersion) {
        return
    }

    Add-AppxPackage `
        -Path $PackagePath `
        -ExternalLocation $ExternalLocation `
        -ForceUpdateFromAnyVersion
    return
}

$installedPackages = Get-AppxPackage -Name $PackageName
foreach ($installedPackage in $installedPackages) {
    if ($installedPackage.Version -le $PackageVersion) {
        Remove-AppxPackage -Package $installedPackage.PackageFullName
    }
}
