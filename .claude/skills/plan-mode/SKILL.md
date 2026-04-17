---
name: plan-mode
description: Use when the user invokes /plan or wants to enter planning mode for thorough analysis before implementation. Triggers on "let's plan", "plan this", "analyze before we code", or any request to think through a feature or change carefully before touching code. Interview the user relentlessly to reach a shared understanding before producing a plan.
---

# Planning Mode

You are now in planning mode.

## Planning Workflow

### Phase 1 — Discovery & Questions

Explore the codebase silently (read files, grep, check graph). Then present to the user:

1. **Twoje sugestie / ulepszenia** — co warto dodać, zmienić, przemyśleć ponad to co użytkownik podał. Krótko, bez uzasadnień meta-procesu.
2. **Pytania** — tylko te, których nie można odpowiedzieć eksplorując kod. Format:

```
1. [CRITICAL/IMPORTANT] Pytanie?
   a) Opcja A
   b) Opcja B

2. [CRITICAL/IMPORTANT] Pytanie?
   a) Opcja A
   b) Opcja B
```

Nie opisuj jak planujesz planować. Nie wyjaśniaj kroków procesu. Pytaj i proponuj — nic więcej.

### Phase 2 — Output

Gdy użytkownik odpowie na pytania i zaakceptuje kierunek — natychmiast wywołaj skill `plan-output`. Nie pytaj jak dostarczyć plan. Nie pokazuj planu w czacie. Po prostu uruchom `plan-output`.

Jeśli request bazuje na pliku z `improvements/`, przekaż tę ścieżkę do `plan-output` żeby mógł przenieść plik do `done/improvements/`.

### Phase 3 — Implementation (if requested)

Execute the confirmed plan with focus and precision.

### Phase 4 — Validation

After implementation:
- [ ] `npm run typecheck`
- [ ] `npm run build`
- [ ] Check for errors, missing imports, broken dependencies

Report: what checked, any issues found/fixed.

## Rules

- Eksploruj kod zamiast pytać, gdy można znaleźć odpowiedź samemu
- Krótko — jedna myśl na zdanie
- Nie komentuj własnego procesu planowania
- Nie pytaj o format outputu — zawsze `plan-output`
