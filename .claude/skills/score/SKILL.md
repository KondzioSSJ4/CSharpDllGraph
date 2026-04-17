---
name: score
description: Output a quick numeric scorecard 1-10. Use whenever the user asks to score, rate, evaluate, or compare options — even casually ("how good is this?", "score it", "give me numbers", "rate this solution", "which is better — score them", "what should we use for X"). Outputs a numbered list of decisions/topics, each with lettered options scored [1-10] where 10=best, 1=worst. Inverts automatically for cost/complexity/risk metrics (lower = better = higher score).
---

Output a numbered list of topics or decisions. No prose before or after — just the list.

For each topic, list available options as lettered items with a score in brackets `[N]`.

Format:
```
1. <Topic or decision>
a) [N] <Option>
b) [N] <Option>
c) [N] <Option>
```

Rules:
- Scores 1–10: 10 = best, 1 = worst
- Invert for cost/complexity/risk (lower = better = higher score)
- If only one option exists for a topic, still show it as `a) [N] <option>`
- Suggest realistic alternatives when the user hasn't listed them — score those too
- No rationale inline — keep it scannable. If needed, add a brief note after the letter block

**Example:**
```
1. What DB will we use?
a) [8] SQLite
b) [1] Azure Cosmos DB (noSQL)
c) [9] PostgreSQL

2. Queue technology?
a) [9] Azure Service Bus
b) [3] Azure Storage Account queues
c) [5] Amazon SQS
```
