# Plan: Phase 04 — HTTP Static Analysis

> Extract producer (server) and consumer (client) HTTP endpoints purely from source. No specs yet — those are phase 05.

Prerequisite: phase 03 complete and signed off.

## [ ] Task 1: Producer — ASP.NET attributes

Goal: Emit `HttpEndpoint` nodes from `[Route]`, `[HttpGet/Post/Put/Delete/Patch]`, `[ApiController]`.

- [ ] 1.1 Roslyn visitor scoped to controller classes
- [ ] 1.2 Combine controller-level `[Route]` template with method-level attribute templates
- [ ] 1.3 Emit `HttpEndpoint` node keyed on `(method, normalized-template, project-version)`
- [ ] 1.4 Emit `HandlesRoute` edge from the user method node to the endpoint node
- [ ] 1.5 Record declared response types / status codes as attributes where obvious from attributes

## [ ] Task 2: Producer — Minimal API

Goal: Detect `app.MapGet/MapPost/MapPut/MapDelete/MapPatch` and their handler delegates.

- [ ] 2.1 Detect invocations on `WebApplication` or `IEndpointRouteBuilder` named `MapGet/MapPost/MapPut/MapDelete/MapPatch`
- [ ] 2.2 Extract the route literal — if non-constant, emit the endpoint with template `unknown` and `confidence=dynamic`
- [ ] 2.3 Resolve the handler: inline lambda → synthesize a method node; named method group → use its symbol
- [ ] 2.4 Emit `HandlesRoute` edge from handler to endpoint

## [ ] Task 3: Consumer — HttpClient family

Goal: Find call sites that hit an HTTP URL and emit `HttpCallSite` nodes.

- [ ] 3.1 Detect `HttpClient.GetAsync/PostAsync/PutAsync/DeleteAsync/PatchAsync/SendAsync` with constant, interpolated, or composed URL expressions
- [ ] 3.2 Record resolvable parts of the URL as the template; mark dynamic segments as `{var}`
- [ ] 3.3 Detect `IHttpClientFactory` named-client patterns; capture base address from DI configuration where resolvable via syntactic match
- [ ] 3.4 Emit `HttpCallSite` node + `CallsRoute` edge to the canonical endpoint reference (method + normalized path)

## [ ] Task 4: Consumer — JS/TS `fetch` and `axios`

Goal: Scan `.ts`, `.tsx`, `.js`, `.jsx`, `.vue`, `.svelte` files for HTTP call literals.

Context: No JS parser dependency in v1 — regex with line/column capture. Good enough; mark the confidence level in attributes.

- [ ] 4.1 Regex scanner with per-call-site capture for `fetch(...)` and `axios.{get,post,put,delete,patch}(...)`
- [ ] 4.2 Emit `HttpCallSite` + `CallsRoute` edge with `source=fetch|axios`, `confidence=regex`
- [ ] 4.3 Skip `node_modules`, `dist`, `build`, `.next`, `out`, `coverage` directories

## [ ] Task 5: URL normalization

Goal: Canonical template form so producer and consumer sides match across languages and repos.

- [ ] 5.1 Normalizer lowercases host, strips trailing slash, unifies path-params: `{id}` vs `:id` → `{id}`
- [ ] 5.2 Separate `method` and `path` fields on both endpoint and call-site nodes
- [ ] 5.3 Unit tests for edge cases: optional trailing slash, `{**catchall}`, route constraints (`{id:int}` → `{id}`)

## [ ] Task 6: Fixtures and tests

- [ ] 6.1 Add a `SampleApi` project (ASP.NET controller + Minimal API) and a `SampleUi` folder (static JS files with fetch/axios)
- [ ] 6.2 Integration test asserts endpoint and call-site nodes plus edges within a single workspace
- [ ] 6.3 Cross-workspace matching is explicitly deferred to phase 06

## [ ] Task 7: Sync product definition

- [ ] 7.1 Update docs
- [ ] 7.2 Status line

## VALIDATE-STOP CHECKLIST

- [ ] Producer endpoints extracted for controllers and Minimal APIs
- [ ] Consumer call sites extracted for HttpClient family and fetch/axios
- [ ] Templates normalize identically on both sides for matching URLs
- [ ] Print signed summary
- [ ] **HALT. Do not open `phase-05-http-specs.md`. Wait for sign-off.**
