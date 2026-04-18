[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Plan,

    [string]$Model = 'gpt-5.4',

    [string]$Root = $PSScriptRoot,

    [int]$MaxParallel,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$executorPath = Join-Path $PSScriptRoot 'scripts\ai\execute-plan.ps1'
if (-not (Test-Path -LiteralPath $executorPath)) {
    throw "Executor script not found: $executorPath"
}

$params = @{
    Plan = $Plan
    Provider = 'codex'
    Model = $Model
    Root = $Root
}

if ($PSBoundParameters.ContainsKey('MaxParallel')) {
    $params.MaxParallel = $MaxParallel
}

if ($DryRun) {
    $params.DryRun = $true
}

& $executorPath @params
