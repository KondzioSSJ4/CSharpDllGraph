# AGENTS.md — CSharpDllGraph

## Product

- Primary source of truth: `docs/PROJECT_DEFINITION.md` — read before planning, implementing, reviewing, or answering product-related questions.
- Treat `docs/PROJECT_DEFINITION.md` as the canonical documentation for product scope and terminology.
- If a request conflicts with `docs/PROJECT_DEFINITION.md`, flag the conflict and ask whether to update the document first.
- Before every final response, run a product sync check: if the task changed scope, users, flows, rules, constraints, or success metrics — update `docs/PROJECT_DEFINITION.md` before finalizing.
- Every final response must include one of:
  - `Product definition: updated`
  - `Product definition: no update required`

---

## Language policy

Only English allowed.

---

## Writing style — Caveman Compression

Write concisely. Remove unnecessary grammar. Keep facts, numbers, names, constraints.

- Remove filler words: "therefore", "however", "because", "in order to", "essentially"
- Short sentences — one idea per sentence
- Action verbs: "add", "check", "fix", "run"
- Active voice: "compute value" not "value is computed"

Apply to: plan steps, sub-agent instructions, own responses.
Do not apply to: UI content visible to end users.

---

## Default behaviors

- Do NOT create tests unless explicitly requested.
- Do NOT generate documentation unless explicitly requested.
- Do NOT add code comments unless necessary.
- **Git commits**: do NOT commit after each task. Make one commit after ALL plan tasks are done. Sub-agents skip the "Commit your work" step and report without committing.
- **File edits**: minimize bash reads. Edit files directly with Read/Edit/Write tools.

---

## Test quality

- Tests must verify **real observable behavior** — inputs, outputs, side effects.
- **Tautological mocks are forbidden.** A mock that always returns the expected value without exercising logic is not a test.
- Tests must fail before a correct implementation and pass after.


---

## Stack

- .NET 10
- `ModelContextProtocol` v1.1.0, transport `WithStdioServerTransport`
- Solution format: `.slnx`
- Structure: `src/<Project>/` and `tests/<Project>.Tests/`
- `Directory.Build.targets` at repo root
