[CmdletBinding()]
param(
    [int]$ProcessId,

    [string]$ExePath,

    [string]$ActionsPath,

    [string]$ActionsText,

    [int]$TimeoutSeconds = 10,

    [string[]]$ArgumentList = @(),

    [string]$ScreenshotDirectory = '.\artifacts\screenshots',

    [string]$ScreenshotPrefix = 'uia',

    [int]$ScreenshotQuality = 80,

    [Parameter(ValueFromPipeline = $true)]
    [AllowEmptyString()]
    [string[]]$InputObject,

    [switch]$KeepOpen
)

$ErrorActionPreference = 'Stop'

if (-not [string]::IsNullOrWhiteSpace($ExePath) -and -not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
    throw "Executable not found: $ExePath"
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function ConvertTo-ControlType {
    param([string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name)) {
        return $null
    }

    switch ($Name.ToLowerInvariant()) {
        'button' { return [System.Windows.Automation.ControlType]::Button }
        'checkbox' { return [System.Windows.Automation.ControlType]::CheckBox }
        'combobox' { return [System.Windows.Automation.ControlType]::ComboBox }
        'document' { return [System.Windows.Automation.ControlType]::Document }
        'edit' { return [System.Windows.Automation.ControlType]::Edit }
        'group' { return [System.Windows.Automation.ControlType]::Group }
        'list' { return [System.Windows.Automation.ControlType]::List }
        'listitem' { return [System.Windows.Automation.ControlType]::ListItem }
        'menu' { return [System.Windows.Automation.ControlType]::Menu }
        'menuitem' { return [System.Windows.Automation.ControlType]::MenuItem }
        'pane' { return [System.Windows.Automation.ControlType]::Pane }
        'radiobutton' { return [System.Windows.Automation.ControlType]::RadioButton }
        'text' { return [System.Windows.Automation.ControlType]::Text }
        'window' { return [System.Windows.Automation.ControlType]::Window }
        default { throw "Unsupported controlType '$Name'. Add it to ConvertTo-ControlType if this app needs it." }
    }
}

function New-PropertyConditionIfValue {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationProperty]$Property,

        [object]$Value
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }

    return [System.Windows.Automation.PropertyCondition]::new($Property, $Value)
}

function Find-WindowByProcessId {
    param(
        [Parameter(Mandatory = $true)]
        [int]$TargetProcessId,

        [Parameter(Mandatory = $true)]
        [DateTime]$Deadline,

        [string]$WindowTitle
    )

    $processCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $TargetProcessId)
    $windowCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Window)
    $condition = [System.Windows.Automation.AndCondition]::new($processCondition, $windowCondition)

    while ([DateTime]::UtcNow -lt $Deadline) {
        $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            $condition)

        foreach ($window in $windows) {
            if ([string]::IsNullOrWhiteSpace($WindowTitle) -or $window.Current.Name -eq $WindowTitle) {
                return $window
            }
        }

        Start-Sleep -Milliseconds 100
    }

    return $null
}

function Find-ElementForAction {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Root,

        [Parameter(Mandatory = $true)]
        [object]$Action
    )

    $conditions = [System.Collections.Generic.List[System.Windows.Automation.Condition]]::new()

    $controlType = ConvertTo-ControlType -Name $Action.controlType
    if ($null -ne $controlType) {
        $conditions.Add([System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            $controlType))
    }

    $nameCondition = New-PropertyConditionIfValue -Property ([System.Windows.Automation.AutomationElement]::NameProperty) -Value $Action.name
    if ($null -ne $nameCondition) {
        $conditions.Add($nameCondition)
    }

    $automationIdCondition = New-PropertyConditionIfValue -Property ([System.Windows.Automation.AutomationElement]::AutomationIdProperty) -Value $Action.automationId
    if ($null -ne $automationIdCondition) {
        $conditions.Add($automationIdCondition)
    }

    $classNameCondition = New-PropertyConditionIfValue -Property ([System.Windows.Automation.AutomationElement]::ClassNameProperty) -Value $Action.className
    if ($null -ne $classNameCondition) {
        $conditions.Add($classNameCondition)
    }

    $condition = if ($conditions.Count -eq 0) {
        [System.Windows.Automation.Condition]::TrueCondition
    }
    elseif ($conditions.Count -eq 1) {
        $conditions[0]
    }
    else {
        [System.Windows.Automation.AndCondition]::new($conditions.ToArray())
    }

    $scope = [System.Windows.Automation.TreeScope]::Descendants
    $candidateElements = $Root.FindAll($scope, $condition)
    $index = if ($null -eq $Action.index) { 0 } else { [int]$Action.index }

    if ($candidateElements.Count -le $index) {
        $selector = ($Action | ConvertTo-Json -Compress)
        throw "No UI Automation element matched action selector at index $index. Selector: $selector"
    }

    return $candidateElements.Item($index)
}

function Invoke-ElementClick {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Element
    )

    $invokePattern = $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $invokePattern.Invoke()
    return 'InvokePattern'
}

function Set-ElementValue {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Element,

        [AllowEmptyString()]
        [string]$Value
    )

    $valuePattern = $Element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $valuePattern.SetValue($Value)
    return 'ValuePattern'
}

function Convert-Rect {
    param([System.Windows.Rect]$Rect)

    if ($Rect.IsEmpty) {
        return [ordered]@{ left = 0; top = 0; width = 0; height = 0; empty = $true }
    }

    [ordered]@{
        left = [int][Math]::Round($Rect.Left)
        top = [int][Math]::Round($Rect.Top)
        width = [int][Math]::Round($Rect.Width)
        height = [int][Math]::Round($Rect.Height)
        empty = $false
    }
}

function Export-UiaElement {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Element,

        [Parameter(Mandatory = $true)]
        [int]$Depth,

        [Parameter(Mandatory = $true)]
        [int]$Index,

        [Parameter(Mandatory = $true)]
        [int]$MaxDepth
    )

    $current = $Element.Current
    $node = [ordered]@{
        depth = $Depth
        index = $Index
        name = $current.Name
        automationId = $current.AutomationId
        className = $current.ClassName
        controlType = $current.ControlType.ProgrammaticName
        processId = $current.ProcessId
        boundingRectangle = Convert-Rect $current.BoundingRectangle
        children = @()
    }

    if ($Depth -lt $MaxDepth) {
        $children = $Element.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        $childNodes = @()
        for ($i = 0; $i -lt $children.Count; $i++) {
            $childNodes += Export-UiaElement -Element $children[$i] -Depth ($Depth + 1) -Index $i -MaxDepth $MaxDepth
        }

        $node.children = $childNodes
    }

    return $node
}

function Export-UiaTree {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$TargetProcess,

        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Root,

        [Parameter(Mandatory = $true)]
        [int]$MaxDepth
    )

    [ordered]@{
        processId = $TargetProcess.Id
        capturedAt = [DateTimeOffset]::Now.ToString('O')
        root = Export-UiaElement -Element $Root -Depth 0 -Index 0 -MaxDepth $MaxDepth
    }
}

function Read-ActionsText {
    param(
        [string]$Path,
        [string]$Text,
        [string]$PipelineText
    )

    if (-not [string]::IsNullOrWhiteSpace($Text)) {
        return $Text
    }

    if (-not [string]::IsNullOrWhiteSpace($Path)) {
        if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
            throw "Actions file not found: $Path"
        }

        return Get-Content -LiteralPath $Path -Raw
    }

    if (-not [string]::IsNullOrWhiteSpace($PipelineText)) {
        return $PipelineText
    }

    if ([Console]::IsInputRedirected) {
        $redirectedText = [Console]::In.ReadToEnd()
        if (-not [string]::IsNullOrWhiteSpace($redirectedText)) {
            return $redirectedText
        }
    }

    throw 'No UI Automation DSL input was provided. Pipe text to this script, use -ActionsText, or use -ActionsPath with a DSL text file.'
}

function ConvertFrom-DslLiteral {
    param([string]$Value)

    if ($Value -match '^-?\d+$') {
        return [int]$Value
    }

    return $Value
}

function Read-DslQuotedString {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text,

        [Parameter(Mandatory = $true)]
        [ref]$Position,

        [Parameter(Mandatory = $true)]
        [int]$LineNumber
    )

    $builder = [System.Text.StringBuilder]::new()
    $Position.Value++

    while ($Position.Value -lt $Text.Length) {
        $char = $Text[$Position.Value]
        if ($char -eq '"') {
            $Position.Value++
            return $builder.ToString()
        }

        if ($char -eq '\') {
            $Position.Value++
            if ($Position.Value -ge $Text.Length) {
                throw "Line ${LineNumber}: quoted string ends with an incomplete escape sequence."
            }

            $escaped = $Text[$Position.Value]
            switch ($escaped) {
                '"' { [void]$builder.Append('"') }
                '\' { [void]$builder.Append('\') }
                default {
                    [void]$builder.Append('\')
                    [void]$builder.Append($escaped)
                }
            }

            $Position.Value++
            continue
        }

        [void]$builder.Append($char)
        $Position.Value++
    }

    throw "Line ${LineNumber}: missing closing quote."
}

function ConvertFrom-DslText {
    param([string]$Text)

    $lines = $Text -split "`r?`n"
    $actions = [System.Collections.Generic.List[object]]::new()

    for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
        $rawLine = $lines[$lineIndex]
        $line = $rawLine.Trim()
        $lineNumber = $lineIndex + 1

        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith('#')) {
            continue
        }

        $statementMatch = [regex]::Match($line, '^(?<command>[A-Za-z][A-Za-z0-9_-]*)(?<rest>\s+.*)?$')
        if (-not $statementMatch.Success) {
            throw "Line ${lineNumber}: expected a command followed by key=value arguments."
        }

        $command = $statementMatch.Groups['command'].Value
        $rest = if (-not $statementMatch.Groups['rest'].Success) { '' } else { $statementMatch.Groups['rest'].Value.Trim() }
        $action = [ordered]@{ action = $command }
        $position = 0

        while ($position -lt $rest.Length) {
            while ($position -lt $rest.Length -and [char]::IsWhiteSpace($rest[$position])) {
                $position++
            }

            if ($position -ge $rest.Length) {
                break
            }

            $keyStart = $position
            while ($position -lt $rest.Length -and $rest[$position] -match '[A-Za-z0-9_-]') {
                $position++
            }

            if ($position -eq $keyStart) {
                throw "Line ${lineNumber}: expected an argument name."
            }

            $key = $rest.Substring($keyStart, $position - $keyStart)

            if ($position -ge $rest.Length -or $rest[$position] -ne '=') {
                throw "Line ${lineNumber}: expected '=' after argument '$key'."
            }

            $position++

            if ($position -ge $rest.Length) {
                throw "Line ${lineNumber}: expected a value for argument '$key'."
            }

            if ($rest.Substring($position).StartsWith('<<')) {
                $position += 2
                $tagStart = $position
                while ($position -lt $rest.Length -and -not [char]::IsWhiteSpace($rest[$position])) {
                    $position++
                }

                if ($position -eq $tagStart) {
                    throw "Line ${lineNumber}: heredoc for '$key' is missing a tag."
                }

                $tag = $rest.Substring($tagStart, $position - $tagStart)
                if (-not [string]::IsNullOrWhiteSpace($rest.Substring($position))) {
                    throw "Line ${lineNumber}: heredoc '$tag' must be the final token on the line."
                }

                $heredocLines = [System.Collections.Generic.List[string]]::new()
                $foundTerminator = $false
                while (++$lineIndex -lt $lines.Count) {
                    if ($lines[$lineIndex].Trim() -eq $tag) {
                        $foundTerminator = $true
                        break
                    }

                    $heredocLines.Add($lines[$lineIndex])
                }

                if (-not $foundTerminator) {
                    throw "Line ${lineNumber}: heredoc '$tag' was not terminated."
                }

                $action[$key] = ($heredocLines -join [Environment]::NewLine)
                break
            }

            if ($rest[$position] -eq '"') {
                $positionRef = [ref]$position
                $action[$key] = Read-DslQuotedString -Text $rest -Position $positionRef -LineNumber $lineNumber
                $position = $positionRef.Value
                continue
            }

            $valueStart = $position
            while ($position -lt $rest.Length -and -not [char]::IsWhiteSpace($rest[$position])) {
                $position++
            }

            $literal = $rest.Substring($valueStart, $position - $valueStart)
            $action[$key] = ConvertFrom-DslLiteral -Value $literal
        }

        $actions.Add([pscustomobject]$action)
    }

    return $actions
}

function Invoke-ScreenshotAction {
    param(
        [Parameter(Mandatory = $true)]
        [int]$TargetProcessId,

        [Parameter(Mandatory = $true)]
        [object]$Action,

        [Parameter(Mandatory = $true)]
        [int]$Step
    )

    $captureScript = Join-Path -Path $PSScriptRoot -ChildPath '..\capture-visible-process-window\Capture-VisibleProcessWindow.ps1'
    if (-not (Test-Path -LiteralPath $captureScript -PathType Leaf)) {
        throw "Capture script not found: $captureScript"
    }

    $outputPath = $Action.outputPath
    if ([string]::IsNullOrWhiteSpace($outputPath)) {
        $extension = if ([string]::IsNullOrWhiteSpace($Action.format)) { 'jpg' } else { [string]$Action.format }
        $extension = $extension.TrimStart('.')
        $outputPath = Join-Path -Path $ScreenshotDirectory -ChildPath ('{0}-step-{1:00}.{2}' -f $ScreenshotPrefix, $Step, $extension)
    }

    $quality = if ($null -eq $Action.quality) { $ScreenshotQuality } else { [int]$Action.quality }
    $timeout = if ($null -eq $Action.timeoutSeconds) { $TimeoutSeconds } else { [int]$Action.timeoutSeconds }

    $captureArguments = @{
        ProcessId = $TargetProcessId
        OutputPath = $outputPath
        CaptureMethod = 'PrintWindow'
        Quality = $quality
        TimeoutSeconds = $timeout
    }

    if (-not [string]::IsNullOrWhiteSpace($Action.windowName)) {
        $captureArguments.WindowTitle = [string]$Action.windowName
    }

    & $captureScript @captureArguments
}

function Invoke-CloseAction {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$TargetProcess,

        [Parameter(Mandatory = $true)]
        [object]$Action
    )

    $timeoutMilliseconds = if ($null -eq $Action.timeoutMilliseconds) { 2000 } else { [int]$Action.timeoutMilliseconds }
    if ($null -ne $Action.force) {
        throw "Command 'close' no longer supports force termination."
    }

    $TargetProcess.Refresh()
    if ($TargetProcess.HasExited) {
        return [pscustomobject]@{ Method = 'AlreadyExited'; CloseRequested = $false; Exited = $true; ExitCode = $TargetProcess.ExitCode; TimeoutMilliseconds = $timeoutMilliseconds }
    }

    $closeRequested = $TargetProcess.CloseMainWindow()
    if (-not $closeRequested) {
        throw "Command 'close' failed because CloseMainWindow returned false."
    }

    $exited = $TargetProcess.WaitForExit($timeoutMilliseconds)
    if (-not $exited) {
        throw "Command 'close' timed out after $timeoutMilliseconds ms."
    }

    $TargetProcess.Refresh()

    [pscustomobject]@{
        Method = 'CloseMainWindow'
        CloseRequested = $closeRequested
        Exited = $true
        ExitCode = $TargetProcess.ExitCode
        TimeoutMilliseconds = $timeoutMilliseconds
    }
}

function Start-TargetProcessFromAction {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Action
    )

    $path = if ([string]::IsNullOrWhiteSpace($Action.exePath)) { [string]$Action.path } else { [string]$Action.exePath }
    if ([string]::IsNullOrWhiteSpace($path)) {
        throw 'run requires exePath or path. Example: run exePath=".\path\to\app.exe"'
    }

    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Executable not found: $path"
    }

    $startInfo = @{
        FilePath = $path
        PassThru = $true
    }

    if (-not [string]::IsNullOrWhiteSpace($Action.arguments)) {
        $startInfo.ArgumentList = [string]$Action.arguments
    }

    Start-Process @startInfo
}

function Get-RequiredTargetProcess {
    param(
        [System.Diagnostics.Process]$TargetProcess,
        [string]$Command
    )

    if ($null -eq $TargetProcess) {
        throw "Command '$Command' requires a target process. Add a run statement first, or call the script with -ExePath or -ProcessId."
    }

    $TargetProcess.Refresh()
    if ($TargetProcess.HasExited) {
        throw "Command '$Command' cannot run because process ID $($TargetProcess.Id) has already exited."
    }

    $TargetProcess
}

function Resolve-TargetRoot {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$TargetProcess,

        [System.Windows.Automation.AutomationElement]$CurrentRoot,

        [int]$WaitSeconds,

        [string]$WindowTitle
    )

    if ($null -ne $CurrentRoot -and [string]::IsNullOrWhiteSpace($WindowTitle)) {
        return $CurrentRoot
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
    $resolvedRoot = Find-WindowByProcessId -TargetProcessId $TargetProcess.Id -Deadline $deadline -WindowTitle $WindowTitle
    if ($null -eq $resolvedRoot) {
        if ([string]::IsNullOrWhiteSpace($WindowTitle)) {
            throw "No UI Automation top-level window was found for process ID $($TargetProcess.Id)."
        }

        throw "No UI Automation top-level window titled '$WindowTitle' was found for process ID $($TargetProcess.Id)."
    }

    return $resolvedRoot
}

$pipelineText = ($InputObject -join [Environment]::NewLine)
$dslText = Read-ActionsText -Path $ActionsPath -Text $ActionsText -PipelineText $pipelineText
$actions = ConvertFrom-DslText -Text $dslText
if ($actions.Count -eq 0) {
    throw 'The UI Automation DSL input did not contain any actions.'
}

$launchedProcess = $null
$process = $null
$root = $null
$rootName = $null
$rootHwnd = $null
$closedByDsl = $false
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

if (-not [string]::IsNullOrWhiteSpace($ExePath)) {
    $startInfo = @{
        FilePath = $ExePath
        PassThru = $true
    }

    if ($ArgumentList.Count -gt 0) {
        $startInfo.ArgumentList = $ArgumentList
    }

    $process = Start-Process @startInfo
    $launchedProcess = $process
}
elseif ($ProcessId -gt 0) {
    $process = Get-Process -Id $ProcessId -ErrorAction Stop
}

try {
    if ($null -ne $launchedProcess) {
        [void]$launchedProcess.WaitForInputIdle([Math]::Min($TimeoutSeconds * 1000, 5000))
        $launchedProcess.Refresh()
    }

    if ($null -ne $process) {
        $root = Resolve-TargetRoot -TargetProcess $process -CurrentRoot $root -WaitSeconds $TimeoutSeconds
        $rootName = $root.Current.Name
        $rootHwnd = ('0x{0:X}' -f $root.Current.NativeWindowHandle)
    }

    $results = [System.Collections.Generic.List[object]]::new()
    $step = 0

    foreach ($action in $actions) {
        $step++
        $kind = [string]$action.action

        if ([string]::IsNullOrWhiteSpace($kind)) {
            throw "Action $step is missing an action value."
        }

        if ($kind -eq 'run') {
            if ($null -ne $process) {
                $process.Refresh()
                if (-not $process.HasExited) {
                    throw "Command 'run' cannot start a new target while process ID $($process.Id) is still running. Use close first."
                }
            }

            $process = Start-TargetProcessFromAction -Action $action
            $launchedProcess = $process
            $closedByDsl = $false
            [void]$process.WaitForInputIdle([Math]::Min($TimeoutSeconds * 1000, 5000))
            $process.Refresh()
            $root = Resolve-TargetRoot -TargetProcess $process -CurrentRoot $null -WaitSeconds $TimeoutSeconds
            $rootName = $root.Current.Name
            $rootHwnd = ('0x{0:X}' -f $root.Current.NativeWindowHandle)

            $results.Add([pscustomobject]@{
                Step = $step
                Action = $kind
                Method = 'StartProcess'
                ProcessId = $process.Id
                RootName = $rootName
                RootHwnd = $rootHwnd
            })
            continue
        }

        if ($kind -eq 'wait') {
            $milliseconds = if ($null -eq $action.milliseconds) { 250 } else { [int]$action.milliseconds }
            Start-Sleep -Milliseconds $milliseconds
            $results.Add([pscustomobject]@{ Step = $step; Action = $kind; Method = 'Sleep'; Milliseconds = $milliseconds })
            continue
        }

        if ($kind -eq 'screenshot') {
            $process = Get-RequiredTargetProcess -TargetProcess $process -Command $kind
            $screenshot = Invoke-ScreenshotAction -TargetProcessId $process.Id -Action $action -Step $step
            Write-Host "Screenshot: $($screenshot.Path)"
            $results.Add([pscustomobject]@{
                Step = $step
                Action = $kind
                Method = 'Screenshot'
                Path = $screenshot.Path
                Bytes = $screenshot.Bytes
                Window = $screenshot.Window
            })
            continue
        }

        if ($kind -eq 'export-uiatree' -or $kind -eq 'exportuiatree') {
            $process = Get-RequiredTargetProcess -TargetProcess $process -Command $kind
            $treeWindowName = if ($null -ne $action.windowName) { [string]$action.windowName } else { $null }
            $treeTimeoutSeconds = if ($null -eq $action.timeoutSeconds) { $TimeoutSeconds } else { [int]$action.timeoutSeconds }
            $treeRoot = Resolve-TargetRoot -TargetProcess $process -CurrentRoot $root -WaitSeconds $treeTimeoutSeconds -WindowTitle $treeWindowName
            $maxDepth = if ($null -eq $action.maxDepth) { 4 } else { [int]$action.maxDepth }
            $uiaTree = Export-UiaTree -TargetProcess $process -Root $treeRoot -MaxDepth $maxDepth
            Write-Host 'UiaTree:'
            Write-Host ($uiaTree | ConvertTo-Json -Depth 64)
            $results.Add([pscustomobject]@{
                Step = $step
                Action = $kind
                Method = 'ExportUiaTree'
                WindowName = $treeRoot.Current.Name
                MaxDepth = $maxDepth
            })
            continue
        }

        if ($kind -eq 'close') {
            $process = Get-RequiredTargetProcess -TargetProcess $process -Command $kind
            $closeResult = Invoke-CloseAction -TargetProcess $process -Action $action
            $closedByDsl = $closeResult.Exited
            if ($closedByDsl) {
                $root = $null
            }
            $results.Add([pscustomobject]@{
                Step = $step
                Action = $kind
                Method = $closeResult.Method
                CloseRequested = $closeResult.CloseRequested
                Exited = $closeResult.Exited
                ExitCode = $closeResult.ExitCode
                TimeoutMilliseconds = $closeResult.TimeoutMilliseconds
            })
            continue
        }

        $process = Get-RequiredTargetProcess -TargetProcess $process -Command $kind
        $actionWindowTitle = if ($null -eq $action.windowTitle) { $null } else { [string]$action.windowTitle }
        $actionRoot = Resolve-TargetRoot -TargetProcess $process -CurrentRoot $root -WaitSeconds $TimeoutSeconds -WindowTitle $actionWindowTitle
        if ([string]::IsNullOrWhiteSpace($rootName)) {
            $rootName = $root.Current.Name
            $rootHwnd = ('0x{0:X}' -f $root.Current.NativeWindowHandle)
        }

        $element = Find-ElementForAction -Root $actionRoot -Action $action
        $method = switch ($kind.ToLowerInvariant()) {
            'click' { Invoke-ElementClick -Element $element }
            'invoke' { Invoke-ElementClick -Element $element }
            'setvalue' { Set-ElementValue -Element $element -Value ([string]$action.value) }
            'settext' { Set-ElementValue -Element $element -Value ([string]$action.value) }
            'focus' { $element.SetFocus(); 'SetFocus' }
            default { throw "Unsupported action '$kind'. Supported actions: run, setValue, setText, click, invoke, focus, wait, screenshot, export-uiatree, close." }
        }

        $results.Add([pscustomobject]@{
            Step = $step
            Action = $kind
            Method = $method
            Name = $element.Current.Name
            ControlType = $element.Current.ControlType.ProgrammaticName
            ClassName = $element.Current.ClassName
            Hwnd = ('0x{0:X}' -f $element.Current.NativeWindowHandle)
            WindowTitle = $actionRoot.Current.Name
        })
    }

    $stopwatch.Stop()
    'Success: executed {0} actions in {1:N0} ms.' -f $results.Count, $stopwatch.Elapsed.TotalMilliseconds
}
finally {
    if ($launchedProcess -and -not $closedByDsl -and -not $KeepOpen -and -not $launchedProcess.HasExited) {
        $null = $launchedProcess.CloseMainWindow()
        $null = $launchedProcess.WaitForExit(2000)
    }
}
