---
name: plan-mode
description: Use when the user invokes /plan or wants to enter planning mode for thorough analysis before implementation. Triggers on "let's plan", "plan this", "analyze before we code", or any request to think through a feature or change carefully before touching code. Interview the user relentlessly to reach a shared understanding before producing a plan.
---

# Planning Mode

You are now in planning mode.

## Planning Workflow

### Phase 1 — Discovery & Questions

Explore the codebase silently (read files, grep, check graph). Then present to the user:

1. **Your suggestions / improvements** — what's worth adding, changing, or reconsidering beyond what the user provided. Brief, no meta-process justifications.
2. **Questions** — only those that cannot be answered by exploring the code. Format:

```
1. [CRITICAL/IMPORTANT] Question?
   a) Option A
   b) Option B

2. [CRITICAL/IMPORTANT] Question?
   a) Option A
   b) Option B
```

Do not describe how you plan to plan. Do not explain process steps. Ask and suggest — nothing more.

### Phase 2 — Output

When the user answers the questions and accepts the direction — ask which plan format:

```
Which plan format?
a) /plan-output — human-readable Markdown (default)
b) /plan-output-script — JSON with agents and dependencies, executable via execute-plan.ps1
```

Then immediately invoke the chosen skill. Do not show the plan in chat.

If the request is based on a file from `improvements/`, pass that path to the chosen skill so it can move the file to `done/improvements/`.

### Phase 3 — Implementation (if requested)

Execute the confirmed plan with focus and precision.

### Phase 4 — Validation

After implementation:
- [ ] `npm run typecheck`
- [ ] `npm run build`
- [ ] Check for errors, missing imports, broken dependencies

Report: what checked, any issues found/fixed.

## Rules

- Explore code instead of asking when you can find the answer yourself
- Brief — one thought per sentence
- Do not comment on your own planning process
- Always ask about the output format: `plan-output` vs `plan-output-script`
