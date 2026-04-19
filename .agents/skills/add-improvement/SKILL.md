---
name: add-improvement
description: >
  Creates a new improvement note file in the improvements/ directory. Use whenever the user wants to add, record, or save an idea for a future feature, improvement, or enhancement — even if they say "note this idea", "add improvement", "save this as improvement", "I want to remember that...", or describes something the app could do but doesn't yet. The output is a structured .md file following the project's improvement format.
---

# add-improvement

Creates a file in `improvements/YYYY-MM-DD_slug.md` based on what the user described.

## Filename

`improvements/YYYY-MM-DD_slug.md`
- Date: today
- Slug: kebab-case English, max 5 words, from the title

## File structure

```markdown
---
title: <title in English>
score: <N>/10
category: <category>
---

# <title in English>

**Score: <N>/10**
**Category:** <category>

## What we expect

<What the user/system should be able to do. Only what follows from the description.>

## Feature scope

<List of things to do. Only what follows from the description — do not invent.>
```

## Rules

- Write only what the user stated. Do not invent scope, integrations, or decisions.
- If the user gave little detail, write little. Short is fine.
- Sections `App integration` and `Key decisions for planning` are optional — add only if the user mentioned relevant details.
- Language: English for all content.

## Categories

- `missing-feature` — app doesn't have it
- `ux-improvement` — exists but could work better
- `performance` — speed or memory
- `integration` — external systems

## Score

1–10. Base on: user value, feasibility, fit with product. Don't overthink it.
