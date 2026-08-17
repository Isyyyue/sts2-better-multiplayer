[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$UploaderDirectory,
    [string]$WorkspacePath = '',
    [string]$WorkshopId = '3768337454'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($WorkspacePath)) {
    $WorkspacePath = Join-Path $repoRoot 'artifacts\workshop-upload'
}

$uploaderDirectoryFull = [System.IO.Path]::GetFullPath($UploaderDirectory)
$workspaceFull = [System.IO.Path]::GetFullPath($WorkspacePath)
$uploaderPath = Join-Path $uploaderDirectoryFull 'ModUploader.exe'
$steamApiPath = Join-Path $uploaderDirectoryFull 'steam_api64.dll'
$steamAppIdPath = Join-Path $uploaderDirectoryFull 'steam_appid.txt'
$logPath = Join-Path $uploaderDirectoryFull 'mod-uploader.log'

foreach ($requiredFile in @($uploaderPath, $steamApiPath, $steamAppIdPath)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Official uploader file not found: $requiredFile"
    }
}

if ((Get-Content -LiteralPath $steamAppIdPath -Raw).Trim() -ne '2868840') {
    throw 'steam_appid.txt does not target Slay the Spire 2 (2868840).'
}

$workspaceIdPath = Join-Path $workspaceFull 'mod_id.txt'
$workspaceConfigPath = Join-Path $workspaceFull 'workshop.json'
$workspaceImagePath = Join-Path $workspaceFull 'image.png'
$workspaceContentPath = Join-Path $workspaceFull 'content'
foreach ($requiredPath in @($workspaceIdPath, $workspaceConfigPath, $workspaceImagePath, $workspaceContentPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Workshop workspace entry not found: $requiredPath"
    }
}

if ((Get-Content -LiteralPath $workspaceIdPath -Raw).Trim() -ne $WorkshopId) {
    throw "Workspace ID does not match requested Workshop ID $WorkshopId."
}
if (Test-Path -LiteralPath (Join-Path $workspaceFull 'previews')) {
    throw 'Upload workspace must not contain previews; their presence would resynchronize remote preview images.'
}

$rootEntries = @(Get-ChildItem -LiteralPath $workspaceFull -Force)
$expectedRootNames = @('content', 'image.png', 'mod_id.txt', 'workshop.json')
$unexpectedRootNames = @($rootEntries.Name | Where-Object { $_ -notin $expectedRootNames })
if ($rootEntries.Count -ne 4 -or $unexpectedRootNames.Count -gt 0) {
    throw 'Upload workspace root contains unexpected entries.'
}

$expectedContentNames = @('BetterMultiplayer.dll', 'BetterMultiplayer.json', 'workshop-branch-support.txt')
$contentEntries = @(Get-ChildItem -LiteralPath $workspaceContentPath -Force -Recurse)
$contentDirectories = @($contentEntries | Where-Object { $_.PSIsContainer })
$contentFiles = @($contentEntries | Where-Object { -not $_.PSIsContainer })
$unexpectedContentNames = @($contentFiles.Name | Where-Object { $_ -notin $expectedContentNames })
if ($contentDirectories.Count -gt 0 -or $contentFiles.Count -ne 3 -or $unexpectedContentNames.Count -gt 0) {
    throw 'Upload workspace content does not match the expected three-file package.'
}

$gitCommand = Get-Command git -ErrorAction Stop
$gitStatus = & $gitCommand.Source -C $repoRoot status --porcelain --untracked-files=normal
if ($LASTEXITCODE -ne 0) {
    throw 'Could not inspect the Git worktree.'
}
if (@($gitStatus).Count -gt 0) {
    throw "The Git worktree must be clean before uploading:`n$($gitStatus -join "`n")"
}

$sourceManifest = Get-Content -LiteralPath (Join-Path $repoRoot 'BetterMultiplayer.json') -Raw | ConvertFrom-Json
$packagedManifest = Get-Content -LiteralPath (Join-Path $workspaceContentPath 'BetterMultiplayer.json') -Raw | ConvertFrom-Json
$modSource = Get-Content -LiteralPath (Join-Path $repoRoot 'BetterMultiplayerMod.cs') -Raw
$sourceVersionMatch = [regex]::Match($modSource, 'public const string Version = "([^"]+)";')
[xml]$project = Get-Content -LiteralPath (Join-Path $repoRoot 'BetterMultiplayer.csproj') -Raw
$projectVersionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')
if (-not $sourceVersionMatch.Success -or $null -eq $projectVersionNode) {
    throw 'Could not read all version sources.'
}
$versions = @(
    $sourceManifest.version
    $packagedManifest.version
    $sourceVersionMatch.Groups[1].Value
    $projectVersionNode.InnerText
) | Select-Object -Unique
if ($versions.Count -ne 1) {
    throw "Version mismatch before upload: $($versions -join ', ')"
}

$filePairs = @(
    [pscustomobject]@{ Source = Join-Path $repoRoot 'BetterMultiplayer.json'; Packaged = Join-Path $workspaceContentPath 'BetterMultiplayer.json' }
    [pscustomobject]@{ Source = Join-Path $repoRoot 'workshop\workshop.json'; Packaged = $workspaceConfigPath }
    [pscustomobject]@{ Source = Join-Path $repoRoot 'workshop\image.png'; Packaged = $workspaceImagePath }
    [pscustomobject]@{ Source = Join-Path $repoRoot 'workshop\mod_id.txt'; Packaged = $workspaceIdPath }
    [pscustomobject]@{ Source = Join-Path $repoRoot 'artifacts\staging\BetterMultiplayer\BetterMultiplayer.dll'; Packaged = Join-Path $workspaceContentPath 'BetterMultiplayer.dll' }
)
foreach ($pair in $filePairs) {
    $sourceHash = (Get-FileHash -LiteralPath $pair.Source -Algorithm SHA256 -ErrorAction Stop).Hash
    $packagedHash = (Get-FileHash -LiteralPath $pair.Packaged -Algorithm SHA256 -ErrorAction Stop).Hash
    if ($sourceHash -ne $packagedHash) {
        throw "Packaged file differs from its source: $($pair.Packaged)"
    }
}

$workshopConfig = Get-Content -LiteralPath $workspaceConfigPath -Raw | ConvertFrom-Json
$expectedBranchSupport = "Better Multiplayer $($sourceManifest.version)`nSupported Steam branches: $($workshopConfig.maxBranch) through $($workshopConfig.minBranch).`n"
$actualBranchSupport = Get-Content -LiteralPath (Join-Path $workspaceContentPath 'workshop-branch-support.txt') -Raw
if ($actualBranchSupport -ne $expectedBranchSupport) {
    throw 'workshop-branch-support.txt does not match the source configuration.'
}

$headCommit = (& $gitCommand.Source -C $repoRoot rev-parse HEAD).Trim()
$packagedDll = Get-Item -LiteralPath (Join-Path $workspaceContentPath 'BetterMultiplayer.dll')
$expectedProductVersion = "$($sourceManifest.version)+$headCommit"
if ($packagedDll.VersionInfo.ProductVersion -ne $expectedProductVersion) {
    throw "DLL ProductVersion must be $expectedProductVersion, got $($packagedDll.VersionInfo.ProductVersion)"
}

if ($null -eq (Get-Process -Name steam -ErrorAction SilentlyContinue)) {
    throw 'Steam desktop client is not running.'
}
if ($null -ne (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue)) {
    throw 'Slay the Spire 2 is running. Close the game before uploading.'
}

Push-Location -LiteralPath $uploaderDirectoryFull
try {
    & $uploaderPath upload --workspace $workspaceFull --id $WorkshopId
    $uploadExitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
    throw "Uploader did not create its log: $logPath"
}

$logText = Get-Content -LiteralPath $logPath -Raw
$recordDirectory = Join-Path $repoRoot 'artifacts\release-records'
New-Item -ItemType Directory -Path $recordDirectory -Force | Out-Null
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$recordPath = Join-Path $recordDirectory "mod-uploader-$timestamp.log"
Copy-Item -LiteralPath $logPath -Destination $recordPath

$successText = "Successfully uploaded '更好的联机 / Better Multiplayer' to the workshop with id $WorkshopId!"
$knownFailurePattern = '(?im)failed|couldn''t|error occurred|workshop legal agreement'
if ($uploadExitCode -ne 0 -or $logText -match $knownFailurePattern -or -not $logText.Contains($successText)) {
    throw "Workshop upload could not be verified. Exit=$uploadExitCode. Review $recordPath"
}

Write-Host "Workshop upload completed: $WorkshopId"
Write-Host "Archived uploader log: $recordPath"
