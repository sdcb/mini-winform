# Automate a visible process window with a text DSL

This tutorial shows a repeatable way for an AI agent to launch or attach to a Windows GUI process, run batched UI Automation actions from plain text, and capture screenshots from inside the same action stream.

The reusable automation script is:

```text
docs/automate-visible-process-window/Invoke-VisibleProcessWindowAutomation.ps1
```

The script accepts the UI Automation DSL from the PowerShell pipeline, from `-ActionsText`, or from `-ActionsPath`. Prefer pipeline text, `-ActionsText`, or `-ActionsPath` for AI-driven work; it avoids JSON escaping and lets the AI write the action plan directly.

When piping text from a file, use `Get-Content -Raw` so the DSL arrives as one text block.

## Recommended AI Invocation

Start from the repository root so relative executable and screenshot paths resolve predictably:

```powershell
Set-Location C:\Projects\repos\win32-ctrl-demo
```

Build or publish the target app first. For the 07 sample app:

```powershell
dotnet publish .\07MiniWinFormDemo\07MiniWinFormDemo.csproj
```

For AI-controlled terminals, prefer sending the automation as one PowerShell command. Building the DSL as an array joined with `[Environment]::NewLine` avoids partial multi-line input and keeps the whole run, action sequence, screenshots, and close operation together:

```powershell
$dsl = @(
    'run exePath=".\07MiniWinFormDemo\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\07MiniWinFormDemo.exe"',
    'wait milliseconds=300',
    'screenshot outputPath=".\artifacts\screenshots\uia-initial.jpg" quality=85',
    'click name="Perform step"',
    'wait milliseconds=200',
    'screenshot outputPath=".\artifacts\screenshots\uia-after-click.jpg" quality=85',
    'close'
) -join [Environment]::NewLine

$summary = $dsl | .\docs\automate-visible-process-window\Invoke-VisibleProcessWindowAutomation.ps1
$summary
```

Replace the `click` selector and screenshot names for the workflow being tested. Keep `wait` statements after launch and after UI actions that repaint the window, then inspect the screenshot paths printed during the run before making visual claims.

Ask the AI agent to inspect any screenshot path printed by `screenshot` actions with `view_image`.

The final `close` statement closes the demo window. If you leave `close` out because you want follow-up actions, launch or track the process separately, run the script with `-ProcessId` and `-KeepOpen`, and close the demo process manually later:

```powershell
$process = Start-Process .\path\to\app.exe -PassThru
$summary = $dsl | .\docs\automate-visible-process-window\Invoke-VisibleProcessWindowAutomation.ps1 -ProcessId $process.Id -KeepOpen
Get-Process -Id $process.Id -ErrorAction SilentlyContinue | Stop-Process
```

Human-authored examples can still use a PowerShell here-string. For automated AI terminal execution, the single-command array form above is the most repeatable.

## Optional DSL File

For repeatable samples, the same text can live in a `.uia.txt` file:

```powershell
$result = .\docs\automate-visible-process-window\Invoke-VisibleProcessWindowAutomation.ps1 `
    -ActionsPath .\docs\automate-visible-process-window\07-miniwinform-demo-actions.uia.txt
```

This is still plain text DSL, not JSON. The pipeline form is usually better for ad hoc AI actions; the file form is useful for checked-in examples.

You can also pipe the file as raw text:

```powershell
$result = Get-Content .\docs\automate-visible-process-window\07-miniwinform-demo-actions.uia.txt -Raw |
    .\docs\automate-visible-process-window\Invoke-VisibleProcessWindowAutomation.ps1
```

## DSL Format

The DSL is line oriented: one action per statement, with `key=value` arguments. Empty lines and lines starting with `#` are ignored.

```text
statement  := command arguments?
command    := run | setValue | click | wait | screenshot | export-uiatree | close
argument   := key "=" value
value      := number | identifier | quotedString | heredoc
quotedString := "..."
heredoc      := <<TAG
                multiple lines
                TAG
```

Examples:

```text
run exePath=".\path\to\app.exe"
click name="Dark theme"
wait milliseconds=300
screenshot
close
```

Use quoted strings for normal text:

```text
setValue className=Edit index=0 value="AI automation"
```

Quoted strings only treat `\"` and `\\` as escapes. Other backslashes are kept as written, so Windows paths like `".\bin\Release\app.exe"` work without double escaping.

Use heredoc for multiline text:

```text
setValue className=Edit index=1 value=<<TEXT
line 1
line 2
TEXT
```

The heredoc terminator must appear alone on its own line. The parser keeps the inner lines exactly, joined with the platform newline.

## Commands

Supported commands:

- `run`: start an executable and make its visible top-level window the current target.
- `setValue` or `setText`: write text into an editable element.
- `click` or `invoke`: invoke a button-like element with UIA `InvokePattern` first.
- `focus`: move focus to an element.
- `wait`: pause between UI transitions.
- `screenshot`: capture the current process window and print the image path.
- `export-uiatree`: print the current process window's UI Automation tree as JSON.
- `close`: close the target process window.

Supported selector fields for UI actions:

- `name`: UI Automation Name, usually visible text for Win32 controls.
- `automationId`: preferred when an app exposes stable automation IDs.
- `className`: Win32 class name such as `Edit` or `Button`.
- `controlType`: UIA control type such as `Edit`, `Button`, `CheckBox`, or `RadioButton`.
- `index`: zero-based index when multiple elements match the same selector.
- `windowTitle`: optional top-level window title within the current process. Use this for owned child windows or modal dialogs.

For future WPF or WinUI apps, prefer `automationId` over visible text. Visible text is convenient for demos, but automation IDs survive localization and copy changes.

## Run Command

Use `run` to start the app from inside the DSL:

```text
run exePath=".\07MiniWinFormDemo\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\07MiniWinFormDemo.exe"
```

After `run`, the script tracks the current `ProcessId`, UI Automation root, and root window handle. Later commands operate on that current target, so the caller does not need to pass `-ProcessId` or `-ExePath` externally.

Run arguments:

- `exePath` or `path`: executable path.
- `arguments`: optional command-line argument string.

The script still supports the older outer launch/attach style with `-ExePath` or `-ProcessId`. Use that when you want the shell command to control the target process instead of the DSL.

## Intermediate Output

The only DSL actions that produce intermediate console output are `screenshot` and `export-uiatree`. Every successful run still finishes with a single summary line.

## Screenshot Command

`screenshot` can be used with no arguments:

```text
screenshot
```

By default, screenshots go to `artifacts/screenshots` with generated names like `uia-step-07.jpg`, using `PrintWindow` capture and JPEG quality `80`.

Every `screenshot` statement prints its image path to the console immediately.

You can override the output per screenshot:

```text
screenshot outputPath=".\artifacts\screenshots\before-click.jpg"
click name="Show TaskDialog"
screenshot outputPath=".\artifacts\screenshots\after-click.jpg" quality=85
```

To capture a child window or dialog instead of the process main window, pass `windowName`. The search is limited to visible top-level windows that belong to the current target process:

```text
click name="Show dialog window"
screenshot windowName="Modal child dialog" outputPath=".\artifacts\screenshots\modal-dialog.png"
click windowTitle="Modal child dialog" name="OK"
```

Screenshot arguments:

- `outputPath`: explicit `.jpg`, `.jpeg`, or `.png` path.
- `quality`: JPEG quality from `1` to `100`.
- `format`: extension used for generated paths when `outputPath` is omitted.
- `timeoutSeconds`: wait time for finding the visible top-level window.
- `windowName`: optional visible top-level window name within the current process.

Screenshots always use `PrintWindow` because it asks the target window to render itself and does not depend on the desktop being unlocked or unobscured. Popup menus, dropdowns, tooltips, and other separate transient windows may not be included; validate those through UI Automation state or app-visible results instead.

## Export UIA Tree Command

`export-uiatree` prints a JSON UI Automation tree for the current target window:

```text
export-uiatree
```

The output starts with `UiaTree:` followed by the JSON tree. The tree includes each node's name, automation ID, class name, control type, process ID, bounding rectangle, and children.

To capture a child window or dialog instead of the process main window, pass `windowName`:

```text
export-uiatree windowName="Modal child dialog" maxDepth=6
```

Export UIA tree arguments:

- `maxDepth`: maximum child depth to include. Defaults to `4`.
- `timeoutSeconds`: wait time for finding the visible top-level window.
- `windowName`: optional visible top-level window name within the current process.

## Close Command

Use `close` to end the target app from the DSL:

```text
close
```

By default, `close` calls `CloseMainWindow()` and waits up to `2000` ms. If the window cannot be closed gracefully in that time, the action fails.

```text
close timeoutMilliseconds=5000
```

Close arguments:

- `timeoutMilliseconds`: how long to wait after requesting a graceful close.

## How the Script Works

The script uses built-in .NET UI Automation assemblies (`UIAutomationClient` and `UIAutomationTypes`) and keeps the action language deliberately small.

The flow is:

1. Read DSL text from stdin, `-ActionsText`, or `-ActionsPath`.
2. Parse one statement per line into action objects.
3. Use `run`, `-ExePath`, or `-ProcessId` to establish the current target process.
4. Find and cache the top-level UI Automation window for that process.
5. Execute actions in order.
6. Use `ValuePattern.SetValue` for editable controls.
7. Use `InvokePattern.Invoke` for clicks.
8. Fail the action if the requested UI Automation pattern is unavailable.
9. Run `screenshot` actions by calling the existing visible-process capture script by process ID.
10. Run `export-uiatree` actions directly with .NET UI Automation APIs.
11. Run `close` actions by requesting a graceful main-window close.
12. Return a single success summary line with the action count and elapsed time.

The DSL intentionally avoids coordinate-style automation and alternate execution paths. If an element cannot satisfy the requested semantic UI Automation operation, the action fails.

## Why This Starts With Built-In UIA Instead Of FlaUI

FlaUI.UIA3 is a good choice for a larger C# test harness, especially when you want richer APIs, test framework integration, or long-lived automation code. For AI-driven repository tasks, the PowerShell UIA script is simpler:

- no NuGet package restore for the automation layer;
- easy line-oriented text that an AI can generate or edit;
- `run`, attach, operation, screenshot, and close all fit in one action stream;
- screenshots can be placed anywhere in the action stream;
- structured output that can be piped into later steps;
- works with Win32, and should also work with WPF or other UIA-friendly desktop apps.

A good upgrade path is to keep the same DSL shape and replace only the executor with FlaUI if the app needs more advanced patterns, tree diagnostics, or cross-process test orchestration.

## Notes From Validation

This workflow was validated against `07MiniWinFormDemo`.

Observed results:

- A target app can be launched, operated, captured, and closed from one DSL stream.
- `run` can start the app and automatically track the process ID and root window handle for later actions.
- `click` uses UIA `InvokePattern`. That is the recommended path for buttons and button-like controls.
- `screenshot` can appear multiple times in one DSL stream, so an AI can inspect intermediate states without relaunching the app.
- `close` can finish the app from the same DSL stream after the final screenshot.
- `PrintWindow` is the only supported screenshot capture path. It is intended for the target process main window, not transient popup menus or dropdowns.
- Treat the success summary and screenshots as complementary evidence. The summary proves the DSL finished; the screenshot proves the user-visible state changed.

Older validation notes:

- A previous single-line Win32 edit accepted text through `ValuePattern`.
- A previous multiline Win32 edit did not expose the same value path consistently. That is treated as an automation failure instead of switching to another input path.

Known MiniWinForm limitation found during the experiment:

- The current owner-drawn `CheckBox` and `RadioButton` controls can expose invokable UIA elements, but the demo control state can fail to move from unchecked to checked. Treat this as a MiniWinForm control bug, not as proof that UI Automation failed. For automation validation, inspect both the script result and the screenshot, and avoid using that checked state as the only pass/fail signal until the control is fixed.

## Practical AI Workflow

For follow-up AI agents, use this loop:

1. Build or publish the target app.
2. Set the working directory to the repository root before invoking the automation script.
3. Write a small DSL script using pipeline text, `-ActionsText`, `-ActionsPath`, or the single-command array pattern shown above.
4. Start the app with `run exePath="..."` inside the DSL.
5. Add `wait` after launch and after UI actions that need repaint time.
6. Include `screenshot` statements where visual inspection is useful.
7. Inspect printed screenshot paths with `view_image`.
8. Include `close` at the end when no more follow-up actions are needed.
9. If needed, omit `close`, run the first script with `-KeepOpen`, save the `ProcessId`, and run another DSL script with `-ProcessId ...` against the still-open app.

This keeps UI automation fast and legible: AI writes intent as text, the script performs semantic UI operations, and screenshots provide visual feedback at chosen checkpoints.
