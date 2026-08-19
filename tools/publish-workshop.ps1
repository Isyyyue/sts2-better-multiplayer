[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$UploaderDirectory,
    [string]$WorkspacePath = '',
    [string]$WorkshopId = '3768337454',
    [switch]$ValidateOnly
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
$versions = @(@(
    $sourceManifest.version
    $packagedManifest.version
    $sourceVersionMatch.Groups[1].Value
    $projectVersionNode.InnerText
) | Select-Object -Unique)
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
$expectedContentBytes = [int64]((Get-ChildItem -LiteralPath $workspaceContentPath -File | Measure-Object -Property Length -Sum).Sum)

$publishedFileDetailsUri = 'https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/'
function Get-PublishedFileDetails {
    param(
        [Parameter(Mandatory)]
        [string]$PublishedFileId
    )

    try {
        $response = Invoke-RestMethod -Method Post -Uri $publishedFileDetailsUri -Body @{
            itemcount = '1'
            'publishedfileids[0]' = $PublishedFileId
        } -TimeoutSec 20
        $details = @($response.response.publishedfiledetails)[0]
        if ($null -eq $details -or [int]$details.result -ne 1) {
            return $null
        }
        return $details
    }
    catch {
        Write-Verbose "Could not query Steam Workshop details: $($_.Exception.Message)"
        return $null
    }
}

if ($ValidateOnly) {
    $dllHash = (Get-FileHash -LiteralPath $packagedDll.FullName -Algorithm SHA256).Hash
    Write-Host "Workshop upload validation passed: $WorkshopId"
    Write-Host "DLL ProductVersion: $($packagedDll.VersionInfo.ProductVersion)"
    Write-Host "DLL SHA256: $dllHash"
    return
}

if ($null -eq (Get-Process -Name steam -ErrorAction SilentlyContinue)) {
    throw 'Steam desktop client is not running.'
}
if ($null -ne (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue)) {
    throw 'Slay the Spire 2 is running. Close the game before uploading.'
}

$detailsBefore = Get-PublishedFileDetails -PublishedFileId $WorkshopId
$previousManifest = if ($null -eq $detailsBefore) { '' } else { [string]$detailsBefore.hcontent_file }
$previousUpdated = if ($null -eq $detailsBefore) { 0L } else { [int64]$detailsBefore.time_updated }

$uploadExitCode = 1
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
$successText = "Successfully uploaded '更好的联机 / Better Multiplayer' to the workshop with id $WorkshopId!"
$uploaderReportedSuccess = $uploadExitCode -eq 0 -and $logText.Contains($successText)

# Steam can commit the update while the uploader times out waiting for the final callback.
# Poll the published item before deciding whether a retry is necessary.
$detailsAfter = $null
$remoteCommitVerified = $false
$verificationDeadline = (Get-Date).AddSeconds(90)
do {
    $detailsAfter = Get-PublishedFileDetails -PublishedFileId $WorkshopId
    if ($null -ne $detailsAfter) {
        $currentManifest = [string]$detailsAfter.hcontent_file
        $currentUpdated = [int64]$detailsAfter.time_updated
        $currentSize = [int64]$detailsAfter.file_size
        $remoteChanged = $null -eq $detailsBefore -or $currentManifest -ne $previousManifest -or $currentUpdated -gt $previousUpdated
        if ($remoteChanged -and $currentSize -eq $expectedContentBytes) {
            $remoteCommitVerified = $true
            break
        }
    }
    if ((Get-Date) -lt $verificationDeadline) {
        Start-Sleep -Seconds 5
    }
} while ((Get-Date) -lt $verificationDeadline)

if (-not $remoteCommitVerified) {
    throw "Workshop upload could not be verified. Exit=$uploadExitCode. The uploader log was not archived so a retry will not create a duplicate release record."
}

if (-not $uploaderReportedSuccess) {
    Write-Warning 'The uploader reported a timeout or non-success result, but Steam API confirms the new content was committed. Do not retry this upload.'
}

$recordDirectory = Join-Path $repoRoot 'artifacts\release-records'
New-Item -ItemType Directory -Path $recordDirectory -Force | Out-Null
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$recordPath = Join-Path $recordDirectory "mod-uploader-$timestamp.log"
Copy-Item -LiteralPath $logPath -Destination $recordPath

Write-Host "Workshop upload completed: $WorkshopId"
Write-Host "Archived uploader log: $recordPath"
