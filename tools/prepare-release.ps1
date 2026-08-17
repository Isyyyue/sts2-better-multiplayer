[CmdletBinding()]
param(
    [string]$DotnetPath = 'dotnet',
    [string]$Sts2Path = '',
    [string]$BaseLibPath = '',
    [switch]$RequireClean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repoRoot 'BetterMultiplayer.csproj'
$testProjectPath = Join-Path $repoRoot 'tests\BetterMultiplayer.Tests.csproj'
$manifestPath = Join-Path $repoRoot 'BetterMultiplayer.json'
$modSourcePath = Join-Path $repoRoot 'BetterMultiplayerMod.cs'
$stageDir = Join-Path $repoRoot 'artifacts\staging\BetterMultiplayer'
$workshopSourceDir = Join-Path $repoRoot 'workshop'
$workshopConfigPath = Join-Path $workshopSourceDir 'workshop.json'
$workshopImagePath = Join-Path $workshopSourceDir 'image.png'
$workshopIdPath = Join-Path $workshopSourceDir 'mod_id.txt'
$uploadWorkspaceDir = Join-Path $repoRoot 'artifacts\workshop-upload'

$resolvedUploadWorkspaceDir = [System.IO.Path]::GetFullPath($uploadWorkspaceDir)
$expectedUploadWorkspaceDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\workshop-upload'))
if ($resolvedUploadWorkspaceDir -ne $expectedUploadWorkspaceDir) {
    throw "Refusing to recreate unexpected Workshop directory: $resolvedUploadWorkspaceDir"
}
if (Test-Path -LiteralPath $resolvedUploadWorkspaceDir) {
    Remove-Item -LiteralPath $resolvedUploadWorkspaceDir -Recurse -Force
}

if ($RequireClean) {
    $gitCommand = Get-Command git -ErrorAction Stop
    $gitStatus = & $gitCommand.Source -C $repoRoot status --porcelain --untracked-files=normal
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not inspect the Git worktree.'
    }
    if (@($gitStatus).Count -gt 0) {
        throw "The Git worktree must be clean for a release build:`n$($gitStatus -join "`n")"
    }
}

if ([string]::IsNullOrWhiteSpace($Sts2Path)) {
    $candidate = 'D:\Steam\steamapps\common\Slay the Spire 2'
    if (Test-Path -LiteralPath $candidate) {
        $Sts2Path = $candidate
    }
}

if ([string]::IsNullOrWhiteSpace($BaseLibPath)) {
    $candidate = 'D:\Steam\steamapps\workshop\content\2868840\3737335127\BaseLib\BaseLib.dll'
    if (Test-Path -LiteralPath $candidate) {
        $BaseLibPath = $candidate
    }
}

if ([string]::IsNullOrWhiteSpace($Sts2Path) -or -not (Test-Path -LiteralPath $Sts2Path -PathType Container)) {
    throw "Slay the Spire 2 directory not found. Pass -Sts2Path explicitly: $Sts2Path"
}

if ([string]::IsNullOrWhiteSpace($BaseLibPath) -or -not (Test-Path -LiteralPath $BaseLibPath -PathType Leaf)) {
    throw "BaseLib.dll not found. Pass -BaseLibPath explicitly: $BaseLibPath"
}

$dotnetCommand = Get-Command $DotnetPath -ErrorAction Stop
$dotnetExecutable = $dotnetCommand.Source
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$workshopConfig = Get-Content -LiteralPath $workshopConfigPath -Raw | ConvertFrom-Json
$workshopId = (Get-Content -LiteralPath $workshopIdPath -Raw).Trim()
$modSource = Get-Content -LiteralPath $modSourcePath -Raw
$sourceVersionMatch = [regex]::Match($modSource, 'public const string Version = "([^"]+)";')
[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$projectVersionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')

if (-not $sourceVersionMatch.Success) {
    throw 'Could not read BetterMultiplayerMod.Version from BetterMultiplayerMod.cs.'
}

$sourceVersion = $sourceVersionMatch.Groups[1].Value
$projectVersion = if ($null -eq $projectVersionNode) { '' } else { $projectVersionNode.InnerText }
if ($manifest.version -ne $sourceVersion -or $manifest.version -ne $projectVersion) {
    throw "Version mismatch: manifest=$($manifest.version), source=$sourceVersion, project=$projectVersion"
}

if ($workshopId -ne '3768337454') {
    throw "Unexpected Workshop ID: $workshopId"
}
if ($workshopConfig.PSObject.Properties.Name -contains 'preservePreviews') {
    throw 'workshop.json contains unsupported preservePreviews field.'
}
$dependencies = @($workshopConfig.dependencies)
if ($dependencies -notcontains 3737335127) {
    throw 'workshop.json must keep BaseLib 3737335127 as a required item.'
}
if ([string]::IsNullOrWhiteSpace($workshopConfig.minBranch) -or [string]::IsNullOrWhiteSpace($workshopConfig.maxBranch)) {
    throw 'workshop.json must define both minBranch and maxBranch.'
}
$workshopImage = Get-Item -LiteralPath $workshopImagePath -ErrorAction Stop
if ($workshopImage.Length -ge 1MB) {
    throw "Workshop image must be smaller than 1 MB: $($workshopImage.Length) bytes"
}

$resolvedStageDir = [System.IO.Path]::GetFullPath($stageDir)
$expectedStageDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\staging\BetterMultiplayer'))
if ($resolvedStageDir -ne $expectedStageDir) {
    throw "Refusing to recreate unexpected staging directory: $resolvedStageDir"
}
if (Test-Path -LiteralPath $resolvedStageDir) {
    Remove-Item -LiteralPath $resolvedStageDir -Recurse -Force
}

$msbuildProperties = @(
    "-p:STS2Path=$Sts2Path"
    "-p:BaseLibPath=$BaseLibPath"
)

Write-Host 'Testing Better Multiplayer (Release)...'
& $dotnetExecutable test $testProjectPath -c Release @msbuildProperties
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed with exit code $LASTEXITCODE."
}

Write-Host 'Building Better Multiplayer (Release)...'
& $dotnetExecutable build $projectPath -c Release @msbuildProperties
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

$builtDll = Join-Path $stageDir 'BetterMultiplayer.dll'
if (-not (Test-Path -LiteralPath $builtDll -PathType Leaf)) {
    throw "Release DLL was not staged at $builtDll"
}

$resolvedContentDir = Join-Path $resolvedUploadWorkspaceDir 'content'
New-Item -ItemType Directory -Path $resolvedContentDir | Out-Null
Copy-Item -LiteralPath $builtDll -Destination $resolvedContentDir
Copy-Item -LiteralPath $manifestPath -Destination $resolvedContentDir
Copy-Item -LiteralPath $workshopConfigPath -Destination $resolvedUploadWorkspaceDir
Copy-Item -LiteralPath $workshopImagePath -Destination $resolvedUploadWorkspaceDir
Copy-Item -LiteralPath $workshopIdPath -Destination $resolvedUploadWorkspaceDir

$branchSupportPath = Join-Path $resolvedContentDir 'workshop-branch-support.txt'
$branchSupport = "Better Multiplayer $($manifest.version)`nSupported Steam branches: $($workshopConfig.maxBranch) through $($workshopConfig.minBranch).`n"
[System.IO.File]::WriteAllText($branchSupportPath, $branchSupport, [System.Text.UTF8Encoding]::new($false))

$contentFiles = @(Get-ChildItem -LiteralPath $resolvedContentDir -File)
$expectedNames = @('BetterMultiplayer.dll', 'BetterMultiplayer.json', 'workshop-branch-support.txt')
$unexpectedNames = @($contentFiles.Name | Where-Object { $_ -notin $expectedNames })
if ($contentFiles.Count -ne 3 -or $unexpectedNames.Count -gt 0) {
    throw 'Workshop content contains unexpected files.'
}

$rootItems = @(Get-ChildItem -LiteralPath $resolvedUploadWorkspaceDir)
$expectedRootNames = @('content', 'image.png', 'mod_id.txt', 'workshop.json')
$unexpectedRootNames = @($rootItems.Name | Where-Object { $_ -notin $expectedRootNames })
if ($rootItems.Count -ne 4 -or $unexpectedRootNames.Count -gt 0) {
    throw 'Workshop upload workspace contains unexpected entries.'
}

$packagedManifest = Get-Content -LiteralPath (Join-Path $resolvedContentDir 'BetterMultiplayer.json') -Raw | ConvertFrom-Json
$dll = Get-Item -LiteralPath (Join-Path $resolvedContentDir 'BetterMultiplayer.dll')
$hash = Get-FileHash -LiteralPath $dll.FullName -Algorithm SHA256
$productVersion = $dll.VersionInfo.ProductVersion

if ($packagedManifest.version -ne $manifest.version) {
    throw "Packaged manifest version mismatch: expected $($manifest.version), got $($packagedManifest.version)"
}
if ($productVersion -ne $manifest.version -and -not $productVersion.StartsWith("$($manifest.version)+", [System.StringComparison]::Ordinal)) {
    throw "DLL ProductVersion mismatch: expected $($manifest.version), got $productVersion"
}
if ($RequireClean) {
    $headCommit = (& $gitCommand.Source -C $repoRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $productVersion -ne "$($manifest.version)+$headCommit") {
        throw "Release DLL does not identify HEAD ${headCommit}: $productVersion"
    }
}

Write-Host ''
Write-Host "Prepared Workshop upload workspace: $resolvedUploadWorkspaceDir"
Write-Host "Mod version: $($manifest.version)"
Write-Host "DLL ProductVersion: $productVersion"
Write-Host "DLL SHA256: $($hash.Hash)"
Write-Host "Content bytes: $(($contentFiles | Measure-Object -Property Length -Sum).Sum)"
