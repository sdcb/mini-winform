[CmdletBinding(DefaultParameterSetName = 'Launch')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Launch')]
    [string]$ExePath,

    [Parameter(Mandatory = $true, ParameterSetName = 'Attach')]
    [int]$ProcessId,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [int]$Quality = 80,

    [ValidateSet('PrintWindow', 'Screen')]
    [string]$CaptureMethod = 'PrintWindow',

    [int]$TimeoutSeconds = 10,

    [string]$WindowTitle,

    [string[]]$ArgumentList = @()
)

$ErrorActionPreference = 'Stop'

if ($PSCmdlet.ParameterSetName -eq 'Launch' -and -not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
    throw "Executable not found: $ExePath"
}

if ($Quality -lt 1 -or $Quality -gt 100) {
    throw 'Quality must be between 1 and 100.'
}

$outputDirectory = Split-Path -Path $OutputPath -Parent
if ($outputDirectory) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

Add-Type -AssemblyName System.Drawing

if (-not ('NativePidWindowCapture' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class NativePidWindowCapture
{
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
}
'@
}

if (-not ('NativePidWindowTitleFilter' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class NativePidWindowTitleFilter
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowTextLength(IntPtr hWnd);
}
'@
}

if (-not ('NativePidWindowActivation' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class NativePidWindowActivation
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
}
'@
}

function Find-VisibleTopLevelWindowByProcessId {
    param(
        [Parameter(Mandatory = $true)]
        [int]$ProcessId,

        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process,

        [Parameter(Mandatory = $true)]
        [DateTime]$Deadline
    )

    $GW_OWNER = 4

    while ([DateTime]::UtcNow -lt $Deadline) {
        $script:foundWindow = [IntPtr]::Zero

        [NativePidWindowCapture+EnumWindowsProc]$callback = {
            param([IntPtr]$candidate, [IntPtr]$lParam)

            [uint32]$candidateProcessId = 0
            [void][NativePidWindowCapture]::GetWindowThreadProcessId($candidate, [ref]$candidateProcessId)

            $ownedWindowMatchesRequest = -not [string]::IsNullOrWhiteSpace($WindowTitle) -or
                [NativePidWindowCapture]::GetWindow($candidate, $GW_OWNER) -eq [IntPtr]::Zero

            if ($candidateProcessId -eq $ProcessId -and
                [NativePidWindowCapture]::IsWindowVisible($candidate) -and
                $ownedWindowMatchesRequest) {
                if (-not [string]::IsNullOrWhiteSpace($WindowTitle)) {
                    $candidateTitle = Get-NativeWindowTitle -WindowHandle $candidate
                    if ($candidateTitle -ne $WindowTitle) {
                        return $true
                    }
                }

                $script:foundWindow = $candidate
                return $false
            }

            return $true
        }

        [void][NativePidWindowCapture]::EnumWindows($callback, [IntPtr]::Zero)

        if ($script:foundWindow -ne [IntPtr]::Zero) {
            return $script:foundWindow
        }

        [void]$Process.WaitForInputIdle(100)
        $Process.Refresh()
    }

    return [IntPtr]::Zero
}

function Get-NativeWindowTitle {
    param(
        [Parameter(Mandatory = $true)]
        [IntPtr]$WindowHandle
    )

    $length = [NativePidWindowTitleFilter]::GetWindowTextLength($WindowHandle)
    if ($length -le 0) {
        return ''
    }

    $builder = [System.Text.StringBuilder]::new($length + 1)
    [void][NativePidWindowTitleFilter]::GetWindowText($WindowHandle, $builder, $builder.Capacity)
    $builder.ToString()
}

function Get-WindowBounds {
    param(
        [Parameter(Mandatory = $true)]
        [IntPtr]$WindowHandle
    )

    $DWMWA_EXTENDED_FRAME_BOUNDS = 9
    $rect = New-Object NativePidWindowCapture+RECT
    $hr = [NativePidWindowCapture]::DwmGetWindowAttribute($WindowHandle, $DWMWA_EXTENDED_FRAME_BOUNDS, [ref]$rect, 16)

    if ($hr -ne 0) {
        if (-not [NativePidWindowCapture]::GetWindowRect($WindowHandle, [ref]$rect)) {
            throw "DwmGetWindowAttribute failed with HRESULT=$hr, and GetWindowRect also failed."
        }
    }

    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top

    if ($width -le 0 -or $height -le 0) {
        throw "Invalid window bounds: ${width}x${height}."
    }

    [pscustomobject]@{
        Left = $rect.Left
        Top = $rect.Top
        Width = $width
        Height = $height
    }
}

function Save-WindowImage {
    param(
        [Parameter(Mandatory = $true)]
        [IntPtr]$WindowHandle,

        [Parameter(Mandatory = $true)]
        [object]$Bounds,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [int]$JpegQuality
    )

    $bitmap = [System.Drawing.Bitmap]::new($Bounds.Width, $Bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $hdc = $graphics.GetHdc()
        try {
            $PW_RENDERFULLCONTENT = 2
            if (-not [NativePidWindowCapture]::PrintWindow($WindowHandle, $hdc, $PW_RENDERFULLCONTENT)) {
                throw 'PrintWindow failed.'
            }
        }
        finally {
            $graphics.ReleaseHdc($hdc)
        }

        $extension = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()

        if ($extension -eq '.jpg' -or $extension -eq '.jpeg') {
            $encoder = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() |
                Where-Object { $_.MimeType -eq 'image/jpeg' }

            $encoderParameters = [System.Drawing.Imaging.EncoderParameters]::new(1)
            $encoderParameters.Param[0] = [System.Drawing.Imaging.EncoderParameter]::new(
                [System.Drawing.Imaging.Encoder]::Quality,
                [int64]$JpegQuality)

            $bitmap.Save($Path, $encoder, $encoderParameters)
        }
        else {
            $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
        }
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Save-ScreenImage {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Bounds,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [int]$JpegQuality
    )

    $bitmap = [System.Drawing.Bitmap]::new($Bounds.Width, $Bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.CopyFromScreen($Bounds.Left, $Bounds.Top, 0, 0, [System.Drawing.Size]::new($Bounds.Width, $Bounds.Height))

        $extension = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()

        if ($extension -eq '.jpg' -or $extension -eq '.jpeg') {
            $encoder = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() |
                Where-Object { $_.MimeType -eq 'image/jpeg' }

            $encoderParameters = [System.Drawing.Imaging.EncoderParameters]::new(1)
            $encoderParameters.Param[0] = [System.Drawing.Imaging.EncoderParameter]::new(
                [System.Drawing.Imaging.Encoder]::Quality,
                [int64]$JpegQuality)

            $bitmap.Save($Path, $encoder, $encoderParameters)
        }
        else {
            $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
        }
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$launchedProcess = $null

if ($PSCmdlet.ParameterSetName -eq 'Launch') {
    $processStartInfo = @{
        FilePath = $ExePath
        PassThru = $true
    }

    if ($ArgumentList.Count -gt 0) {
        $processStartInfo.ArgumentList = $ArgumentList
    }

    $process = Start-Process @processStartInfo
    $launchedProcess = $process
}
else {
    $process = Get-Process -Id $ProcessId -ErrorAction Stop
}

try {
    if ($launchedProcess) {
        [void]$process.WaitForInputIdle([Math]::Min($TimeoutSeconds * 1000, 5000))
    }

    $process.Refresh()

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $windowHandle = Find-VisibleTopLevelWindowByProcessId -ProcessId $process.Id -Process $process -Deadline $deadline

    if ($windowHandle -eq [IntPtr]::Zero) {
        if ([string]::IsNullOrWhiteSpace($WindowTitle)) {
            throw "No visible top-level window was found for process ID $($process.Id)."
        }

        throw "No visible top-level window titled '$WindowTitle' was found for process ID $($process.Id)."
    }

    $capturedTitle = Get-NativeWindowTitle -WindowHandle $windowHandle
    $bounds = Get-WindowBounds -WindowHandle $windowHandle
    [void][NativePidWindowActivation]::SetForegroundWindow($windowHandle)
    Start-Sleep -Milliseconds 250
    if ($CaptureMethod -eq 'Screen') {
        Save-ScreenImage -Bounds $bounds -Path $OutputPath -JpegQuality $Quality
    }
    else {
        Save-WindowImage -WindowHandle $windowHandle -Bounds $bounds -Path $OutputPath -JpegQuality $Quality
    }

    $file = Get-Item -LiteralPath $OutputPath
    [pscustomobject]@{
        Path = $file.FullName
        Bytes = $file.Length
        ProcessId = $process.Id
        Hwnd = ('0x{0:X}' -f $windowHandle.ToInt64())
        Title = $capturedTitle
        Window = "$($bounds.Width)x$($bounds.Height)"
        CaptureMethod = $CaptureMethod
    }
}
finally {
    if ($launchedProcess -and -not $process.HasExited) {
        $null = $process.CloseMainWindow()
        if (-not $process.WaitForExit(2000)) {
            $process.Kill()
        }
    }
}