# Plan: Phase 05 — HTTP specs (OpenAPI + .http + Postman)

> Ingest explicit specs and reconcile them with the static-analysis endpoints from phase 04.

Prerequisite: phase 04 complete and signed off.

## [ ] Task 1: OpenAPI / Swagger ingestion

Goal: Parse `openapi.json/yaml` and `swagger.json/yaml` files discovered anywhere under the workspace.

- [ ] 1.1 Reference a minimal OpenAPI parser (`Microsoft.OpenApi.Readers` — verify license is acceptable before adding)
- [ ] 1.2 For every operation, emit `HttpEndpoint` with attribute `source=openapi`
- [ ] 1.3 Capture `operationId`, tags, and request/response schema refs as attributes (summarized — name + ref, not full expansion)
- [ ] 1.4 Emit `DescribedBy` edge from the endpoint to an `ExternalRef` node pointing at the spec file + path

## [ ] Task 2: `.http` / REST Client files

Goal: Parse VS Code REST Client `.http` and `.rest` files, emit call-site nodes.

- [ ] 2.1 Lightweight parser: split on `###`, parse first non-blank line as `METHOD URL`, subsequent `Header: value` lines, body after blank line
- [ ] 2.2 Substitute `{{variable}}` placeholders with `{var}` template markers
- [ ] 2.3 Emit `HttpCallSite` with `source=http-file`, include surrounding request name as display name

## [ ] Task 3: Postman collections

Goal: Parse Postman v2.1 collection JSON, emit call-site nodes.

- [ ] 3.1 Walk the `item` tree recursively (folders + requests)
- [ ] 3.2 Emit `HttpCallSite` per request with `source=postman`, record folder path and collection name as attributes
- [ ] 3.3 Handle `url` as either string or object form; support `variable` substitution where resolvable

## [ ] Task 4: Reconciliation

Goal: An endpoint described by multiple sources collapses to a single node whose provenance lists every source.

Acceptance: Endpoint defined in an ASP.NET controller AND in an OpenAPI spec yields ONE `HttpEndpoint` node with `sources=[static, openapi]`.

- [ ] 4.1 Merge key: `(normalized-method, normalized-path, version-token)`
- [ ] 4.2 Merge strategy: union attributes; `sources` array appended; `SourceRefs` combined
- [ ] 4.3 If specs disagree (operationId vs controller method name, differing response types), keep both as attributes; do NOT pick a winner
- [ ] 4.4 Unit tests for realistic merge cases including disagreement

## [ ] Task 5: Sync product definition

- [ ] 5.1 Update docs
- [ ] 5.2 Status line

## VALIDATE-STOP CHECKLIST

- [ ] All three spec sources ingest their respective sample inputs
- [ ] Reconciliation merges duplicates with full provenance preserved
- [ ] Disagreement cases retain both values without silent drops
- [ ] Print signed summary
- [ ] **HALT. Do not open `phase-06-mcp-tools.md`. Wait for sign-off.**
