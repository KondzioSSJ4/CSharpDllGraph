# Phase 05 Summary — Spec Ingestion

## New Providers Added

| Provider | File | Description |
|---|---|---|
| `OpenApiSpecProvider` | `src/CSharpDllGraph.Providers.Dotnet/Http/OpenApiSpecProvider.cs` | Reads OpenAPI/Swagger JSON/YAML files; emits `HttpEndpoint` nodes with `source=openapi`, `operationId`, and `DescribedBy` edges to `ExternalRef` nodes |
| `HttpFileCallSiteProvider` | `src/CSharpDllGraph.Providers.Dotnet/Http/HttpFileCallSiteProvider.cs` | Reads `.http` / `.rest` files (REST Client format); emits `HttpCallSite` nodes with `requestName` where a `# @name` comment is present |
| `PostmanCallSiteProvider` | `src/CSharpDllGraph.Providers.Dotnet/Http/PostmanCallSiteProvider.cs` | Reads Postman collection JSON (v2.1); emits `HttpCallSite` nodes, resolves `{{variables}}`, preserves `folderPath` and `collectionName` |
| `HttpEndpointReconciler` | `src/CSharpDllGraph.Providers.Dotnet/Http/HttpEndpointReconciler.cs` | Merges duplicate `HttpEndpoint` nodes by `(method, normalizedPath)`; unions `SourceRefs`; prefixes conflicting attribute values with their source name (e.g. `summary:static`, `summary:openapi`) |

## Fixture Files

| File | Purpose |
|---|---|
| `tests/Fixtures/SampleApi/openapi.json` | OpenAPI 3.0 spec with 5 operations: `GET /api/users`, `POST /api/users`, `GET /api/users/{id}`, `DELETE /api/users/{id}`, `GET /api/health`. The health summary deliberately differs from the static value to exercise the disagreement path. |
| `tests/Fixtures/SampleApi/requests.http` | REST Client `.http` file with 3 request blocks. The first block carries `# @name listUsers` to test name extraction. |
| `tests/Fixtures/SampleApi/SampleApi.postman_collection.json` | Pre-existing Postman collection fixture (Task 3). |

## Test Classes and Methods Added

### `tests/CSharpDllGraph.Providers.Dotnet.Tests/SpecIngestionTests.cs`

**OpenAPI ingestion (Task 5.2)**
- `OpenApiSpecProvider_LoadsFixture_EmitsHttpEndpointNodesWithSourceOpenapi` — asserts ≥2 `HttpEndpoint` nodes, all with `source=openapi`
- `OpenApiSpecProvider_LoadsFixture_EmitsOperationIdAttribute` — asserts at least one node carries a non-empty `operationId`
- `OpenApiSpecProvider_LoadsFixture_EmitsDescribedByEdges` — asserts ≥2 `DescribedBy` edges from `HttpEndpoint` to `ExternalRef`
- `OpenApiSpecProvider_LoadsFixture_GetAllUsersEndpointPresent` — asserts the specific `GET /api/users` endpoint with `operationId=GetAllUsers`

**.http file ingestion (Task 5.2)**
- `HttpFileCallSiteProvider_LoadsFixture_EmitsHttpCallSiteNodes` — asserts ≥2 `HttpCallSite` nodes from `requests.http`
- `HttpFileCallSiteProvider_LoadsFixture_CorrectMethodAndPath` — asserts correct method/path for GET and POST blocks
- `HttpFileCallSiteProvider_LoadsFixture_RequestNamePresentWhenAnnotated` — asserts `requestName=listUsers` on the annotated block

**Postman ingestion (Task 5.2 / verify Task 3)**
- `PostmanCallSiteProvider_SampleApiFixture_EmitsHttpCallSiteNodes` — asserts ≥3 `HttpCallSite` nodes from the Postman fixture

**Provenance preservation (Task 5.3)**
- `Reconciler_StaticAndOpenApi_MergedNodeHasSourceRefsFromBothSources` — runs static pipeline + OpenAPI provider + Reconciler; asserts merged `GET /api/users` node has SourceRefs from both the controller file and `openapi.json`

**Disagreement visibility (Task 5.4)**
- `Reconciler_DisagreementOnSummary_BothTaggedAttributesPresent` — two crafted nodes with differing summaries; asserts `summary:static` and `summary:openapi` present, plain `summary` absent
- `Reconciler_AgreementOnSummary_PlainKeyRetained` — two nodes with identical summaries; asserts plain `summary` kept, tagged keys absent

---

Phase 05 HALT — awaiting human sign-off
