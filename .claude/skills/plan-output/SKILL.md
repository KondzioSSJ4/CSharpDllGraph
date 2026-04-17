---
name: plan-output
description: Use when the user wants to create, write, or output an implementation plan saved to a file. Triggers when user says "create a plan", "write a plan for X", "plan this out", "save the plan", or any request to produce a structured implementation plan as a markdown file in /plans/.
---

# Plan Output Mode

When this skill is active, produce the implementation plan as a markdown file in the `/plans/` directory.

## File Naming

`plans/{YYYY-MM-DD}_{short-description}.md`

- Date is today's date
- Short description is kebab-case, max 4-5 words, derived from the plan topic
- Example: `plans/2026-03-23_add-ocr-module-endpoints.md`

## Plan Template

```markdown
# Plan: {Title}

> One or two sentences describing the overall goal of this plan.

## [ ] Task 1: {Task name}

Goal: One sentence — what this task produces or achieves.

Context: Key files, modules, patterns, or decisions from prior tasks that an agent needs to work independently. Omit if obvious.

Acceptance: Specific, observable outcome that defines when this task is complete. Omit if obvious from the goal.

- [ ] 1.1 Step description
- [ ] 1.2 Step description

## [ ] Task 2: {Task name}

Goal: One sentence — what this task produces or achieves.

Context: Key files, modules, patterns, or decisions from prior tasks that an agent needs to work independently. Omit if obvious.

Acceptance: Specific, observable outcome that defines when this task is complete. Omit if obvious from the goal.

- [ ] 2.1 Step description
- [ ] 2.2 Step description

## [ ] Task N: Sync product definition

Goal: Update `PRODUCT_DEFINITION.md` if the plan introduces new scope, users, flows, rules, constraints, or success metrics.

- [ ] N.1 Review `PRODUCT_DEFINITION.md` against the changes introduced by this plan
- [ ] N.2 Update `PRODUCT_DEFINITION.md` where applicable, preserving internal consistency. Skip if no changes apply.
- [ ] N.3 Respond with `Product definition: updated` or `Product definition: no update required`
```

## Conventions

- `[x]` = done, `[ ]` = not started, `[~]` = in progress — applies to both task headings and steps
- Steps must be specific and actionable — an LLM reading a step should know exactly what to do
- Keep 3-8 steps per task — fewer means the task is trivial (merge it), more means it should be split
- **Goal is required for every task** — without it, a sub-agent cannot know what it is building
- **Context** — include when: tasks touch non-obvious file locations, reference decisions made in prior tasks, or require knowing a specific pattern used elsewhere in the codebase. If a task depends on a specific output from an earlier task, state it here. Omit when the task is self-explanatory.
- **Acceptance** — include when the success criteria isn't obvious from the goal (e.g. a specific UI element is visible, a specific API shape is produced). Omit when the goal already makes it clear.
- **References** — use `#[[file:path]]` syntax in Context or steps to link relevant files when it helps the executing agent locate what it needs

## Workflow

1. Analyze the request and gather context (explore the codebase as needed)
2. Break the work into sequential tasks, each with a clear goal
3. Write steps that are concrete enough for an LLM agent to execute independently
4. Save the plan file to `/plans/` using the naming convention above
5. **If the plan was based on a file from `improvements/`**: move that source file to `done/improvements/` (create the directory if it doesn't exist). Use `mv` via Bash.
6. Present the saved plan file path to the user (and mention the moved improvement file if applicable)
