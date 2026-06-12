<#
.SYNOPSIS
    Stamp a release version into every hand-maintained version reference.

.DESCRIPTION
    Single source of truth for the per-release version bump. Updates:
      - SW2RD/AssemblyInfo.cs : AssemblyVersion + AssemblyFileVersion ("X.Y.Z.0")
      - INSTALL/Install.iss   : #define MyAppVersion "X.Y.Z"

    AssemblyVersion is bumped ONLY on real releases (a committed literal, never
    a wildcard) to avoid the registry-subkey pollution documented in AGENTS.md.

    Idempotent: re-running with the same version is a no-op.

.PARAMETER Version
    The release version in X.Y.Z form (e.g. 0.2.0). No leading "v".

.EXAMPLE
    pwsh ./scripts/SetVersion.ps1 -Version 0.2.0
#>
param (
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version '$Version' is not in X.Y.Z form (e.g. 0.2.0). Do not include a leading 'v'."
}

$AssemblyVersion = "$Version.0"

# Resolve paths relative to this script so it works from any working directory.
$RepoRoot       = Split-Path -Parent $PSScriptRoot
$AssemblyInfo   = Join-Path $RepoRoot 'SW2RD\AssemblyInfo.cs'
$InstallScript  = Join-Path $RepoRoot 'INSTALL\Install.iss'

function Update-FileContent {
    param(
        [string]$Path,
        [string]$Pattern,
        [string]$Replacement
    )
    if (-not (Test-Path $Path)) {
        throw "Expected file not found: $Path"
    }
    $content = Get-Content -Path $Path -Raw
    $updated = [System.Text.RegularExpressions.Regex]::Replace($content, $Pattern, $Replacement)
    if ($updated -eq $content) {
        if ($content -notmatch $Pattern) {
            throw "Pattern '$Pattern' did not match anything in $Path. The file layout may have changed."
        }
        Write-Host "No change needed in $Path (already at $Version)."
    } else {
        # Preserve the file's existing bytes/encoding behavior; -NoNewline keeps
        # the trailing newline state intact since $content was read with -Raw.
        Set-Content -Path $Path -Value $updated -NoNewline
        Write-Host "Updated $Path"
    }
}

# AssemblyInfo.cs: [assembly: AssemblyVersion("X.Y.Z.0")] / AssemblyFileVersion
Update-FileContent -Path $AssemblyInfo `
    -Pattern 'AssemblyVersion\("[^"]*"\)' `
    -Replacement "AssemblyVersion(`"$AssemblyVersion`")"
Update-FileContent -Path $AssemblyInfo `
    -Pattern 'AssemblyFileVersion\("[^"]*"\)' `
    -Replacement "AssemblyFileVersion(`"$AssemblyVersion`")"

# Install.iss: #define MyAppVersion "X.Y.Z"
Update-FileContent -Path $InstallScript `
    -Pattern '#define MyAppVersion "[^"]*"' `
    -Replacement "#define MyAppVersion `"$Version`""

Write-Host "Version stamped: $Version (assembly: $AssemblyVersion)"
