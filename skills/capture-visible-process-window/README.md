# Capture a visible process window for AI inspection

This tutorial shows a repeatable way to run a Windows GUI executable, find the visible top-level window created by that process, capture that window to a compact image, and then inspect the image with an AI `view_image` tool.

The reusable script is:

```text
docs/capture-visible-process-window/Capture-VisibleProcessWindow.ps1
```

## Basic usage

Publish or build the app first, then run the script with the executable path and an output image path:

```powershell
dotnet publish .\07MiniWinFormDemo\07MiniWinFormDemo.csproj

.\docs\capture-visible-process-window\Capture-VisibleProcessWindow.ps1 `
    -ExePath .\07MiniWinFormDemo\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\07MiniWinFormDemo.exe `
    -OutputPath .\artifacts\screenshots\07-miniwinform-demo.jpg `
    -CaptureMethod PrintWindow `
    -Quality 80
```

The script prints the captured image path, byte size, process ID, window handle, and measured window size. For AI review, ask the agent to call `view_image` on the printed `Path`.

## What the script does

The script follows this flow:

1. Run the executable with `Start-Process -PassThru`.
2. Keep the launched process ID.
3. Enumerate top-level windows with `EnumWindows`.
4. Use `GetWindowThreadProcessId` to find windows owned by that process.
5. Keep the first visible, unowned top-level window.
6. Measure its bounds with `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`.
7. Bring the target window to the foreground.
8. Capture the window with either `PrintWindow(PW_RENDERFULLCONTENT)` or screen pixels from the measured bounds.
9. Save JPEG for small AI-friendly screenshots, or PNG if the output path ends with `.png`.

The screenshot dimensions are measured at runtime. They are not hard-coded and do not depend on a window title.

## Useful options

```powershell
.\docs\capture-visible-process-window\Capture-VisibleProcessWindow.ps1 `
    -ExePath .\path\to\app.exe `
    -OutputPath .\artifacts\screenshots\app.jpg `
    -CaptureMethod PrintWindow `
    -Quality 75 `
    -TimeoutSeconds 15
```

- `-ExePath` is the GUI executable to launch.
- `-ProcessId` attaches to an already-running process instead of launching a new one.
- `-OutputPath` controls both location and image format. Use `.jpg` or `.jpeg` for compact screenshots; use `.png` when exact pixels matter.
- `-CaptureMethod` can be `PrintWindow` or `Screen`. `PrintWindow` asks the target window to render itself. `Screen` activates the target window and captures pixels from its measured screen bounds.
- `-Quality` applies to JPEG output and must be between `1` and `100`.
- `-TimeoutSeconds` controls how long to wait for a visible process window.
- `-ArgumentList` can pass command-line arguments to the launched executable.

## Why this method is more reliable

`PrintWindow` asks the target window to render itself into the bitmap, which avoids occlusion for many normal Win32 controls and does not depend on the desktop being unlocked. This is the better default for AI/unattended automation.

`Graphics.CopyFromScreen` depends on what is actually visible, so it can include the lock screen, wallpaper, VS Code, Explorer, or any other window that covers the target. Use `-CaptureMethod Screen` only when the desktop is unlocked and you intentionally want the same pixels a user currently sees. Some custom or owner-drawn windows can render stale or incomplete client content through `PrintWindow`; for those cases, rerun with `Screen` after making sure the target window is visible.

`Process.MainWindowHandle` is convenient, but it can be zero while the app is still starting, and it is less explicit than looking for the visible window belonging to the PID you just launched. PID-based `EnumWindows` also avoids relying on a fixed window title.

`GetWindowRect` can be affected by DPI virtualization. The script prefers DWM extended frame bounds and only falls back to `GetWindowRect` if DWM bounds are unavailable.

## Notes from the first validation

The workflow was first validated with `07MiniWinFormDemo`. JPEG quality `80` produced a readable screenshot of about `31 KB`, and `view_image` could inspect it directly.

The main pitfalls found during validation were:

1. Persistent PowerShell sessions cannot `Add-Type` the same C# type name twice, so the script checks whether the type already exists.
2. Relative paths depend on the current terminal directory, so examples should be run from the repository root or use explicit paths.
3. DWM bounds are better than `GetWindowRect` on scaled desktops.
4. Generated screenshots should stay under `artifacts/screenshots` instead of source directories.
5. If `PrintWindow` returns stale or incomplete client content, rerun with `-CaptureMethod Screen` only on an unlocked, visible desktop.