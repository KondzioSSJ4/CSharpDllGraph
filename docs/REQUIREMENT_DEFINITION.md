# CSharpDllGraph — Definicja wymagań

> Symbole statusu: `[x]` ukończone · `[~]` częściowo · `[ ]` do zrobienia
>
> Kolejność faz i bramki VALIDATE-STOP: [`plans/README.md`](../plans/README.md)

---

## Faza 0 — Scaffold

| Status | Wymaganie |
|---|---|
| `[x]` | Repozytorium zawiera plik solucji `.slnx` z projektami `src/` i `tests/` |
| `[x]` | Projekt `src/CSharpDllGraph.Mcp` uruchamia serwer MCP na stdio |
| `[x]` | Host odpowiada na żądanie `initialize` i kończy działanie bez błędu |
| `[x]` | `Directory.Build.targets` skonfigurowany w korzeniu repozytorium |
| `[x]` | Szkielet dokumentacji (`docs/`, `AGENTS.md`) umieszczony w repozytorium |
| `[x]` | `plans/README.md` zawiera tabelę faz i aktywną fazę |

---

## Faza 1 — Schemat grafu i JSON store

| Status | Wymaganie |
|---|---|
| `[x]` | Zdefiniowany schemat węzłów: `Workspace`, `Project`, `Package`, `Assembly`, `Namespace`, `Type`, `Method`, `HttpEndpoint`, `HttpCallSite`, `ExternalRef` |
| `[x]` | Zdefiniowany schemat krawędzi: `Contains`, `References`, `DependsOn`, `Implements`, `Inherits`, `Calls`, `Uses`, `HandlesRoute`, `CallsRoute`, `DescribedBy` |
| `[x]` | Klucze węzłów są wersjonowane i deterministyczne (`{kind}:{fqn}@{version}`) |
| `[x]` | JSON store workspace działa w układzie shard-per-kind (`manifest.json`, `nodes/*.json`, `edges/*.json`) |
| `[x]` | Warstwa zapytań v0 udostępnia `GetNode`, `FindNodes`, `GetEdges`, `Neighbors` z indeksami wtórnymi |
| `[~]` | Round-trip byte-equality: implementacja i testy dodane; walidacja lokalna zablokowana przez lock `NuGetScratch`, odmowę zapisu MSBuild do `obj/*.cache` oraz brak odkrytych testów w istniejącej zbudowanej DLL |
| `[~]` | Test stabilności dwóch niezależnych runów: test dodany; walidacja lokalna zablokowana przez lock `NuGetScratch`, odmowę zapisu MSBuild do `obj/*.cache` oraz brak odkrytych testów w istniejącej zbudowanej DLL |

---

## Faza 2 — Provider .NET (strukturalny)

| Status | Wymaganie |
|---|---|
| `[x]` | Provider odczytuje rozwiązane artefakty pakietów NuGet z `project.assets.json` i emituje węzły `Package` + `Assembly` |
| `[x]` | Provider parsuje plik `.sln`/`.slnx` i odnajduje projekty |
| `[x]` | Węzły `Type` i `Method` generowane są dla każdego publicznego assembly |
| `[x]` | Krawędzie `Contains` łączą `Package → Assembly → Namespace → Type → Method` |
| `[x]` | Krawędzie `DependsOn` odzwierciedlają zależności NuGet per projekt i TFM |
| `[x]` | Fixture offline z sample solution i zablokowanymi artefaktami emituje oczekiwany graf strukturalny |
| `[x]` | Seam dla przyszłych providerów (`IGraphProvider` + pipeline engine) zarezerwowany |

---

## Faza 3 — Krawędzie użycia (Roslyn)

| Status | Wymaganie |
|---|---|
| `[x]` | Roslyn workspace ładuje projekty z pliku solucji |
| `[x]` | Semantyczny model Roslyn rozwiązuje symbole do węzłów grafu |
| `[x]` | Krawędzie `Calls` między `Method` (kod użytkownika → symbol NuGet) |
| `[x]` | Krawędzie oznaczone tagiem wersji pakietu docelowego |
| `[x]` | Krawędzie `Implements` dla relacji implementacji interfejsów |
| `[x]` | Testy weryfikują poprawność krawędzi na syntetycznym projekcie C# |

---

## Faza 4 — Endpointy HTTP (analiza statyczna)

| Status | Wymaganie |
|---|---|
| `[x]` | Wykrywanie atrybutów routingowych ASP.NET (`[HttpGet]`, `[Route]`, itp.) |
| `[x]` | Wykrywanie Minimal API (`app.MapGet`, `app.MapPost`, itp.) |
| `[x]` | Wykrywanie wywołań `fetch`/`axios` w plikach JS/TS (opcjonalnie w v1) |
| `[x]` | Węzły `HttpEndpoint` z znormalizowanym szablonem URL |
| `[x]` | Krawędzie `HandlesRoute` i `CallsRoute` łączące endpointy z metodami |
| `[x]` | Znormalizowane szablony URL pozwalają na dopasowanie producent–konsument |

---

## Faza 5 — Specyfikacje HTTP

| Status | Wymaganie |
|---|---|
| `[x]` | Ingestion specyfikacji OpenAPI/Swagger (JSON i YAML) |
| `[x]` | Ingestion plików `.http` (REST Client format) |
| `[x]` | Ingestion kolekcji Postman (JSON v2.1) |
| `[x]` | Endpointy ze specyfikacji uzgadniane z endpointami z analizy statycznej |
| `[x]` | Rozbieżności raportowane jako krawędzie `SpecMismatch` lub atrybut węzła |

---

## Faza 6 — Narzędzia MCP

| Status | Wymaganie |
|---|---|
| `[ ]` | Narzędzie `describe_package_api` zwraca publiczną powierzchnię pakietu NuGet |
| `[ ]` | Narzędzie `find_usages` zwraca miejsca użycia symbolu w kodzie użytkownika |
| `[ ]` | Narzędzie `trace_http_call` zwraca wywołania trafiające do endpointu X |
| `[ ]` | Narzędzie `list_dependencies` zwraca rozwiązane wersje pakietów per projekt |
| `[ ]` | Narzędzie `find_version_conflicts` wykrywa ten sam pakiet w różnych wersjach |
| `[ ]` | Narzędzie `suggest_usage` zwraca kanoniczne miejsca użycia symbolu |
| `[ ]` | Wszystkie narzędzia zwracają oczekiwane dane na podstawie fixture'ów testowych |
| `[ ]` | Żadne narzędzie nie wywołuje LLM ani osadzeń wektorowych |
| `[ ]` | Rejestr URL cross-workspace obsługuje `trace_http_call` między solucjami |

---

## Faza 7 — CLI i file watcher

| Status | Wymaganie |
|---|---|
| `[ ]` | CLI obsługuje polecenie `build <path-to-sln>` |
| `[ ]` | CLI obsługuje polecenie `serve` uruchamiające serwer MCP |
| `[ ]` | File watcher wykrywa zmiany w plikach `.cs` i `.csproj` |
| `[ ]` | Inkrementalny rebuild aktualizuje tylko zmienione węzły i krawędzie |
| `[ ]` | Latencja edit → rebuild → query poniżej 2 s na przykładowej solucji |
| `[ ]` | Testy weryfikują poprawność inkrementalnego rebuildu |
