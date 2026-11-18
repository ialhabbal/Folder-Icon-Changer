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

function Refresh-ShellAssociations {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class FolderIconChangerShellNotifications
{
    [DllImport("shell32.dll", ExactSpelling = true)]
    public static extern void SHChangeNotify(
        uint eventId,
        uint flags,
        IntPtr item1,
        IntPtr item2);
}
"@
    [FolderIconChangerShellNotifications]::SHChangeNotify(
        0x08000000,
        0,
        [IntPtr]::Zero,
        [IntPtr]::Zero)
}

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
    Refresh-ShellAssociations
    return
}

$installedPackages = Get-AppxPackage -Name $PackageName
foreach ($installedPackage in $installedPackages) {
    if ($installedPackage.Version -le $PackageVersion) {
        Remove-AppxPackage -Package $installedPackage.PackageFullName
    }
}

Refresh-ShellAssociations
