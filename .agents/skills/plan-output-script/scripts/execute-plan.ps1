[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Plan,

    [ValidateSet('codex', 'claude-code')]
    [string]$Provider,

    [string]$Model,

    [string]$Root = (Get-Location).Path,

    [int]$MaxParallel,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-AbsolutePath {
    param([Parameter(Mandatory = $true)][string]$PathValue)

    $resolved = Resolve-Path -LiteralPath $PathValue -ErrorAction Stop
    return $resolved.Path
}

function Resolve-PlanFile {
    param(
        [Parameter(Mandatory = $true)][string]$PlanValue,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    if (Test-Path -LiteralPath $PlanValue) {
        return (Resolve-AbsolutePath -PathValue $PlanValue)
    }

    $plansRoot = Join-Path $RepoRoot 'plans'
    if (-not (Test-Path -LiteralPath $plansRoot)) {
        throw "Plans directory not found: $plansRoot"
    }

    $searchNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    [void]$searchNames.Add($PlanValue)

    if (-not [System.IO.Path]::GetExtension($PlanValue)) {
        [void]$searchNames.Add("$PlanValue.md")
    }

    $matches = Get-ChildItem -LiteralPath $plansRoot -File -Recurse |
        Where-Object {
            $searchNames.Contains($_.Name) -or $searchNames.Contains($_.BaseName)
        }

    if ($matches.Count -ne 1) {
        $found = if ($matches) { ($matches | ForEach-Object FullName) -join '; ' } else { 'none' }
        throw "Expected exactly one plan file for '$PlanValue'. Found $($matches.Count): $found"
    }

    return $matches[0].FullName
}

function Get-PlanData {
    param([Parameter(Mandatory = $true)][string]$PlanPath)

    $content = Get-Content -Raw -LiteralPath $PlanPath
    $titleMatch = [regex]::Match($content, '(?m)^# Plan:\s*(.+)$')
    if (-not $titleMatch.Success) {
        throw "Missing '# Plan:' heading in $PlanPath"
    }

    $summaryMatch = [regex]::Match($content, '(?m)^>\s*(.+)$')
    $metaMatch = [regex]::Match($content, '(?ms)```plan-meta\s*(\{.*?\})\s*```')
    if (-not $metaMatch.Success) {
        throw "Missing ```plan-meta``` block in $PlanPath"
    }

    $taskMatches = [regex]::Matches($content, '(?ms)^## Task\s+([^\r\n]+?)\r?\n\s*```task\s*(\{.*?\})\s*```')
    if ($taskMatches.Count -eq 0) {
        throw "No ```task``` blocks found in $PlanPath"
    }

    $meta = $metaMatch.Groups[1].Value | ConvertFrom-Json -AsHashtable
    $tasks = @()

    foreach ($match in $taskMatches) {
        $task = $match.Groups[2].Value | ConvertFrom-Json -AsHashtable
        $tasks += ,$task
    }

    return @{
        Title = $titleMatch.Groups[1].Value.Trim()
        Summary = if ($summaryMatch.Success) { $summaryMatch.Groups[1].Value.Trim() } else { '' }
        Meta = $meta
        Tasks = $tasks
    }
}

function Assert-TaskField {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Task,
        [Parameter(Mandatory = $true)][string]$FieldName
    )

    if (-not $Task.ContainsKey($FieldName)) {
        throw "Task is missing required field '$FieldName'"
    }
}

function Validate-PlanData {
    param([Parameter(Mandatory = $true)][hashtable]$PlanData)

    $allowedStatuses = @('pending', 'in_progress', 'done', 'failed')
    $allowedProviders = @('codex', 'claude-code')
    $taskIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    foreach ($task in $PlanData.Tasks) {
        foreach ($field in @('id', 'title', 'status', 'agent', 'dependsOn', 'paths', 'goal', 'acceptance', 'steps')) {
            Assert-TaskField -Task $task -FieldName $field
        }

        if (-not $taskIds.Add([string]$task.id)) {
            throw "Duplicate task id: $($task.id)"
        }

        if ($task.status -notin $allowedStatuses) {
            throw "Invalid status '$($task.status)' for task $($task.id)"
        }

        if (-not ($task.dependsOn -is [System.Collections.IEnumerable])) {
            throw "dependsOn must be an array for task $($task.id)"
        }

        if (-not ($task.paths -is [System.Collections.IEnumerable])) {
            throw "paths must be an array for task $($task.id)"
        }

        if (-not ($task.acceptance -is [System.Collections.IEnumerable])) {
            throw "acceptance must be an array for task $($task.id)"
        }

        if (-not ($task.steps -is [System.Collections.IEnumerable])) {
            throw "steps must be an array for task $($task.id)"
        }

        $agentPath = Join-Path $script:RepoRoot ".agents\agents\$($task.agent).md"
        if (-not (Test-Path -LiteralPath $agentPath)) {
            throw "Agent file not found for task $($task.id): $agentPath"
        }
    }

    foreach ($task in $PlanData.Tasks) {
        foreach ($dependency in $task.dependsOn) {
            if (-not $taskIds.Contains([string]$dependency)) {
                throw "Task $($task.id) depends on missing task id '$dependency'"
            }
        }
    }

    if ($PlanData.Meta.ContainsKey('provider') -and $PlanData.Meta.provider -notin $allowedProviders) {
        throw "Unsupported provider in plan-meta: $($PlanData.Meta.provider)"
    }
}

function ConvertTo-PrettyJson {
    param([Parameter(Mandatory = $true)]$Value)
    return ($Value | ConvertTo-Json -Depth 20)
}

function Save-PlanData {
    param(
        [Parameter(Mandatory = $true)][hashtable]$PlanData,
        [Parameter(Mandatory = $true)][string]$PlanPath
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# Plan: $($PlanData.Title)")
    $lines.Add('')

    if ($PlanData.Summary) {
        $lines.Add("> $($PlanData.Summary)")
        $lines.Add('')
    }

    $lines.Add('```plan-meta')
    $lines.Add((ConvertTo-PrettyJson -Value $PlanData.Meta))
    $lines.Add('```')
    $lines.Add('')

    foreach ($task in $PlanData.Tasks) {
        $lines.Add("## Task $($task.id): $($task.title)")
        $lines.Add('')
        $lines.Add('```task')
        $lines.Add((ConvertTo-PrettyJson -Value $task))
        $lines.Add('```')
        $lines.Add('')
    }

    [System.IO.File]::WriteAllText($PlanPath, ($lines -join [Environment]::NewLine), [System.Text.Encoding]::UTF8)
}

function Get-ReadyTasks {
    param([Parameter(Mandatory = $true)][hashtable]$PlanData)

    $doneIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($task in $PlanData.Tasks | Where-Object { $_.status -eq 'done' }) {
        [void]$doneIds.Add([string]$task.id)
    }

    $ready = @()
    foreach ($task in $PlanData.Tasks | Where-Object { $_.status -eq 'pending' }) {
        $dependenciesMet = $true
        foreach ($dependency in $task.dependsOn) {
            if (-not $doneIds.Contains([string]$dependency)) {
                $dependenciesMet = $false
                break
            }
        }

        if ($dependenciesMet) {
            $ready += ,$task
        }
    }

    return $ready
}

function New-TaskPrompt {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Task,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    $agentPath = Join-Path $RepoRoot ".agents\agents\$($Task.agent).md"
    $agentContent = Get-Content -Raw -LiteralPath $agentPath
    $taskJson = ConvertTo-PrettyJson -Value $Task

    @"
Use the following agent definition as your role instructions:

$agentContent

Repository root: $RepoRoot

Plan task payload:
$taskJson

Execution rules:
- Execute only this task
- Read docs/PROJECT_DEFINITION.md before editing
- Work on the current shared git branch
- Do not commit
- Do not run broad validation unless the task explicitly asks for it
- Final response must be exactly SUCCESS or FAILURE: <one short sentence>
"@
}

function Start-ExecutionJob {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Task,
        [Parameter(Mandatory = $true)][string]$ExecutionProvider,
        [Parameter(Mandatory = $false)][string]$ExecutionModel,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("plan-output-script-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tempRoot | Out-Null

    $promptPath = Join-Path $tempRoot 'prompt.txt'
    $resultPath = Join-Path $tempRoot 'result.txt'
    $logPath = Join-Path $tempRoot 'log.txt'

    $prompt = New-TaskPrompt -Task $Task -RepoRoot $RepoRoot
    [System.IO.File]::WriteAllText($promptPath, $prompt, [System.Text.Encoding]::UTF8)

    $job = Start-Job -ArgumentList $Task.id, $ExecutionProvider, $ExecutionModel, $RepoRoot, $promptPath, $resultPath, $logPath -ScriptBlock {
        param($TaskId, $ProviderName, $ModelName, $WorkingRoot, $PromptFile, $OutputFile, $LogFile)

        $ErrorActionPreference = 'Stop'
        Set-StrictMode -Version Latest

        try {
            $promptText = Get-Content -Raw -LiteralPath $PromptFile

            switch ($ProviderName) {
                'codex' {
                    $args = @(
                        'exec',
                        '--cd', $WorkingRoot,
                        '--sandbox', 'danger-full-access',
                        '--skip-git-repo-check',
                        '--dangerously-bypass-approvals-and-sandbox',
                        '--output-last-message', $OutputFile,
                        '--color', 'never',
                        '-'
                    )

                    if ($ModelName) {
                        $args = @('exec', '--cd', $WorkingRoot, '--model', $ModelName, '--sandbox', 'danger-full-access', '--skip-git-repo-check', '--dangerously-bypass-approvals-and-sandbox', '--output-last-message', $OutputFile, '--color', 'never', '-')
                    }

                    $log = $promptText | & codex @args 2>&1 | Out-String
                    [System.IO.File]::WriteAllText($LogFile, $log, [System.Text.Encoding]::UTF8)

                    if (-not (Test-Path -LiteralPath $OutputFile)) {
                        throw "Codex did not produce an output file"
                    }
                }
                'claude-code' {
                    $args = @(
                        '-p',
                        '--output-format', 'text',
                        '--permission-mode', 'bypassPermissions',
                        '--add-dir', $WorkingRoot
                    )

                    if ($ModelName) {
                        $args += @('--model', $ModelName)
                    }

                    $result = $promptText | & claude @args 2>&1 | Out-String
                    [System.IO.File]::WriteAllText($OutputFile, $result, [System.Text.Encoding]::UTF8)
                    [System.IO.File]::WriteAllText($LogFile, $result, [System.Text.Encoding]::UTF8)
                }
                default {
                    throw "Unsupported provider: $ProviderName"
                }
            }

            [pscustomobject]@{
                TaskId = $TaskId
                OutputFile = $OutputFile
                LogFile = $LogFile
                Success = $true
                ErrorMessage = ''
            }
        }
        catch {
            [System.IO.File]::WriteAllText($LogFile, $_.Exception.ToString(), [System.Text.Encoding]::UTF8)
            [pscustomobject]@{
                TaskId = $TaskId
                OutputFile = $OutputFile
                LogFile = $LogFile
                Success = $false
                ErrorMessage = $_.Exception.Message
            }
        }
    }

    return [pscustomobject]@{
        Task = $Task
        Job = $job
        ResultPath = $resultPath
        LogPath = $logPath
        TempRoot = $tempRoot
    }
}

function Read-TaskResult {
    param([Parameter(Mandatory = $true)]$ExecutionHandle)

    $jobResult = Receive-Job -Job $ExecutionHandle.Job -Wait -AutoRemoveJob
    if (-not $jobResult.Success) {
        return @{
            Success = $false
            Message = $jobResult.ErrorMessage
            Output = ''
            LogPath = $ExecutionHandle.LogPath
        }
    }

    $output = Get-Content -Raw -LiteralPath $ExecutionHandle.ResultPath
    $trimmed = $output.Trim()

    if ($trimmed -eq 'SUCCESS') {
        return @{
            Success = $true
            Message = 'SUCCESS'
            Output = $trimmed
            LogPath = $ExecutionHandle.LogPath
        }
    }

    if ($trimmed -match '^FAILURE:\s+.+') {
        return @{
            Success = $false
            Message = $trimmed
            Output = $trimmed
            LogPath = $ExecutionHandle.LogPath
        }
    }

    return @{
        Success = $false
        Message = "Invalid agent response for task $($ExecutionHandle.Task.id): '$trimmed'"
        Output = $trimmed
        LogPath = $ExecutionHandle.LogPath
    }
}

function Invoke-Validation {
    param(
        [Parameter(Mandatory = $true)][hashtable]$PlanData,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    if (-not $PlanData.Meta.ContainsKey('validation')) {
        return
    }

    foreach ($command in $PlanData.Meta.validation) {
        Write-Host "Validation: $command"
        $null = & pwsh -NoProfile -Command $command
        if ($LASTEXITCODE -ne 0) {
            throw "Validation failed: $command"
        }
    }
}

$script:RepoRoot = Resolve-AbsolutePath -PathValue $Root
$resolvedPlan = Resolve-PlanFile -PlanValue $Plan -RepoRoot $script:RepoRoot
$planData = Get-PlanData -PlanPath $resolvedPlan
Validate-PlanData -PlanData $planData

$resolvedProvider = if ($Provider) { $Provider } elseif ($planData.Meta.provider) { [string]$planData.Meta.provider } else { 'codex' }
$resolvedModel = if ($PSBoundParameters.ContainsKey('Model')) { $Model } elseif ($planData.Meta.model) { [string]$planData.Meta.model } else { '' }
$parallelLimit = if ($PSBoundParameters.ContainsKey('MaxParallel')) { $MaxParallel } elseif ($planData.Meta.maxParallel) { [int]$planData.Meta.maxParallel } else { 1 }
if ($parallelLimit -lt 1) {
    $parallelLimit = 1
}

Write-Host "Plan file: $resolvedPlan"
Write-Host "Provider: $resolvedProvider"
Write-Host "Model: $(if ($resolvedModel) { $resolvedModel } else { '<default>' })"
Write-Host "MaxParallel: $parallelLimit"

if ($DryRun) {
    Write-Host "Dry run. No agents started."
    foreach ($task in $planData.Tasks) {
        Write-Host "Task $($task.id): status=$($task.status) agent=$($task.agent) dependsOn=$(([string[]]$task.dependsOn) -join ',')"
    }
    exit 0
}

while ($true) {
    $pendingTasks = @($planData.Tasks | Where-Object { $_.status -eq 'pending' })
    if ($pendingTasks.Count -eq 0) {
        break
    }

    $readyTasks = @(Get-ReadyTasks -PlanData $planData)
    if ($readyTasks.Count -eq 0) {
        throw "No ready tasks found. Dependency cycle or blocked plan."
    }

    $batch = @($readyTasks | Select-Object -First $parallelLimit)
    Write-Host "Starting batch: $((@($batch | ForEach-Object id)) -join ', ')"

    foreach ($task in $batch) {
        $task.status = 'in_progress'
    }
    Save-PlanData -PlanData $planData -PlanPath $resolvedPlan

    $handles = @()
    foreach ($task in $batch) {
        $handles += ,(Start-ExecutionJob -Task $task -ExecutionProvider $resolvedProvider -ExecutionModel $resolvedModel -RepoRoot $script:RepoRoot)
    }

    $batchFailed = $false
    foreach ($handle in $handles) {
        $result = Read-TaskResult -ExecutionHandle $handle
        if ($result.Success) {
            $handle.Task.status = 'done'
            Write-Host "Task $($handle.Task.id): SUCCESS"
        }
        else {
            $handle.Task.status = 'failed'
            $batchFailed = $true
            Write-Error "Task $($handle.Task.id): $($result.Message)"
            Write-Host "Log: $($result.LogPath)"
        }
    }

    Save-PlanData -PlanData $planData -PlanPath $resolvedPlan

    if ($batchFailed) {
        throw "Batch failed. See task logs above."
    }
}

Invoke-Validation -PlanData $planData -RepoRoot $script:RepoRoot
Write-Host 'Plan execution complete.'
