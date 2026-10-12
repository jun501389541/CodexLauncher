param(
    [Parameter(Mandatory)][string]$PublishDir,
    [string]$CompilerPath = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\dist')
)

$ErrorActionPreference = 'Stop'
$payload = (Resolve-Path -LiteralPath $PublishDir).Path
if (-not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw "Inno Setup compiler not found: $CompilerPath"
}
foreach ($name in @('CodexLauncher.exe', 'e_sqlite3.dll', 'THIRD-PARTY-NOTICES.md', 'licenses')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $name))) {
        throw "Publish payload is missing $name"
    }
}
foreach ($name in @('CodexLauncher.portable', 'portable-data')) {
    if (Test-Path -LiteralPath (Join-Path $payload $name)) {
        throw "Installer payload must not contain $name. Use a clean self-contained publish directory."
    }
}
$fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $payload 'CodexLauncher.exe')).FileVersion
if ($fileVersion -notmatch '^(\d+\.\d+\.\d+)\.0$') {
    throw "Unexpected application file version: $fileVersion"
}
$appVersion = $Matches[1]
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDir).Path
& $CompilerPath "/DAppVersion=$appVersion" "/DPublishDir=$payload" "/O$output" (Join-Path $PSScriptRoot 'CodexLauncher.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed: $LASTEXITCODE" }
$setup = Join-Path $output "CodexLauncher-v$appVersion-win-x64-setup.exe"
if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) { throw 'Compiler did not produce the expected installer.' }
if ([System.Diagnostics.FileVersionInfo]::GetVersionInfo($setup).FileVersion.Trim() -ne $fileVersion) {
    throw 'Installer version does not match its application payload.'
}
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path -Leaf $setup)" | Set-Content -LiteralPath "$setup.sha256" -Encoding ascii
Write-Output "Built $setup; SHA256=$hash"
