---
name: plan-output-script
description: Use when the user wants a machine-parsable implementation plan plus a script-executable workflow. Triggers on requests like "create script-friendly plan", "make plan for executor script", "prepare plan with agents/dependencies", or when the plan must include exact agent names, dependency edges, provider, and model defaults for later execution by a script.
---

# Plan Output Script

Produce the plan as a markdown file in `/plans/` using strict fenced JSON blocks.

Use this skill when the plan must be executed later by `scripts/execute-plan.ps1`.

## File Naming

`plans/{YYYY-MM-DD}_{short-description}.md`

- Date is today's date
- Short description is kebab-case, max 4-5 words

## Required Format

Write exactly this structure:

````markdown
# Plan: {Title}

> {One short summary sentence.}

```plan-meta
{
  "version": 1,
  "provider": "codex",
  "model": "gpt-5.4",
  "maxParallel": 2,
  "validation": [
    "dotnet build CSharpDllGraph.slnx"
  ]
}
```

## Task T1: {Task title}

```task
{
  "id": "T1",
  "title": "{Task title}",
  "status": "[ ]",
  "agent": "backend-csharp",
  "dependsOn": [],
  "paths": [
    "src/CSharpDllGraph.Engine/"
  ],
  "goal": "{One sentence}",
  "acceptance": [
    "{Observable outcome}"
  ],
  "steps": [
    "{Concrete step 1}",
    "{Concrete step 2}"
  ]
}
```

## Task T2: {Task title}

```task
{
  "id": "T2",
  "title": "{Task title}",
  "status": "[ ]",
  "agent": "docs-product",
  "dependsOn": [
    "T1"
  ],
  "paths": [
    "docs/PROJECT_DEFINITION.md"
  ],
  "goal": "{One sentence}",
  "acceptance": [
    "{Observable outcome}"
  ],
  "steps": [
    "{Concrete step 1}"
  ]
}
```
````

## Rules

- Every task must have one exact agent name
- Every task must have one stable `id`
- Use only `[ ]`, `[~]`, `[x]` for `status`
- `[ ]` = not done
- `[~]` = in progress
- `[x]` = done
- Status is owned by the executor script, not by AI agents
- Use exact dependency ids in `dependsOn`
- Keep `steps` concrete enough for one execution agent
- Keep `paths` short and real
- Prefer 1 task per independently executable unit
- Keep summary outside JSON short

## Allowed Agents

Use exact names from `.agents/agents/`:
- `backend-csharp`
- `backend-testing`
- `docs-product`
- `code-reviewer`

## Provider And Model Defaults

Set provider and model in `plan-meta`.

Current supported providers for the executor script:
- `codex`
- `claude-code`

Examples:
- `provider: "codex"` with `model: "gpt-5.4"`
- `provider: "claude-code"` with `model: "claude-sonnet-4-6"`

The script may override both at runtime, so keep plan defaults sensible.

## Workflow

1. Explore codebase only as needed
2. Break work into explicit task units
3. Assign exact agents
4. Add exact dependency edges
5. Set every new task status to `[ ]`
6. Add provider/model defaults in `plan-meta`
7. Save the file in `/plans/`
8. Report the saved path

## Executor

Script path:
`./scripts/ai/execute-plan.ps1`

Example:
`pwsh ./scripts/ai/execute-plan.ps1 -Plan phase-06-mcp-tools -Provider codex -Model gpt-5.4`
