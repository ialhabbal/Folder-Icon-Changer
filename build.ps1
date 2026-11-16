param(
    [ValidateSet("All", "Test", "Publish", "Installer", "Shell", "SignedRelease")]
    [string]$Task = "All",

    [string]$SigningCertificateThumbprint = "0F981672D8A74A1CF8EA2F5E67EE30DDE82C3FFD"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = $PSScriptRoot
$localDotnet = Join-Path $repositoryRoot ".tools\dotnet\dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $localDotnet) {
    $localDotnet
}
else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

$env:NUGET_PACKAGES = Join-Path $repositoryRoot ".packages"
$env:DOTNET_CLI_HOME = Join-Path $repositoryRoot ".tools\cli-home"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"

function Invoke-Dotnet {
    param([string[]]$Arguments)

    & $dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "The .NET command failed. Review the build output above."
    }
}

function Publish-Application {
    $publishDirectory = Join-Path $repositoryRoot "artifacts\publish"
    New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null

    Invoke-Dotnet @(
        "publish",
        "src\FolderIconChanger.App\FolderIconChanger.App.csproj",
        "--configuration", "Release",
        "--runtime", "win-x64",
        "--self-contained", "true",
        "--output", $publishDirectory,
        "-p:PublishSingleFile=true",
        "-p:IncludeAllContentForSelfExtract=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true",
        "-p:DebugType=none"
    )

    $executable = Join-Path $publishDirectory "FolderIconChanger.exe"
    if (-not (Test-Path -LiteralPath $executable)) {
        throw "The standalone application was not produced."
    }

    Copy-Item -LiteralPath (Join-Path $repositoryRoot "THIRD-PARTY-NOTICES.md") `
        -Destination (Join-Path $publishDirectory "THIRD-PARTY-NOTICES.md") -Force
    foreach ($licenseFile in @(
        "Apache-2.0.txt",
        "Magick.NET-Q8-x64-15.1.0-Notice.txt"
    )) {
        $source = Join-Path $repositoryRoot "licenses\$licenseFile"
        if (-not (Test-Path -LiteralPath $source)) {
            throw "Required third-party license file is missing: $source"
        }

        Copy-Item -LiteralPath $source `
            -Destination (Join-Path $publishDirectory $licenseFile) -Force
        Copy-Item -LiteralPath $source `
            -Destination (Join-Path $repositoryRoot "dist\$licenseFile") -Force
    }

    Copy-Item -LiteralPath $executable `
        -Destination (Join-Path $repositoryRoot "dist\FolderIconChanger.exe") -Force
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "THIRD-PARTY-NOTICES.md") `
        -Destination (Join-Path $repositoryRoot "dist\THIRD-PARTY-NOTICES.md") -Force
}

function Build-ShellExtension {
    $visualStudioRoot = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools"
    $developerCommand = Join-Path $visualStudioRoot "Common7\Tools\VsDevCmd.bat"
    $sourceFile = Join-Path $repositoryRoot "native\FolderIconChanger.Shell\FolderIconChanger.Shell.cpp"
    $moduleDefinition = Join-Path $repositoryRoot "native\FolderIconChanger.Shell\FolderIconChanger.Shell.def"
    $outputFile = Join-Path $repositoryRoot "artifacts\publish\FolderIconChanger.Shell.dll"
    $buildDirectory = Join-Path $repositoryRoot "artifacts\shell-build"
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outputFile) | Out-Null
    New-Item -ItemType Directory -Force -Path $buildDirectory | Out-Null
    if (-not (Test-Path -LiteralPath $developerCommand)) {
        throw "Visual Studio C++ Build Tools are required to build the Explorer integration."
    }

    $compileCommand = (
        'call "' + $developerCommand + '" -arch=x64 -host_arch=x64 >nul && ' +
        'cd /d "' + $buildDirectory + '" && ' +
        'cl.exe /nologo /std:c++17 /EHsc /MD /W4 /WX /DUNICODE /D_UNICODE /LD "' +
        $sourceFile + '" /link shell32.lib shlwapi.lib ole32.lib /DEF:"' +
        $moduleDefinition + '" /OUT:"' +
        $outputFile + '"'
    )
    & $env:ComSpec /c $compileCommand
    if ($LASTEXITCODE -ne 0) {
        throw "The native Explorer integration build failed."
    }

    if (-not (Test-Path -LiteralPath $outputFile)) {
        throw "The native Explorer integration DLL was not produced."
    }

    Copy-Item -LiteralPath $outputFile `
        -Destination (Join-Path $repositoryRoot "dist\FolderIconChanger.Shell.dll") -Force
}

function Build-Installer {
    param([bool]$IncludeSparsePackage = $false)

    Invoke-Dotnet @("tool", "restore", "--tool-manifest", ".config\dotnet-tools.json")

    $publishDirectory = Join-Path $repositoryRoot "artifacts\publish"
    $installerPath = Join-Path $repositoryRoot "dist\FolderIconChangerSetup.msi"
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $installerPath) | Out-Null

    Invoke-Dotnet @(
        "tool", "run", "wix", "--", "build",
        "-arch", "x64",
        "-d", "PublishDir=$publishDirectory",
        "-d", "RepositoryRoot=$repositoryRoot",
        "-d", "IncludeSparsePackage=$($IncludeSparsePackage.ToString().ToLowerInvariant())",
        "installer\FolderIconChanger.wxs",
        "-o", $installerPath
    )
}

function Get-SigningCertificate {
    param([string]$Thumbprint)

    $certificatePath = "Cert:\CurrentUser\My\$Thumbprint"
    $certificate = Get-Item -LiteralPath $certificatePath -ErrorAction SilentlyContinue
    if (-not $certificate) {
        throw "The configured signing certificate was not found in the current user's Personal certificate store."
    }

    $now = [DateTime]::UtcNow
    if (-not $certificate.HasPrivateKey -or
        $certificate.NotBefore.ToUniversalTime() -gt $now -or
        $certificate.NotAfter.ToUniversalTime() -le $now) {
        throw "The certificate is expired, not yet valid, or does not contain an accessible private key."
    }

    $codeSigningOid = "1.3.6.1.5.5.7.3.3"
    $ekuExtension = $certificate.Extensions |
        Where-Object { $_ -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] }
    if (-not $ekuExtension -or
        -not ($ekuExtension.EnhancedKeyUsages | Where-Object { $_.Value -eq $codeSigningOid })) {
        throw "The certificate does not include the Code Signing usage required for application packages."
    }

    $rsaKey = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey(
        $certificate)
    if ($null -eq $rsaKey) {
        throw "The certificate's private key is not available as a supported RSA signing key."
    }

    $rsaKey.Dispose()
    [pscustomobject]@{
        Thumbprint = $certificate.Thumbprint
        Certificate = $certificate
        Publisher = $certificate.Subject
        PublisherDisplayName = $certificate.GetNameInfo(
            [System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
            $false)
    }
}

function Find-SignTool {
    $kitsRoot = "C:\Program Files (x86)\Windows Kits\10\bin"
    $signTool = Get-ChildItem -LiteralPath $kitsRoot -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\signtool.exe" } |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
    if (-not $signTool) {
        throw "Windows SDK SignTool was not found."
    }

    return $signTool
}

function Sign-File {
    param(
        [string]$SignTool,
        [string]$Thumbprint,
        [string]$FilePath,
        [string]$ExpectedThumbprint
    )

    & $SignTool sign /sha1 $Thumbprint /s My /fd SHA256 $FilePath
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool could not sign one of the release files."
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $FilePath
    if (-not $signature.SignerCertificate -or
        $signature.SignerCertificate.Thumbprint -ne $ExpectedThumbprint) {
        throw "The release file's signature does not match the selected certificate."
    }

    $verificationProcess = [System.Diagnostics.Process]::new()
    $verificationProcess.StartInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $verificationProcess.StartInfo.FileName = $SignTool
    $verificationProcess.StartInfo.Arguments = 'verify /pa /v "' + $FilePath + '"'
    $verificationProcess.StartInfo.UseShellExecute = $false
    $verificationProcess.StartInfo.CreateNoWindow = $true
    $verificationProcess.StartInfo.RedirectStandardOutput = $true
    $verificationProcess.StartInfo.RedirectStandardError = $true
    [void]$verificationProcess.Start()
    $verificationOutput = $verificationProcess.StandardOutput.ReadToEnd()
    $verificationError = $verificationProcess.StandardError.ReadToEnd()
    $verificationProcess.WaitForExit()
    $verificationExitCode = $verificationProcess.ExitCode
    $verificationText = $verificationOutput + [Environment]::NewLine + $verificationError
    if ($verificationExitCode -ne 0) {
        if ($verificationText -notmatch 'terminated in a root\s+certificate which is not trusted') {
            throw "SignTool could not verify the release file's signature."
        }

        Write-Warning (
            "The signature on '$([System.IO.Path]::GetFileName($FilePath))' matches the selected certificate, " +
            "but Windows does not trust its self-signed root. No trust store was changed."
        )
    }
}

function Build-SparsePackage {
    param(
        [string]$Publisher,
        [string]$PublisherDisplayName
    )

    $manifestTemplate = Join-Path $repositoryRoot "package\AppxManifest.xml.template"
    $stagingDirectory = Join-Path $repositoryRoot "artifacts\msix-staging"
    $assetDirectory = Join-Path $stagingDirectory "Assets"
    $manifestPath = Join-Path $stagingDirectory "AppxManifest.xml"
    $template = [System.IO.File]::ReadAllText($manifestTemplate)
    $xmlPublisher = [System.Security.SecurityElement]::Escape($Publisher)
    $xmlDisplayName = [System.Security.SecurityElement]::Escape($PublisherDisplayName)
    $manifest = $template.Replace("__PUBLISHER__", $xmlPublisher).Replace(
        "__PUBLISHER_DISPLAY_NAME__",
        $xmlDisplayName)
    [System.IO.Directory]::CreateDirectory($assetDirectory) | Out-Null
    [System.IO.File]::WriteAllText(
        $manifestPath,
        $manifest,
        [System.Text.UTF8Encoding]::new($false))
    foreach ($asset in @(
        "Square44x44Logo.png",
        "Square150x150Logo.png",
        "StoreLogo.png"
    )) {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot "package\Assets\$asset") `
            -Destination (Join-Path $assetDirectory $asset) -Force
    }

    [xml]$validatedManifest = Get-Content -LiteralPath $manifestPath -Raw
    $identityPublisher = $validatedManifest.Package.Identity.Publisher
    if ($identityPublisher -cne $Publisher) {
        throw "The sparse package Publisher does not exactly match the signing certificate Subject."
    }

    $makeAppx = Get-ChildItem `
        -LiteralPath "C:\Program Files (x86)\Windows Kits\10\bin" `
        -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\makeappx.exe" } |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
    if (-not $makeAppx) {
        throw "Windows SDK MakeAppx was not found."
    }

    $packagePath = Join-Path $repositoryRoot "artifacts\publish\FolderIconChanger.msix"
    & $makeAppx pack /nv /d $stagingDirectory /p $packagePath /o *> $null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $packagePath)) {
        throw "The sparse MSIX package could not be created."
    }

    return $packagePath
}

function Build-SignedRelease {
    $certificateInfo = Get-SigningCertificate -Thumbprint $SigningCertificateThumbprint
    try {
        Invoke-Dotnet @(
            "build",
            "src\FolderIconChanger.App\FolderIconChanger.App.csproj",
            "--configuration", "Release"
        )
        Invoke-Dotnet @(
            "test",
            "tests\FolderIconChanger.Tests\FolderIconChanger.Tests.csproj",
            "--configuration", "Release"
        )
        Publish-Application
        Build-ShellExtension

        $signTool = Find-SignTool
        $publishDirectory = Join-Path $repositoryRoot "artifacts\publish"
        foreach ($fileName in @("FolderIconChanger.exe", "FolderIconChanger.Shell.dll")) {
            Sign-File `
                -SignTool $signTool `
                -Thumbprint $certificateInfo.Thumbprint `
                -FilePath (Join-Path $publishDirectory $fileName) `
                -ExpectedThumbprint $certificateInfo.Thumbprint
        }

        $packagePath = Build-SparsePackage `
            -Publisher $certificateInfo.Publisher `
            -PublisherDisplayName $certificateInfo.PublisherDisplayName
        Sign-File `
            -SignTool $signTool `
            -Thumbprint $certificateInfo.Thumbprint `
            -FilePath $packagePath `
            -ExpectedThumbprint $certificateInfo.Thumbprint

        Copy-Item -LiteralPath (Join-Path $publishDirectory "FolderIconChanger.exe") `
            -Destination (Join-Path $repositoryRoot "dist\FolderIconChanger.exe") -Force
        Copy-Item -LiteralPath (Join-Path $publishDirectory "FolderIconChanger.Shell.dll") `
            -Destination (Join-Path $repositoryRoot "dist\FolderIconChanger.Shell.dll") -Force
        Copy-Item -LiteralPath $packagePath `
            -Destination (Join-Path $repositoryRoot "dist\FolderIconChanger.msix") -Force
        Build-Installer -IncludeSparsePackage $true
        $installerPath = Join-Path $repositoryRoot "dist\FolderIconChangerSetup.msi"
        Sign-File `
            -SignTool $signTool `
            -Thumbprint $certificateInfo.Thumbprint `
            -FilePath $installerPath `
            -ExpectedThumbprint $certificateInfo.Thumbprint

        $publicCertificatePath = Join-Path $repositoryRoot "dist\FolderIconChangerPublisher.cer"
        [System.IO.File]::WriteAllBytes(
            $publicCertificatePath,
            $certificateInfo.Certificate.RawData)
        $publicCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
            $publicCertificatePath)
        try {
            if ($publicCertificate.HasPrivateKey -or
                $publicCertificate.Thumbprint -ne $certificateInfo.Thumbprint) {
                throw "The exported publisher certificate does not match the signing certificate."
            }
        }
        finally {
            $publicCertificate.Dispose()
        }

        Write-Output "Publisher: $($certificateInfo.Publisher)"
        Write-Output "Certificate thumbprint: $($certificateInfo.Certificate.Thumbprint)"
        Write-Output "Certificate valid until: $($certificateInfo.Certificate.NotAfter.ToUniversalTime().ToString('u'))"
        Write-Output "MSIX, standalone EXE, shell DLL, and MSI contain SHA-256 signatures from the selected certificate."
        Write-Output "Public signer certificate: $publicCertificatePath"
    }
    finally {
        $certificateInfo.Certificate.Dispose()
    }
}

Push-Location $repositoryRoot
try {
    switch ($Task) {
        "Test" {
            Invoke-Dotnet @(
                "test",
                "tests\FolderIconChanger.Tests\FolderIconChanger.Tests.csproj",
                "--configuration", "Release"
            )
        }
        "Publish" {
            Publish-Application
            Build-ShellExtension
        }
        "Installer" {
            Publish-Application
            Build-ShellExtension
            Build-Installer
        }
        "Shell" {
            Build-ShellExtension
        }
        "SignedRelease" {
            Build-SignedRelease
        }
        "All" {
            Invoke-Dotnet @(
                "build",
                "src\FolderIconChanger.App\FolderIconChanger.App.csproj",
                "--configuration", "Release"
            )
            Invoke-Dotnet @(
                "test",
                "tests\FolderIconChanger.Tests\FolderIconChanger.Tests.csproj",
                "--configuration", "Release"
            )
            Publish-Application
            Build-ShellExtension
            Build-Installer
        }
    }
}
finally {
    Pop-Location
}

Write-Output "Completed '$Task' successfully."
