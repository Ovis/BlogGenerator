[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ThemeRoot,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ArticleRoot,

    [ValidateRange(1, 65535)]
    [int]$Port = 8765,

    [switch]$NoServer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$generatorRoot = Split-Path -Parent $PSCommandPath
$blogRoot = Split-Path -Parent $generatorRoot
$ThemeRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ThemeRoot)
$ArticleRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ArticleRoot)
$outputRoot = Join-Path $blogRoot 'output'
$solutionPath = Join-Path $generatorRoot 'BlogGenerator.sln'
$generatorDll = Join-Path $generatorRoot 'src\bin\Debug\net8.0\BlogGenerator.dll'
$themeDirectory = Join-Path $ThemeRoot 'templates'
$configPath = Join-Path $ThemeRoot 'blogconfig.json'
$runId = [guid]::NewGuid().ToString('N')
$articleStagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("BlogGenerator-articles-$runId")
$generatedOutputRoot = "$outputRoot.next-$runId"

function Assert-CommandExists {
    param(
        [Parameter(Mandatory)]
        [string]$Name
    )

    if (-not (Get-Command -Name $Name -ErrorAction SilentlyContinue)) {
        throw "Required command was not found: $Name"
    }
}

function Assert-PathExists {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description was not found: $Path"
    }
}

function Assert-LastExitCode {
    param(
        [Parameter(Mandatory)]
        [string]$Operation
    )

    if ($LASTEXITCODE -ne 0) {
        throw "$Operation failed with exit code $LASTEXITCODE."
    }
}

function Publish-GeneratedOutput {
    $expectedOutputRoot = [System.IO.Path]::GetFullPath((Join-Path $blogRoot 'output'))
    $actualOutputRoot = [System.IO.Path]::GetFullPath($outputRoot)
    $actualGeneratedOutputRoot = [System.IO.Path]::GetFullPath($generatedOutputRoot)

    if (-not [string]::Equals($expectedOutputRoot, $actualOutputRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace an unexpected output directory: $actualOutputRoot"
    }

    $expectedGeneratedPrefix = "$expectedOutputRoot.next-"
    if (-not $actualGeneratedOutputRoot.StartsWith($expectedGeneratedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to publish an unexpected generated directory: $actualGeneratedOutputRoot"
    }

    $backupRoot = $null
    if (Test-Path -LiteralPath $actualOutputRoot) {
        $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $backupRoot = "$actualOutputRoot.previous-$timestamp-$($runId.Substring(0, 8))"
        Write-Host "[Backup] $actualOutputRoot -> $backupRoot"
        Move-Item -LiteralPath $actualOutputRoot -Destination $backupRoot
    }

    try {
        Move-Item -LiteralPath $actualGeneratedOutputRoot -Destination $actualOutputRoot
    }
    catch {
        if ($backupRoot -and
            (Test-Path -LiteralPath $backupRoot) -and
            -not (Test-Path -LiteralPath $actualOutputRoot)) {
            Move-Item -LiteralPath $backupRoot -Destination $actualOutputRoot
        }

        throw
    }

    return $backupRoot
}

Assert-CommandExists -Name 'dotnet'
Assert-CommandExists -Name 'robocopy'
Assert-PathExists -Path $solutionPath -Description 'BlogGenerator solution'
Assert-PathExists -Path $ArticleRoot -Description 'Article root'
Assert-PathExists -Path $themeDirectory -Description 'Theme directory'
Assert-PathExists -Path $configPath -Description 'Blog configuration'

Write-Host '[Build] BlogGenerator'
Push-Location $generatorRoot
try {
    & dotnet build $solutionPath -v minimal
    Assert-LastExitCode -Operation 'BlogGenerator build'
}
finally {
    Pop-Location
}

try {
    New-Item -ItemType Directory -Path $articleStagingRoot | Out-Null
    New-Item -ItemType Directory -Path $generatedOutputRoot | Out-Null

    Write-Host '[Prepare] Article input without repository metadata'
    $robocopyArguments = @(
        $ArticleRoot,
        $articleStagingRoot,
        '/E',
        '/XD',
        (Join-Path $ArticleRoot '.git'),
        (Join-Path $ArticleRoot '.github'),
        (Join-Path $ArticleRoot '.agents'),
        (Join-Path $ArticleRoot '.codex'),
        '/R:2',
        '/W:1',
        '/NFL',
        '/NDL',
        '/NJH',
        '/NJS',
        '/NP'
    )
    & robocopy @robocopyArguments
    $robocopyExitCode = $LASTEXITCODE
    if ($robocopyExitCode -gt 7) {
        throw "Article staging failed with robocopy exit code $robocopyExitCode."
    }

    Write-Host '[Generate] Static blog'
    & dotnet $generatorDll `
        --input $articleStagingRoot `
        --output $generatedOutputRoot `
        --theme $themeDirectory `
        --config $configPath
    Assert-LastExitCode -Operation 'Blog generation'

    $requiredOutputFiles = @(
        'index.html',
        'feed.rss',
        'feed.atom',
        'css\blog.css',
        'js\blog.js'
    )
    foreach ($relativePath in $requiredOutputFiles) {
        Assert-PathExists `
            -Path (Join-Path $generatedOutputRoot $relativePath) `
            -Description "Generated file '$relativePath'"
    }

    if (Test-Path -LiteralPath (Join-Path $generatedOutputRoot '.git')) {
        throw 'Repository metadata was unexpectedly copied into the output directory.'
    }

    $generatedFileCount = @(Get-ChildItem -LiteralPath $generatedOutputRoot -Recurse -File).Count
    $backupRoot = Publish-GeneratedOutput
    Write-Host "[Completed] Generated $generatedFileCount files in $outputRoot"
    if ($backupRoot) {
        Write-Host "[Completed] Previous output is preserved in $backupRoot"
    }
}
finally {
    if (Test-Path -LiteralPath $articleStagingRoot) {
        Remove-Item -LiteralPath $articleStagingRoot -Recurse -Force
    }

    if (Test-Path -LiteralPath $generatedOutputRoot) {
        Remove-Item -LiteralPath $generatedOutputRoot -Recurse -Force
    }
}

if ($NoServer) {
    return
}

Assert-CommandExists -Name 'python'

$previewUrl = "http://127.0.0.1:$Port/"
Write-Host "[Serve] $previewUrl"
Write-Host 'Press Ctrl+C to stop the preview server.'
& python -m http.server $Port --bind 127.0.0.1 --directory $outputRoot
