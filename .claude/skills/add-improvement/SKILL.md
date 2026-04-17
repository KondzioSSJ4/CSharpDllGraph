---
name: add-improvement
description: >
  Creates a new improvement note file in the improvements/ directory. Use whenever the user wants to add, record, or save an idea for a future feature, improvement, or enhancement — even if they say "zanotuj pomysł", "dodaj improvement", "zapisz to jako improvement", "chcę zapamiętać że...", or describes something the app could do but doesn't yet. The output is a structured .md file following the project's improvement format.
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
title: <tytuł po polsku>
score: <N>/10
category: <category>
---

# <tytuł po polsku>

**Score: <N>/10**
**Category:** <category>

## Czego oczekujemy

<Co użytkownik/system ma móc zrobić. Tylko to co wynika z opisu.>

## Zakres funkcji

<Lista rzeczy do zrobienia. Tylko to co wynika z opisu — nie wymyślaj.>
```

## Rules

- Write only what the user stated. Do not invent scope, integrations, or decisions.
- If the user gave little detail, write little. Short is fine.
- Sections `Integracja z aplikacją` and `Kluczowe decyzje do podjęcia przy planowaniu` are optional — add only if the user mentioned relevant details.
- Language: Polish for all content.

## Categories

- `missing-feature` — app doesn't have it
- `ux-improvement` — exists but could work better
- `performance` — speed or memory
- `integration` — external systems

## Score

1–10. Base on: user value, feasibility, fit with product. Don't overthink it.
