# To run: powershell -ExecutionPolicy Bypass -File .\build_msix.ps1

# ===============================
#   CONFIGURATION
# ===============================

$makeappx = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe"
$signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"

# Path to your self-sign certificate
#$pfx = "C:\git\SmartMemeSearch\SmartMemeSearch.Wpf\MDTools.pfx"
#$pfxpass = "yourpassword"

# Build output folder (net8)
$build = "bin\Release\net8.0-windows\win-x64"

# Manifest files
$manifestFree = "AppxManifest_Free.xml"
$manifestPro  = "AppxManifest_Pro.xml"

# Output folders
$stagingFree = "msix_free"
$stagingPro  = "msix_pro"

# ===============================
#   CLEAN OLD OUTPUT
# ===============================

Remove-Item -Recurse -Force $stagingFree, $stagingPro -ErrorAction SilentlyContinue
New-Item -ItemType Directory $stagingFree | Out-Null
New-Item -ItemType Directory $stagingPro  | Out-Null

# ===============================
#   FUNCTION: COPY CLEAN FILESET
# ===============================

function Copy-AppFiles($source, $destination, $manifest) {

    Write-Host "Copying: $manifest"

    Copy-Item -Recurse -Force "$source\*" $destination

    # REMOVE forbidden WebView2 runtime folder
    $wv2 = Join-Path $destination "SmartMemeSearch.Wpf.exe.WebView2"
    if (Test-Path $wv2) { Remove-Item -Recurse -Force $wv2 }

    # REMOVE CUDA / TensorRT (NV GPU) DLLs
    $nv = @(
        "onnxruntime_providers_cuda.dll",
        "onnxruntime_providers_shared.dll",
        "onnxruntime_providers_tensorrt.dll",
        "onnxruntime_providers_cuda.lib",
        "onnxruntime_providers_shared.lib",
        "onnxruntime_providers_tensorrt.lib"
    )

    foreach ($n in $nv) {
        $f = Join-Path $destination $n
        if (Test-Path $f) { Remove-Item -Force $f }
    }

    # Copy manifest
    Copy-Item $manifest "$destination\AppxManifest.xml" -Force
}


# ──────────────────────────────────────────────────────────────
function Build-Version {
    param(
        [string]$configName,
        [bool]$isFree
    )

    Write-Host "======== BUILDING $configName ========" -ForegroundColor Cyan

    if ($isFree) {
        dotnet build $project -c Release -p:DefineConstants="MS_STORE_FREE_WITH_ADDS"
    }
    else {
        dotnet build $project -c Release -p:DefineConstants=""
    }
}

# ──────────────────────────────────────────────────────────────

# ===============================
#   BUILD FREE VERSION
# ===============================

Write-Host "`n=== Building FREE MSIX ==="

### Build FREE version
Build-Version -configName "FREE VERSION" -isFree $true

Copy-AppFiles $build $stagingFree $manifestFree

& $makeappx pack /d $stagingFree /p SmartMemeSearch.msix
#& $signtool sign /a /f $pfx /p $pfxpass SmartMemeSearch.msix

# ===============================
#   BUILD PRO VERSION
# ===============================

Write-Host "`n=== Building PRO MSIX ==="

### Build PRO version
Build-Version -configName "PRO VERSION" -isFree $false

Copy-AppFiles $build $stagingPro $manifestPro

& $makeappx pack /d $stagingPro /p SmartMemeSearchPro.msix
#& $signtool sign /a /f $pfx /p $pfxpass SmartMemeSearchPro.msix

Write-Host "`nDONE! Packages created:"
Write-Host " - SmartMemeSearch.msix"
Write-Host " - SmartMemeSearchPro.msix"
