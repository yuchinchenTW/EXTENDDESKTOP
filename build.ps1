$ErrorActionPreference = "Stop"

$compiler = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $compiler)) {
    $compiler = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

if (-not (Test-Path $compiler)) {
    throw "csc.exe was not found."
}

$root = $PSScriptRoot
$outDir = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$refs = @(
    "/r:System.dll"
    "/r:System.Core.dll"
    "/r:System.Drawing.dll"
    "/r:System.Windows.Forms.dll"
)

$shared = @(
    (Join-Path $root "Shared\Protocol.cs")
    (Join-Path $root "Shared\DiscoveryProtocol.cs")
    (Join-Path $root "Shared\MFInterop.cs")
    (Join-Path $root "Shared\PixelConvert.cs")
)

$hostSources = @(
    (Join-Path $root "Host\Program.cs")
    (Join-Path $root "Host\HostForm.cs")
    (Join-Path $root "Host\DisplayHostServer.cs")
    (Join-Path $root "Host\ScreenCaptureStreamer.cs")
    (Join-Path $root "Host\H264Encoder.cs")
    (Join-Path $root "Host\HostDiscoveryBroadcaster.cs")
) + $shared

$receiverSources = @(
    (Join-Path $root "Receiver\Program.cs")
    (Join-Path $root "Receiver\ReceiverForm.cs")
    (Join-Path $root "Receiver\DisplayReceiverClient.cs")
    (Join-Path $root "Receiver\H264Decoder.cs")
    (Join-Path $root "Receiver\FrameBitmapPool.cs")
    (Join-Path $root "Receiver\HostDiscoveryListener.cs")
) + $shared

$hostOut = "/out:" + (Join-Path $outDir "ExtentDesktopHost.exe")
$receiverOut = "/out:" + (Join-Path $outDir "ExtentDesktopReceiver.exe")

& $compiler /nologo /unsafe /target:winexe $hostOut $refs $hostSources
if ($LASTEXITCODE -ne 0) {
    throw "Host build failed."
}

& $compiler /nologo /unsafe /target:winexe $receiverOut $refs $receiverSources
if ($LASTEXITCODE -ne 0) {
    throw "Receiver build failed."
}

Write-Output "Built:"
Get-ChildItem $outDir | Select-Object Name, Length
