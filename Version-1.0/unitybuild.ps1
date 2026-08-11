param(
    # Auto-detects the exact name of the parent folder
    [string] $ModName = (Split-Path (Split-Path $PSScriptRoot -Parent) -Leaf),
    
    # Points to your Unity project (up 3 levels to 'repos', then into 'timberborn-modding-main')
    [string] $UnityProjectPath = [System.IO.Path]::GetFullPath("$PSScriptRoot\..\..\..\timberborn-modding-main"),
    
    # Output folder defaults to the current script directory
    [string] $OutputFolder = $PSScriptRoot,

    # Auto-detects version from the current folder leaf (e.g., 'Version-1.0' -> '1.0')
    [string] $CompatibilityVersion = ((Split-Path $PSScriptRoot -Leaf) -replace '^Version-', '')
)

$ErrorActionPreference = "Stop"

Write-Host "======================================================="
# FIXED: Wrapped in $() so the colon doesn't break the parser
Write-Host "$($ModName): UNITY ASSET BUNDLE EXPORT STARTING"
Write-Host "======================================================="

$projectRoot = [System.IO.Path]::GetFullPath($UnityProjectPath)
$targetOutput = [System.IO.Path]::GetFullPath($OutputFolder)
$logPath = Join-Path $targetOutput "unitybuild.log"

# 1. Auto-Detect Unity Version
$versionFile = Join-Path $projectRoot "ProjectSettings/ProjectVersion.txt"
$versionText = Get-Content -Raw -LiteralPath $versionFile
$unityVersion = [regex]::Match($versionText, "(?m)^m_EditorVersion:\s*(\S+)").Groups[1].Value
$unityExe = "C:\Program Files\Unity\Hub\Editor\$unityVersion\Editor\Unity.exe"

if (-not (Test-Path $unityExe)) {
    Write-Error "Could not find Unity.exe at $unityExe. Make sure Unity Hub is installed in the default location!"
    exit
}

# 2. Command Line Arguments for Native Wrapper
$unityArguments = @(
    "-batchmode", "-quit",
    "-projectPath", "`"$projectRoot`"",
    "-executeMethod", "NativeModBuilderBatch.Build",
    "-mod", "`"$ModName`"",
    "-logFile", "`"$logPath`""
)

if ($CompatibilityVersion) {
    $unityArguments += @("-compatibilityVersion", "`"$CompatibilityVersion`"")
}

Write-Host "Running Unity export for $ModName..."
$process = Start-Process -FilePath $unityExe -ArgumentList ($unityArguments -join " ") -WindowStyle Hidden -Wait -PassThru

if ($process.ExitCode -eq 0) {
    # 3. Post-Build Routing
    $liveModFolder = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "Timberborn\Mods\$ModName"
    $generatedBundles = Get-ChildItem -Path $liveModFolder -Filter "AssetBundles" -Recurse -Directory | Select-Object -First 1

    if ($null -ne $generatedBundles) {
        Write-Host "Routing AssetBundles to $targetOutput..."
        
        $finalDestination = Join-Path $targetOutput "AssetBundles"
        if (-not (Test-Path $finalDestination)) { New-Item -ItemType Directory -Path $finalDestination -Force | Out-Null }

        # Copy the bundles to your source code directory
        Copy-Item -Path "$($generatedBundles.FullName)\*" -Destination $finalDestination -Recurse -Force
        
        # ALL REMOVE-ITEM/DELETE COMMANDS HAVE BEEN REMOVED FROM THIS SCRIPT.
        
        Write-Host "Unity Pipeline Complete! AssetBundles copied safely." -ForegroundColor Green
    } else {
        Write-Warning "Could not find AssetBundles folder in $liveModFolder. Check unitybuild.log"
    }
} else {
    Write-Warning "Unity build failed with exit code $($process.ExitCode)."
    Write-Host "--- LAST 20 LINES OF UNITY LOG ---" -ForegroundColor Yellow
    if (Test-Path $logPath) {
        Get-Content $logPath -Tail 20 | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    }
}

Write-Host "======================================================="
Write-Host " ASSET EXPORT COMPLETE." -ForegroundColor Green
pause