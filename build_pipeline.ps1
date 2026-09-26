#Requires -Version 5.1
<#
.SYNOPSIS
    LibreScan Security - Automated Build Pipeline

.DESCRIPTION
    Downloads ClamAV portable binaries, configures freshclam, compiles the
    .NET 10 WPF application as a self-contained single-file executable,
    and invokes Inno Setup to produce the final installer.

.PARAMETER ClamAVVersion
    ClamAV release version to download. Default: 1.5.4

.PARAMETER SkipClamAV
    Skip the ClamAV binary download step (use existing clamav_bin/).

.PARAMETER SkipInstaller
    Skip the Inno Setup compilation step.

.EXAMPLE
    .\build_pipeline.ps1
    .\build_pipeline.ps1 -ClamAVVersion "1.5.4"
    .\build_pipeline.ps1 -SkipClamAV -SkipInstaller
#>

param(
    [string]$ClamAVVersion = "1.5.4",
    [string]$ClamAVUrl = "",
    [switch]$SkipClamAV,
    [switch]$SkipInstaller
)

# -- Strict error handling ---------------------------------------------------
$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'   # Dramatically speeds up Invoke-WebRequest

# -- Path constants ----------------------------------------------------------
$ProjectRoot  = $PSScriptRoot
$ClamAVBin    = Join-Path $ProjectRoot "clamav_bin"
$DatabaseDir  = Join-Path $ClamAVBin   "database"
$AssetsDir    = Join-Path $ProjectRoot "Assets"
$QuarantineDir= Join-Path $ProjectRoot "Quarantine"
$SrcProject   = Join-Path $ProjectRoot "src\LibreScan\LibreScan.csproj"
$PublishDir   = Join-Path $ProjectRoot "publish"

Write-Host ""
Write-Host "+------------------------------------------------------+" -ForegroundColor Cyan
Write-Host "|       LibreScan Security - Build Pipeline            |" -ForegroundColor Cyan
Write-Host "+------------------------------------------------------+" -ForegroundColor Cyan

# -----------------------------------------------------------------------------
# STEP 1: Create Required Directories & Assets
# -----------------------------------------------------------------------------
Write-Host "`n[1/6] Creating directory structure..." -ForegroundColor Cyan

foreach ($dir in @($AssetsDir, $DatabaseDir, $QuarantineDir)) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-Host "  + Created: $dir" -ForegroundColor DarkGray
    }
    else {
        Write-Host "  - Exists:  $dir" -ForegroundColor DarkGray
    }
}

$IconPath = Join-Path $AssetsDir "librescan.ico"
if (-not (Test-Path $IconPath)) {
    $IconScript = Join-Path $ProjectRoot "generate_icon.ps1"
    if (Test-Path $IconScript) {
        Write-Host "  Generating application icon..." -ForegroundColor DarkGray
        & $IconScript
    }
}

# -----------------------------------------------------------------------------
# STEP 2: Download & Extract ClamAV Portable (win-x64)
# -----------------------------------------------------------------------------
if (-not $SkipClamAV) {
    $ClamScanExe = Join-Path $ClamAVBin "clamscan.exe"

    if (Test-Path $ClamScanExe) {
        Write-Host "`n[2/6] ClamAV binaries already present - skipping download." -ForegroundColor Yellow
    }
    else {
        Write-Host "`n[2/6] Downloading ClamAV $ClamAVVersion (win-x64)..." -ForegroundColor Cyan

        if ($ClamAVUrl) {
            $candidateUrls = @($ClamAVUrl)
        }
        else {
            $candidateUrls = @(
                "https://github.com/Cisco-Talos/clamav/releases/download/clamav-$ClamAVVersion/clamav-$ClamAVVersion.win.x64.zip",
                "https://github.com/Cisco-Talos/clamav/releases/download/clamav-$ClamAVVersion/clamav-$ClamAVVersion.win.x64.portable.zip"
            )
        }

        $ZipPath     = Join-Path $ProjectRoot "clamav-$ClamAVVersion.win.x64.zip"
        $TempExtract = Join-Path $ProjectRoot "_clamav_extract_temp"

        try {
            [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12 -bor [System.Net.SecurityProtocolType]::Tls13
            $curlCmd = Get-Command "curl.exe" -ErrorAction SilentlyContinue

            $downloadSuccess = $false
            foreach ($url in $candidateUrls) {
                Write-Host "  Attempting URL: $url" -ForegroundColor DarkGray
                $retryCount = 0
                $maxRetries = 3

                while ($retryCount -lt $maxRetries) {
                    try {
                        if (Test-Path $ZipPath) {
                            Remove-Item -Path $ZipPath -Force -ErrorAction SilentlyContinue
                        }

                        if ($curlCmd) {
                            & $curlCmd.Source -fL $url -o $ZipPath
                            if ($LASTEXITCODE -ne 0) {
                                throw "curl failed with exit code $LASTEXITCODE"
                            }
                        }
                        else {
                            Invoke-WebRequest -Uri $url -OutFile $ZipPath -UseBasicParsing -UserAgent "Mozilla/5.0 (Windows NT 10.0; Win64; x64) LibreScan/1.1.0"
                        }

                        if ((Test-Path $ZipPath) -and ((Get-Item $ZipPath).Length -gt 1000000)) {
                            $downloadSuccess = $true
                            $ClamAVUrl = $url
                            break
                        }
                        else {
                            throw "Downloaded file is missing or suspiciously small."
                        }
                    }
                    catch {
                        $retryCount++
                        if ($retryCount -lt $maxRetries) {
                            Write-Host "  Retry $retryCount/$maxRetries..." -ForegroundColor Yellow
                            Start-Sleep -Seconds 3
                        }
                    }
                }

                if ($downloadSuccess) {
                    break
                }
            }

            if (-not $downloadSuccess) {
                throw "Failed to download ClamAV from candidate URLs: $($candidateUrls -join ', ')"
            }

            $zipSizeMB = ((Get-Item $ZipPath).Length / 1MB).ToString('F1')
            Write-Host "  Downloaded: $zipSizeMB MB" -ForegroundColor DarkGray

            # Extract to temp directory
            Write-Host "  Extracting archive..." -ForegroundColor DarkGray
            if (Test-Path $TempExtract) {
                Remove-Item -Path $TempExtract -Recurse -Force
            }
            Expand-Archive -Path $ZipPath -DestinationPath $TempExtract -Force

            # Locate clamscan.exe inside the extracted contents
            $foundScan = Get-ChildItem -Path $TempExtract -Filter "clamscan.exe" -Recurse | Select-Object -First 1
            if (-not $foundScan) {
                throw "Archive did not contain clamscan.exe"
            }

            $extractedBinDir = $foundScan.DirectoryName

            # Remove existing clamav_bin if present
            if (Test-Path $ClamAVBin) {
                Remove-Item -Path $ClamAVBin -Recurse -Force
            }

            # Copy all files from extracted folder into clamav_bin
            New-Item -ItemType Directory -Path $ClamAVBin -Force | Out-Null
            Copy-Item -Path "$extractedBinDir\*" -Destination $ClamAVBin -Recurse -Force

            # Ensure database directory exists inside clamav_bin
            if (-not (Test-Path $DatabaseDir)) {
                New-Item -ItemType Directory -Path $DatabaseDir -Force | Out-Null
            }

            Write-Host "  ClamAV $ClamAVVersion extracted to: $ClamAVBin" -ForegroundColor Green
        }
        finally {
            # Cleanup temp files
            if (Test-Path $ZipPath) {
                Remove-Item -Path $ZipPath -Force -ErrorAction SilentlyContinue
            }
            if ($TempExtract -and (Test-Path $TempExtract)) {
                Remove-Item -Path $TempExtract -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        # Validate extraction
        if (-not (Test-Path $ClamScanExe)) {
            throw "ClamAV extraction failed: clamscan.exe not found at $ClamScanExe"
        }
    }
}
else {
    Write-Host "`n[2/6] Skipping ClamAV download (-SkipClamAV)." -ForegroundColor Yellow
}

# -----------------------------------------------------------------------------
# STEP 3: Configure freshclam.conf & Seed Definitions
# -----------------------------------------------------------------------------
Write-Host "`n[3/6] Configuring freshclam.conf..." -ForegroundColor Cyan

$ConfTarget = Join-Path $ClamAVBin "freshclam.conf"
$ConfCandidates = @(
    (Join-Path $ClamAVBin "freshclam.conf.sample"),
    (Join-Path $ClamAVBin "conf_examples\freshclam.conf.sample")
)
$ConfSample = $ConfCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($ConfSample) {
    # Read the sample config
    $confContent = Get-Content $ConfSample -Raw -Encoding UTF8

    # Remove the mandatory "Example" line (ClamAV refuses to run with it)
    $confContent = $confContent -replace '(?m)^\s*Example\s*$', '# Example (removed by build pipeline)'

    # Comment out any static DatabaseDirectory so runtime can pass dynamic absolute --datadir
    $confContent = $confContent -replace '(?m)^\s*DatabaseDirectory\s+.*$', '# DatabaseDirectory configured at runtime via --datadir'

    # Ensure DatabaseMirror is set to database.clamav.net
    if ($confContent -notmatch '(?m)^\s*DatabaseMirror\s+database\.clamav\.net') {
        $confContent = $confContent.TrimEnd()
        $confContent += "`r`n`r`n# --- Configured by LibreScan build pipeline ---`r`n"
        $confContent += "DatabaseMirror database.clamav.net`r`n"
    }

    # CRITICAL: Write UTF-8 WITHOUT Byte Order Mark (BOM). ClamAV C parser fails if BOM (0xEF 0xBB 0xBF) is present!
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($ConfTarget, $confContent, $utf8NoBom)
    Write-Host "  Configured: $ConfTarget (UTF-8 without BOM)" -ForegroundColor Green
}
elseif (Test-Path $ConfTarget) {
    # Ensure existing freshclam.conf has no BOM and has DatabaseMirror configured
    $confContent = Get-Content $ConfTarget -Raw -Encoding UTF8
    $confContent = $confContent -replace '(?m)^\s*Example\s*$', '# Example (removed by build pipeline)'
    if ($confContent -notmatch '(?m)^\s*DatabaseMirror\s+database\.clamav\.net') {
        $confContent = $confContent.TrimEnd()
        $confContent += "`r`n`r`n# --- Configured by LibreScan build pipeline ---`r`n"
        $confContent += "DatabaseMirror database.clamav.net`r`n"
    }
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($ConfTarget, $confContent, $utf8NoBom)
    Write-Host "  freshclam.conf validated & preserved (UTF-8 without BOM)." -ForegroundColor Green
}
else {
    # Fallback minimal freshclam.conf
    $minimalConf = "DatabaseMirror database.clamav.net`r`nDNSDatabaseInfo current.cvd.clamav.net`r`n"
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($ConfTarget, $minimalConf, $utf8NoBom)
    Write-Host "  Generated minimal: $ConfTarget" -ForegroundColor Green
}

# Pre-seed initial virus definitions if database directory is empty
$cvdFiles = Get-ChildItem -Path $DatabaseDir -Filter "*.cvd" -ErrorAction SilentlyContinue
if (-not $cvdFiles -or $cvdFiles.Count -eq 0) {
    Write-Host "`n  Downloading initial virus definitions (main, daily, bytecode)..." -ForegroundColor Cyan
    Write-Host "  This enables instant out-of-the-box virus scanning." -ForegroundColor DarkGray
    $freshClamExe = Join-Path $ClamAVBin "freshclam.exe"
    if (Test-Path $freshClamExe) {
        try {
            & $freshClamExe --config-file="$ConfTarget" --datadir="$DatabaseDir" --show-progress=no
            if ($LASTEXITCODE -eq 0) {
                Write-Host "  Initial definitions downloaded successfully." -ForegroundColor Green
            }
            else {
                Write-Warning "Initial definition download exited with code $LASTEXITCODE. The app will retry on launch."
            }
        }
        catch {
            Write-Warning "Could not pre-download virus definitions ($($_)). The app will download them on first run."
        }
    }
}
else {
    Write-Host "  Virus definitions present in database/ ($($cvdFiles.Count) CVD files)." -ForegroundColor DarkGray
}

# -----------------------------------------------------------------------------
# STEP 4: Restore NuGet Packages
# -----------------------------------------------------------------------------
Write-Host "`n[4/6] Restoring NuGet packages..." -ForegroundColor Cyan

if (-not (Test-Path $SrcProject)) {
    throw "Project file not found: $SrcProject"
}

dotnet restore $SrcProject
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed (exit code: $LASTEXITCODE)"
}
Write-Host "  Restore completed." -ForegroundColor Green

# -----------------------------------------------------------------------------
# STEP 5: Publish Single-File Executable
# -----------------------------------------------------------------------------
Write-Host "`n[5/6] Publishing LibreScan (Release | win-x64 | SingleFile)..." -ForegroundColor Cyan

# Ensure no background instance is running
Get-Process -Name "LibreScan" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# Clean previous publish output with retry
if (Test-Path $PublishDir) {
    $cleaned = $false
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            Remove-Item -Path $PublishDir -Recurse -Force -ErrorAction Stop
            $cleaned = $true
            break
        }
        catch {
            Start-Sleep -Milliseconds 750
        }
    }
}

dotnet publish $SrcProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed (exit code: $LASTEXITCODE)"
}

# Copy ClamAV binaries alongside the published executable if present
$PublishClamAV = Join-Path $PublishDir "clamav_bin"
if (Test-Path $ClamAVBin) {
    Write-Host "  Staging clamav_bin into publish output..." -ForegroundColor DarkGray
    if (-not (Test-Path $PublishClamAV)) {
        New-Item -ItemType Directory -Path $PublishClamAV -Force | Out-Null
    }
    Copy-Item -Path "$ClamAVBin\*" -Destination $PublishClamAV -Recurse -Force

    # Remove debug symbols, static libraries, and dev folders from publish staging
    Get-ChildItem -Path $PublishClamAV -Include "*.pdb", "*.lib", "*.exp" -Recurse -File | Remove-Item -Force -ErrorAction SilentlyContinue
    foreach ($devFolder in @("include", "certs", "UserManual")) {
        $devPath = Join-Path $PublishClamAV $devFolder
        if (Test-Path $devPath) { Remove-Item -Path $devPath -Recurse -Force -ErrorAction SilentlyContinue }
    }

    # Double check publish freshclam.conf has no BOM
    $pubConf = Join-Path $PublishClamAV "freshclam.conf"
    if (Test-Path $pubConf) {
        $rawBytes = [System.IO.File]::ReadAllBytes($pubConf)
        if ($rawBytes.Length -ge 3 -and $rawBytes[0] -eq 0xEF -and $rawBytes[1] -eq 0xBB -and $rawBytes[2] -eq 0xBF) {
            $noBomBytes = $rawBytes[3..($rawBytes.Length - 1)]
            [System.IO.File]::WriteAllBytes($pubConf, $noBomBytes)
        }
    }
}

# Stage Assets into publish output
$PublishAssets = Join-Path $PublishDir "Assets"
if (Test-Path $AssetsDir) {
    if (-not (Test-Path $PublishAssets)) {
        New-Item -ItemType Directory -Path $PublishAssets -Force | Out-Null
    }
    Copy-Item -Path "$AssetsDir\*" -Destination $PublishAssets -Recurse -Force
}

# Create Quarantine directory in publish output
$PublishQuarantine = Join-Path $PublishDir "Quarantine"
if (-not (Test-Path $PublishQuarantine)) {
    New-Item -ItemType Directory -Path $PublishQuarantine -Force | Out-Null
}

# Validate published executable
$ExePath = Join-Path $PublishDir "LibreScan.exe"
if (Test-Path $ExePath) {
    $sizeMB = ((Get-Item $ExePath).Length / 1MB).ToString('F1')
    Write-Host "  Published: LibreScan.exe ($sizeMB MB)" -ForegroundColor Green
}
else {
    throw "Published executable not found: $ExePath"
}

# -----------------------------------------------------------------------------
# STEP 6: Compile Inno Setup Installer
# -----------------------------------------------------------------------------
if (-not $SkipInstaller) {
    Write-Host "`n[6/6] Compiling Inno Setup installer..." -ForegroundColor Cyan

    $IssFile = Join-Path $ProjectRoot "installer.iss"
    if (-not (Test-Path $IssFile)) {
        throw "Inno Setup script not found: $IssFile"
    }

    # Search for ISCC.exe in standard install locations
    $IsccCandidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
    )

    $IsccExe = $IsccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $IsccExe) {
        Write-Warning "Inno Setup 6 (ISCC.exe) not found - skipping installer compilation."
        Write-Warning "Install from: https://jrsoftware.org/isdl.php"
        Write-Host ""
        Write-Host "  To compile manually after installing Inno Setup:" -ForegroundColor DarkGray
        Write-Host "    ISCC.exe `"$IssFile`"" -ForegroundColor DarkGray
    }
    else {
        Write-Host "  Using: $IsccExe" -ForegroundColor DarkGray
        & $IsccExe $IssFile
        if ($LASTEXITCODE -ne 0) {
            throw "Inno Setup compilation failed (exit code: $LASTEXITCODE)"
        }

        $OutputDir = Join-Path $ProjectRoot "Output"
        $Installers = Get-ChildItem -Path $OutputDir -Filter "*.exe" -ErrorAction SilentlyContinue
        foreach ($inst in $Installers) {
            $instSizeMB = (($inst.Length) / 1MB).ToString('F1')
            Write-Host "  Installer: $($inst.Name) ($instSizeMB MB)" -ForegroundColor Green
        }
    }
}
else {
    Write-Host "`n[6/6] Skipping Inno Setup compilation (-SkipInstaller)." -ForegroundColor Yellow
}

# -----------------------------------------------------------------------------
# Done
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "+------------------------------------------------------+" -ForegroundColor Green
Write-Host "|       Build pipeline completed successfully!         |" -ForegroundColor Green
Write-Host "+------------------------------------------------------+" -ForegroundColor Green
Write-Host ""
Write-Host "  Publish directory: $PublishDir" -ForegroundColor DarkGray
if (-not $SkipInstaller) {
    Write-Host "  Installer output:  $(Join-Path $ProjectRoot 'Output')" -ForegroundColor DarkGray
}
Write-Host ""
