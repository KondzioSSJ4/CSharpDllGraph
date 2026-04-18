# Plan: Phase 05 — HTTP Specs

> Ingest HTTP contract files and reconcile them with the static HTTP graph from phase 04. Preserve provenance, keep disagreements visible, then stop at the gate.

## [x] Task 1: Ingest OpenAPI and Swagger files

Goal: Turn OpenAPI descriptions into endpoint graph metadata without hiding provenance.

Context: Static HTTP endpoints from phase 04 already exist. Spec ingestion should extend them rather than replace them.

Acceptance: OpenAPI operations produce endpoint data and `DescribedBy` edges that point back to the spec source.

- [x] 1.1 Add a minimal OpenAPI reader after confirming the package license is acceptable
- [x] 1.2 Emit `HttpEndpoint` data with `source=openapi`
- [x] 1.3 Capture `operationId`, tags, and summarized schema references as attributes
- [x] 1.4 Emit `DescribedBy` edges to `ExternalRef` nodes that point at the spec file and operation path

## [x] Task 2: Ingest `.http` request files

Goal: Capture manual REST client requests as consumer call sites.

Context: `.http` requests should feed the same normalized route model used in phase 04.

Acceptance: Each request block yields an `HttpCallSite` with method, normalized path, headers, and request name context when available.

- [x] 2.1 Parse `.http` and REST Client files by request block
- [x] 2.2 Read the first request line as `METHOD URL`
- [x] 2.3 Normalize `{{variable}}` placeholders to `{var}` markers
- [x] 2.4 Emit `HttpCallSite` nodes with request-name metadata where present

## [x] Task 3: Ingest Postman collections

Goal: Capture Postman requests as consumer call sites with folder provenance.

Context: Postman collection data can arrive in nested trees and mixed URL formats.

Acceptance: Every request item in a fixture collection becomes one `HttpCallSite` with collection and folder metadata preserved.

- [x] 3.1 Walk the Postman `item` tree recursively
- [x] 3.2 Parse string and object URL forms
- [x] 3.3 Normalize variable substitutions when they are resolvable
- [x] 3.4 Emit `HttpCallSite` nodes with collection name and folder-path metadata

## [x] Task 4: Reconcile spec data with static HTTP graph data

Goal: Merge duplicate endpoints by route identity while preserving disagreements and provenance.

Context: Use the normalized `(method, path, version-token)` identity introduced in phase 04.

Acceptance: Matching endpoints merge source data without dropping conflicting values or overwriting one source with another.

- [x] 4.1 Merge endpoints by normalized method, normalized path, and version token
- [x] 4.2 Union attributes and combine `SourceRefs` from all sources
- [x] 4.3 Keep disagreements, such as different response metadata, visible as separate attributes
- [x] 4.4 Add unit tests for realistic merge and disagreement cases

## [x] Task 5: Prove spec ingestion on fixtures

Goal: Validate all three spec-source paths against committed sample inputs.

Context: Extend `tests/Fixtures/` with one OpenAPI file, one `.http` file, and one Postman collection aligned to the HTTP fixture.

Acceptance: Integration tests show all supported spec sources ingest successfully and reconcile with existing endpoints.

- [x] 5.1 Add deterministic fixture files for OpenAPI, `.http`, and Postman
- [x] 5.2 Add integration tests for each source type
- [x] 5.3 Assert merged endpoints keep full provenance
- [x] 5.4 Assert disagreement cases remain visible in the graph

## [x] Task 6: Pass the validate-stop gate

Goal: Close phase 05 with verified spec ingestion and halt before phase 06.

Acceptance: All supported spec inputs ingest, reconcile correctly, and the summary output is signed.

- [x] 6.1 Prove OpenAPI, `.http`, and Postman fixture ingestion is green
- [x] 6.2 Prove reconciliation preserves provenance across duplicate endpoints
- [x] 6.3 Prove disagreement cases keep both values without silent drops
- [x] 6.4 Print a signed summary and halt for human sign-off

## [x] Task 7: Sync product definition

Goal: Keep product docs aligned with HTTP spec ingestion support.

Context: Update #[[file:docs/PROJECT_DEFINITION.md]] and #[[file:docs/REQUIREMENT_DEFINITION.md]] after validation.

- [x] 7.1 Review `docs/PROJECT_DEFINITION.md` against new spec-ingestion behavior
- [x] 7.2 Update touched phase-05 requirements in `docs/REQUIREMENT_DEFINITION.md`
- [x] 7.3 Respond with `Product definition: updated` or `Product definition: no update required`
