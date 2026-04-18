# Plan: Phase 04 — HTTP Static Analysis

> Add static HTTP producer and consumer extraction on top of semantic graph data. Normalize URLs across server and client code, prove matching behavior on fixtures, then stop at the gate.

## [ ] Task 1: Extract ASP.NET controller endpoints

Goal: Emit HTTP producer nodes from attribute-routed controllers.

Context: User methods and source spans arrive from phase 03. HTTP graph nodes reuse the phase-01 graph contract in #[[file:src/CSharpDllGraph.Engine/Graph/NodeKind.cs]] and #[[file:src/CSharpDllGraph.Engine/Graph/EdgeKind.cs]].

Acceptance: Controller routes become normalized `HttpEndpoint` nodes with `HandlesRoute` edges from the producing method.

- [ ] 1.1 Add a Roslyn visitor scoped to controller classes
- [ ] 1.2 Combine controller-level and method-level route attributes into one template
- [ ] 1.3 Emit `HttpEndpoint` nodes keyed by HTTP method, normalized template, and project version
- [ ] 1.4 Emit `HandlesRoute` edges from user methods to endpoint nodes
- [ ] 1.5 Capture obvious response metadata from attributes when available

## [ ] Task 2: Extract Minimal API producers

Goal: Cover `MapGet`, `MapPost`, and related endpoint registration patterns.

Context: This work belongs in the same provider pipeline as the controller extractor.

Acceptance: Minimal API handlers produce normalized endpoint nodes and are linked back to the actual handler method or synthesized lambda method.

- [ ] 2.1 Detect `MapGet`, `MapPost`, `MapPut`, `MapDelete`, and `MapPatch` calls
- [ ] 2.2 Extract constant route templates; mark unresolved dynamic routes as `unknown` with confidence metadata
- [ ] 2.3 Resolve named handlers or synthesize handler nodes for inline lambdas
- [ ] 2.4 Emit `HandlesRoute` edges from handlers to endpoint nodes

## [ ] Task 3: Extract `HttpClient` consumer call sites

Goal: Emit normalized HTTP call sites from C# client code.

Context: Phase 03 already tracks user methods and source spans. This task adds `HttpCallSite` plus `CallsRoute` edges.

Acceptance: Constant and partly dynamic `HttpClient` URLs become normalized call-site nodes that can later be matched to producers.

- [ ] 3.1 Detect `GetAsync`, `PostAsync`, `PutAsync`, `DeleteAsync`, `PatchAsync`, and `SendAsync`
- [ ] 3.2 Normalize constant, interpolated, and composed URLs into path templates with `{var}` placeholders
- [ ] 3.3 Resolve named-client base addresses from simple `IHttpClientFactory` DI patterns when possible
- [ ] 3.4 Emit `HttpCallSite` nodes and `CallsRoute` edges keyed by method plus normalized path

## [ ] Task 4: Extract JS and TS `fetch` and `axios` calls

Goal: Cover common browser and frontend client call sites without a full JS parser.

Context: Frontend scanning is optional in v1 but included in the plan scope. Skip generated directories.

Acceptance: Regex-backed extraction yields stable `HttpCallSite` nodes for simple `fetch` and `axios` usage in fixture files.

- [ ] 4.1 Scan JS and TS files for `fetch(...)` and `axios.<verb>(...)`
- [ ] 4.2 Emit `HttpCallSite` plus `CallsRoute` with source metadata and confidence flags
- [ ] 4.3 Skip `node_modules`, `dist`, `build`, `.next`, `out`, and `coverage`
- [ ] 4.4 Add fixture examples covering both constant and partly dynamic URLs

## [ ] Task 5: Normalize URLs consistently

Goal: Make producer and consumer URLs comparable across frameworks and syntaxes.

Acceptance: The same logical route normalizes to the same `(method, path)` pair on both producer and consumer sides.

- [ ] 5.1 Lowercase host data when present and strip trailing slashes
- [ ] 5.2 Normalize route parameters such as `:id`, `{id}`, and `{id:int}` to `{id}`
- [ ] 5.3 Preserve HTTP method and normalized path as separate fields
- [ ] 5.4 Add unit tests for edge cases like catch-alls and optional trailing slashes

## [ ] Task 6: Prove static HTTP analysis on fixtures

Goal: Validate producer and consumer extraction inside one workspace.

Context: Extend the sample solution with an API project and a small frontend sample.

Acceptance: Fixture output contains matched producers and consumers built from controllers, Minimal APIs, `HttpClient`, `fetch`, and `axios`.

- [ ] 6.1 Add `SampleApi` and frontend fixture inputs to `tests/Fixtures/`
- [ ] 6.2 Add integration tests asserting endpoint nodes, call-site nodes, and route edges
- [ ] 6.3 Verify normalized templates match inside a single workspace
- [ ] 6.4 Keep cross-workspace matching deferred to phase 06

## [ ] Task 7: Pass the validate-stop gate

Goal: Close phase 04 with verified static HTTP extraction and halt before phase 05.

Acceptance: Producers and consumers are both extracted, templates match correctly, and summary output is signed.

- [ ] 7.1 Prove controller and Minimal API producers are extracted
- [ ] 7.2 Prove `HttpClient`, `fetch`, and `axios` consumers are extracted
- [ ] 7.3 Prove normalization produces matching templates on both sides
- [ ] 7.4 Print a signed summary and halt for human sign-off

## [ ] Task 8: Sync product definition

Goal: Keep product docs aligned with static HTTP analysis support.

Context: Update #[[file:docs/PROJECT_DEFINITION.md]] and #[[file:docs/REQUIREMENT_DEFINITION.md]] after validation.

- [ ] 8.1 Review `docs/PROJECT_DEFINITION.md` against new HTTP analysis behavior
- [ ] 8.2 Update touched phase-04 requirements in `docs/REQUIREMENT_DEFINITION.md`
- [ ] 8.3 Respond with `Product definition: updated` or `Product definition: no update required`
