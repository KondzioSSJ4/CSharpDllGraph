---
name: plan-status
description: Use when the user wants to check the status of plans or improvements. Triggers on "plan status", "/plan-status", "show plan status", "what plans are done", "check plans", "which plans are finished", "improvements status", "show improvements", "what's in progress", or any request to get an overview of pending/completed plans or improvement documents.
---

# Plan Status

Scan `plans/` and `improvements/` directories and report status of each file.

## How to determine status

Use **Glob** to find all `.md` files in `plans/` and `improvements/`. For each file, use **Grep** (pattern `\[ \]|\[~\]|\[-\]`, file by file) or **Read** to check for unchecked tasks:

- **Done** — no `[ ]`, `[~]`, or `[-]` remain
- **In Progress** — has `[~]` tasks (but no `[ ]`)
- **Pending** — has one or more `[ ]` tasks

Extract the title from the first `#` heading in the file. If no heading, use the filename.

Count remaining tasks: total of `[ ]` + `[~]` + `[-]` occurrences.

## Output format

Print two top-level sections — **Not Done** and **Done**. Inside **Not Done**, show Plans and Improvements as separate sub-sections. Inside **Done**, also show Plans and Improvements as separate sub-sections. Use this exact structure:

```
## Not Done

### Plans

#### In Progress
- [~] plan-title (filename) — 2 tasks remaining

#### Pending
- [ ] plan-title (filename) — 5 tasks remaining

### Improvements

#### In Progress
- [~] improvement-title (filename) — 1 task remaining

#### Pending
- [ ] improvement-title (filename) — 3 tasks remaining

---

## Done

### Plans
- plan-title (filename)
- plan-title (filename)

### Improvements
- improvement-title (filename)
```

- If a sub-section (In Progress / Pending) or a Plans/Improvements group has no entries, omit it entirely.
- If the entire **Not Done** or **Done** section has no entries, omit it entirely.
- If a directory doesn't exist, skip it silently.
- Sort entries within each group alphabetically by filename.

## Move done plans to `done/plans/`

After classifying all files, **move every Done plan** from `plans/` into `done/plans/`:

1. Ensure `done/plans/` exists (create if missing).
2. For each plan classified as Done, run: `mv plans/<filename> done/plans/<filename>`.
3. Do **not** move `improvements/` files — only `plans/`.
4. After moving, note which files were relocated in the output (one line: "Moved N plan(s) to done/plans/").
5. Files already inside `done/plans/` are ignored by the Glob (it only scans `plans/*.md`).

## Workflow

1. Glob `plans/*.md` and `improvements/*.md`
2. For each file: grep for `\[ \]`, `\[~\]`, and `\[-\]` to count remaining tasks; grep for first heading to get title
3. Classify each file into Done / In Progress / Pending
4. Move all Done plans from `plans/` to `done/plans/` (create dir if needed)
5. Print the grouped summary as shown above, followed by the move note
