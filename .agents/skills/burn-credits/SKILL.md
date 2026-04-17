---
name: burn-credits
description: >
  Burn-credits mode: a high-throughput autonomous improvement loop that works through all pending plans, detects UX issues via Playwright, researches competitive apps, and produces scored improvement documents — then queues everything for planning. Use when the user says "burn credits", "run everything", "autonomous improvement loop", "burn through tasks", "do everything", "auto-improve", or any request to unleash Codex on the full backlog without hand-holding. Ideal for unattended runs where the user wants maximum output per session.
---

# Burn Credits — Autonomous Improvement Loop

This mode maximizes productive output in a single session. It runs four phases, as many in parallel as possible.

Before starting, orient yourself:
1. **Identify the plans directory** — look for `plans/` in the project root; if absent, check `docs/plans/`, `.plans/`, or skip Phase 1 with a note.
2. **Identify the product definition** — look for `PRODUCT_DEFINITION.md`, then `AGENTS.md`, then `README.md`. Read whichever exists to understand what this product is. Do NOT skip this — you need product context for Phases 2–4.
3. **Ensure `improvements/` exists** at the project root. Create it if missing (the sub-agents will write there).

---

## Phase 1 — Execute Incomplete Plans

### Goal
Find every plan file with incomplete tasks and dispatch a sub-agent to execute each one.

### How to detect incomplete plans

A plan is incomplete if its file contains any line matching `## [ ]` (incomplete task heading) or `- [ ]` (incomplete sub-step).

```
# Quick check: grep -l '## \[ \]' plans/*.md
```

For each incomplete plan file:
- Extract the incomplete task headings and their count
- Spawn **one sub-agent per plan** (not per task — the plan-executor handles task ordering internally)

### Sub-agent prompt for each incomplete plan

```
You are executing a plan file. Use the plan-executor skill.

Plan file: {relative-path-to-plan-file}

Your job: open the plan-executor skill (read .Codex/skills/plan-executor/SKILL.md),
then execute ALL incomplete tasks in the plan. Follow the plan-executor rules exactly:
one sub-agent per task, validate after each, mark done with [x], stop on failure.

If the project has a AGENTS.md, read it first for conventions.

Report back: SUCCESS or FAILURE, list of tasks completed, any blockers.
```

Run all plan sub-agents **in parallel** (use multiple Agent tool calls in one message).

### After Phase 1
Wait for all plan sub-agents to finish. Note any failures — include them in the Phase 4 planning context.

---

## Phase 2 — UX Analysis via Playwright

### Goal
Run the app, navigate through all major screens, identify UX issues and improvement opportunities. Enrich findings with web research.

### Sub-agent prompt

```
You are a UX analyst. Your job:

1. READ the product definition:
   - Try PRODUCT_DEFINITION.md, then AGENTS.md, then README.md
   - Understand what the app does, who it's for, and what its main workflows are

2. START the dev server if it's not running:
   - Try `npm run dev` or `npm start` in the background
   - Wait for it to be ready (check localhost:3000, localhost:5173, or whatever port the project uses)
   - If already running, use the existing server

3. USE Playwright (read .Codex/skills/playwright-skill/SKILL.md for the pattern) to:
   - Take a full-page screenshot of the main screen
   - Navigate through every major section/tab/view you can find
   - Screenshot each one
   - Try interactive elements: forms, modals, buttons, dropdowns
   - Note anything that looks broken, confusing, missing, or improvable

4. ANALYZE what you observed. For each issue or improvement, consider:
   - Is the UI clear and self-explanatory to a first-time user?
   - Are there missing labels, unclear affordances, layout problems?
   - Are there features that obviously should exist but don't?
   - Performance: is anything slow to render or respond?
   - Mobile/responsive: does it handle narrow viewports?

5. SEARCH THE WEB for UX best practices relevant to the app type:
   - Search: "{product type} UX best practices 2024"
   - Search: "common UX mistakes {product type}"
   - Synthesize web findings with your own observations

6. WRITE the results to: improvements/{YYYY-MM-DD}-ux-analysis.md

   Use this format:
   ---
   # UX Analysis — {date}

   ## Summary
   Brief overview of findings.

   ## Improvements

   ### 1. {Improvement Title}
   **Score: {1-10}/10** (10 = high value, 1 = trivial)
   **Category:** {usability | visual | accessibility | performance | missing-feature}
   **Description:** What the problem is and where it appears.
   **Benefits:**
   - ...
   **Implementation hint:** (optional, 1-2 sentences)
   **Source:** (if from web research, cite the source)

   ### 2. ...
   ---

   Include at least 5 improvements. Score them honestly.

Report back: number of improvements found, path to the file written.
```

---

## Phase 3 — Competitive Analysis

### Goal
Research similar applications, compare their features to ours, identify what they do better, and document actionable improvements.

### Sub-agent prompt

```
You are a product analyst doing competitive research.

1. READ the product definition:
   - Try PRODUCT_DEFINITION.md, then AGENTS.md, then README.md
   - Understand the product domain, target users, key features, and gaps

2. SEARCH THE WEB for similar applications:
   - Search: "{product type} alternatives" / "{product type} tools" / "best {product type} software"
   - Find 3–5 real competing or complementary apps
   - For each: note their name, URL, key features, and what they do uniquely well

3. COMPARE each competitor to our product:
   - What features do they have that we don't?
   - What UX patterns do they use that work well?
   - What are they missing that we could use as a differentiator?

4. WRITE the results to: improvements/{YYYY-MM-DD}-competitive-analysis.md

   Use this format:
   ---
   # Competitive Analysis — {date}

   ## Product Summary
   One paragraph describing what our product does.

   ## Competitors Reviewed
   - **{App Name}** — {URL} — {1-sentence description}

   ## Improvements

   ### 1. {Improvement Title}
   **Score: {1-10}/10** (10 = high value, 1 = trivial)
   **Inspired by:** {competitor name + URL}
   **Description:** What they do and how we could adapt it.
   **Benefits:**
   - ...
   **Differentiation note:** (optional) How our implementation could be better than theirs.

   ### 2. ...
   ---

   Include at least 5 improvements. Prioritize high-score items.

Report back: competitors researched, number of improvements found, path to the file written.
```

### Parallelism
Run Phase 2 and Phase 3 **in parallel** — spawn both sub-agents in the same message. They are independent.

---

## Phase 4 — Plan the Improvements

### Goal
After Phases 2 and 3 finish, use `plan-mode` to create actionable implementation plans for the highest-scoring improvements.

### Steps

1. **Read all new improvement files** from `improvements/` created in this session.
2. **Filter**: collect all improvements with score ≥ 7.
3. **Deduplicate**: if Phase 2 and Phase 3 surfaced the same issue, merge them.
4. **Invoke `plan-mode`** (read `.Codex/skills/plan-mode/SKILL.md`) to design implementation plans for the top improvements.

   Frame the plan-mode request like this:
   ```
   We have {N} high-priority improvements identified from UX analysis and competitive research.
   Here they are in priority order:
   {list: score, title, brief description for each}

   Please plan the implementation of the top {3 or all, whichever is feasible} improvements.
   For each, produce a plan using the plan-output skill so it's saved to plans/.
   ```

5. If Phase 1 had failures, add them to the plan-mode context as blockers to address first.

---

## Execution Order

```
[Start]
  │
  ├── Phase 1: Execute incomplete plans (parallel: one sub-agent per plan)
  │
  ├── Phase 2: Playwright UX analysis    ─┐
  ├── Phase 3: Competitive analysis      ─┘  (run in parallel with each other, after Phase 1)
  │
  └── Phase 4: Plan improvements  (after Phases 2 & 3 complete)
```

---

## Output Summary

At the end, report:
```
## Burn Credits Complete

### Phase 1 — Plans executed
- {plan name}: {DONE / FAILED: reason}

### Phase 2 — UX Analysis
- File: improvements/{date}-ux-analysis.md
- {N} improvements found, top score: {X}/10

### Phase 3 — Competitive Analysis
- File: improvements/{date}-competitive-analysis.md
- {N} improvements found, competitors: {list}

### Phase 4 — Implementation Plans
- {N} plans created in plans/
- Top priorities: {list titles}
```

---

## Notes

- **Date format**: always use `YYYY-MM-DD` in filenames and document headers.
- **Scores**: be honest — not everything is a 9 or 10. A realistic distribution helps prioritization.
- **Product definition file**: never hardcode the app name or domain in this skill. Always read the project's own documentation to discover what it does.
- **If `plans/` doesn't exist**: skip Phase 1, note it in the output summary.
- **If Playwright or the dev server fails**: skip Phase 2 with a note; proceed with Phases 3 and 4.
- **Don't commit**: per project conventions, do not create git commits. Leave that to the user.
