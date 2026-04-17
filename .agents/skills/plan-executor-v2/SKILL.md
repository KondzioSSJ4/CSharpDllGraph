---
name: plan-executor-v2
description: Use when the user wants to execute an existing plan file by dispatching sub-agents. Triggers when user says "run plan X", "execute the plan", "continue plan from task N", "run remaining tasks in plan X", "execute plan v2", or any request to dispatch agents to implement tasks from a saved plan file in /plans/. Prefer this over plan-executor for cost efficiency.
---

# Plan Executor v2

You are a dispatcher. You do NOT write code. Group pending tasks by domain, then delegate each group to one sub-agent — possibly in parallel.

## Step 1: Discover the Plan

1. List `plans/` directory silently (do NOT output the list)
2. Fuzzy-match the user's request to a filename
3. If ambiguous or unspecified, list options and ask

## Step 2: Read and Group

Read the plan file ONCE. Extract all pending tasks (skip `[x]`). Group by domain:

| Signal | Group |
|--------|-------|
| React components, UI, Tailwind, hooks, forms | `frontend-ui` |
| Three.js, @react-three, 3D, scene, visualization | `frontend-3d` |
| Domain logic, calculations, energy models, types | `domain-logic` |
| Vite, tsconfig, package.json, build config | `config` |
| Docs (.md) | use the domain it describes |

Output grouping before proceeding:
```
Groups:
- frontend-ui: Task 1, 3
- domain-logic: Task 2
```

## Step 3: Delegate

For each group, invoke ONE sub-agent with ALL tasks for that group in plan order.

**Run independent groups in parallel** — if groups have no data dependencies between them, spawn their agents in the same message.

Use `model: "gpt-5.3-codex"` for sub-agents. Never use any `gpt-5.4` variant (`gpt-5.4`, `gpt-5.4-mini`) for subtasks.

### Sub-Agent Prompt Template

```
You are implementing tasks from a React 18 + TypeScript + Vite project plan.

## Tasks (execute in order)

{paste ALL task blocks for this group — headings, Goals, Acceptance, Steps only. Omit Context: sections.}

## Acceptance Criteria

{list each task's Acceptance line, or "None" if absent}

## Rules

- Read PRODUCT_DEFINITION.md for scope and terminology before starting
- Explore existing code patterns before writing
- Implement ONLY what these tasks require
- Do NOT commit changes
- Do NOT run validation — orchestrator handles it

## Response Format (≤50 words)

Status: SUCCESS or FAILURE
Tasks completed: (numbers)
Files changed: (paths)
Failure reason: (one sentence, if failed)
```

## Step 4: After Each Group Returns

1. FAILURE → STOP. Report what failed and why. Do not continue.
2. Mark tasks done in the plan file:
   - `## [ ] Task N:` → `## [x] Task N:`
   - `- [ ] N.X` → `- [x] N.X`
3. Report: `✓ Tasks {ids} done ({group})`
4. Proceed to next group.

## Step 5: After All Groups Succeed

Run validation once:
```
npm run typecheck && npm run build
```

Validation fails → STOP and report.

Validation passes → report:
```
✓ Plan complete ({done}/{total} tasks)
```

## On Failure

STOP. Report which task/group failed and why. Leave files as-is for manual inspection. Never revert.

## Rules

- NEVER write code yourself
- ONE sub-agent per domain group
- Independent groups → spawn in parallel
- No git commits
- No code review step
- No prior-task history in sub-agent prompts — agents read the filesystem
- SKIP tasks marked `[x]`, resume from first pending group
