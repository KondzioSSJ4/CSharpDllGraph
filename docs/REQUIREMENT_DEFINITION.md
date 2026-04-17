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
| `[ ]` | Zdefiniowany schemat węzłów: `Package`, `Assembly`, `Type`, `Method` |
| `[ ]` | Zdefiniowany schemat krawędzi: `DependsOn`, `Contains`, `Calls`, `Implements` |
| `[ ]` | Klucze węzłów są wersjonowane i deterministyczne |
| `[ ]` | Graf serializuje się do JSON i deserializuje z byte-equality |
| `[ ]` | Test round-trip udowadnia byte-equality na grafie syntetycznym |
| `[ ]` | JSON store obsługuje operacje: zapis, odczyt, scalanie przyrostowe |

---

## Faza 2 — Provider .NET (strukturalny)

| Status | Wymaganie |
|---|---|
| `[ ]` | Provider wczytuje pakiet `.nupkg` i emituje węzły `Package` + `Assembly` |
| `[ ]` | Provider parsuje plik `.sln`/`.slnx` i odnajduje projekty |
| `[ ]` | Węzły `Type` i `Method` generowane są dla każdego assembly |
| `[ ]` | Krawędzie `Contains` łączą `Assembly → Type → Method` |
| `[ ]` | Krawędzie `DependsOn` odzwierciedlają zależności NuGet per projekt |
| `[ ]` | Provider ingests sample `.nupkg` + `.sln` i emituje oczekiwany graf strukturalny |
| `[ ]` | Seam dla przyszłych providerów (interfejs `IGraphProvider`) zarezerwowany |

---

## Faza 3 — Krawędzie użycia (Roslyn)

| Status | Wymaganie |
|---|---|
| `[ ]` | Roslyn workspace ładuje projekty z pliku solucji |
| `[ ]` | Semantyczny model Roslyn rozwiązuje symbole do węzłów grafu |
| `[ ]` | Krawędzie `Calls` między `Method` (kod użytkownika → symbol NuGet) |
| `[ ]` | Krawędzie oznaczone tagiem wersji pakietu docelowego |
| `[ ]` | Krawędzie `Implements` dla relacji implementacji interfejsów |
| `[ ]` | Testy weryfikują poprawność krawędzi na syntetycznym projekcie C# |

---

## Faza 4 — Endpointy HTTP (analiza statyczna)

| Status | Wymaganie |
|---|---|
| `[ ]` | Wykrywanie atrybutów routingowych ASP.NET (`[HttpGet]`, `[Route]`, itp.) |
| `[ ]` | Wykrywanie Minimal API (`app.MapGet`, `app.MapPost`, itp.) |
| `[ ]` | Wykrywanie wywołań `fetch`/`axios` w plikach JS/TS (opcjonalnie w v1) |
| `[ ]` | Węzły `HttpEndpoint` z znormalizowanym szablonem URL |
| `[ ]` | Krawędzie `Produces` łączące `Method → HttpEndpoint` |
| `[ ]` | Znormalizowane szablony URL pozwalają na dopasowanie producent–konsument |

---

## Faza 5 — Specyfikacje HTTP

| Status | Wymaganie |
|---|---|
| `[ ]` | Ingestion specyfikacji OpenAPI/Swagger (JSON i YAML) |
| `[ ]` | Ingestion plików `.http` (REST Client format) |
| `[ ]` | Ingestion kolekcji Postman (JSON v2.1) |
| `[ ]` | Endpointy ze specyfikacji uzgadniane z endpointami z analizy statycznej |
| `[ ]` | Rozbieżności raportowane jako krawędzie `SpecMismatch` lub atrybut węzła |

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
