# CSharpDllGraph — Definicja produktu

> Źródło faz i kolejności realizacji: [`plans/README.md`](../plans/README.md)

---

## Cel produktu

CSharpDllGraph to serwer MCP (Model Context Protocol) realizujący statyczną analizę grafową ekosystemu .NET.
Narzędzie buduje graf zależności na podstawie pliku `.sln`, a następnie udostępnia zestaw precyzyjnych
narzędzi zapytaniowych, z których korzystają modele językowe działające w trybie generowania kodu
oraz deweloperzy pracujący bezpośrednio z CLI.

Żadne z narzędzi nie używa modelu językowego ani osadzeń wektorowych — cała logika opiera się
na statycznej analizie kodu źródłowego i metadanych pakietów NuGet.

---

## Użytkownicy

| Użytkownik | Tryb użycia | Główna potrzeba |
|---|---|---|
| Model językowy (AI w trybie code-gen) | Klient MCP (stdio) | Precyzyjne kontekst-minimalne odpowiedzi bez halucynacji |
| Deweloper | CLI / klient MCP | Szybka nawigacja po dużej bazie kodu bez otwierania IDE |

---

## Narzędzia MCP — v1 (faza 6)

Wszystkie narzędzia są tylko do odczytu; żadne nie modyfikuje kodu użytkownika.
Realizowane w fazie 6 po ukończeniu analizy strukturalnej (fazy 1–5).

| # | Narzędzie | Opis |
|---|---|---|
| 1 | `describe_package_api` | Publiczna powierzchnia pakietu NuGet w danej wersji |
| 2 | `find_usages` | Miejsca, w których symbol X jest używany w kodzie użytkownika |
| 3 | `trace_http_call` | Wywołania trafiające do endpointu X (między workspace'ami) |
| 4 | `list_dependencies` | Rozwiązane wersje pakietów per projekt |
| 5 | `find_version_conflicts` | Ten sam pakiet w różnych wersjach w ramach solucji |
| 6 | `suggest_usage` | Kanoniczne miejsca użycia symbolu wyekstrahowane z istniejącego kodu |

---

## Fazy realizacji

Szczegółowe pliki faz i bramki VALIDATE-STOP opisano w [`plans/README.md`](../plans/README.md).

| Faza | Status | Fokus |
|---|---|---|
| 0 | ✓ | Scaffold — solucja, host MCP, szkielet dokumentacji |
| 1 | ✓ | Schemat grafu + JSON store |
| 2 | ✓ | Provider .NET — pakiety, assemblies, typy, metody |
| 3 | ✓ | Krawędzie użycia (Roslyn) |
| 4 | ✓ | Endpointy HTTP — analiza statyczna |
| 5 | ✓ | Specyfikacje HTTP (OpenAPI/Swagger, `.http`, Postman) + reconciliation |
| 6 | ✓ | Implementacja 6 narzędzi MCP |
| 7 |   | CLI + file watcher, inkrementalny rebuild |

---

## Model grafu — faza 1

Faza 1 wprowadza wspólny model grafu dla całego produktu.
Każdy element reprezentowany jest jako węzeł z jednoznacznym identyfikatorem.
Relacje między elementami reprezentowane są jako krawędzie.
Model obejmuje strukturę kodu, zależności, elementy HTTP i odwołania zewnętrzne.
Dane grafu zapisywane są per workspace w małych plikach JSON podzielonych na typy.
Zapis jest deterministyczny, aby ułatwić porównywanie zmian między kolejnymi przebudowami.
Na tym modelu opiera się warstwa zapytań używana później przez narzędzia MCP.

---

## Non-goals dla v1

- **Brak runtime capture** — narzędzie nie przechwytuje ruchu HTTP w czasie działania aplikacji.
- **Brak LLM w narzędziach** — żadne narzędzie nie wywołuje modelu językowego ani osadzeń.
- **Brak automatycznego merge workspace'ów** — rozwiązywanie odbywa się wyłącznie w czasie zapytania.
- **Brak providerów innych niż .NET** — seam dla przyszłych providerów zarezerwowany w fazie 2,
  ale żaden inny provider nie jest dostarczany w v1.

---

## Wymagania techniczne (minimalne)

- Platforma: .NET 10
- Biblioteka MCP: `ModelContextProtocol` v1.1.0, transport `WithStdioServerTransport`
- Format solucji: `.slnx`
- Struktura katalogów: `src/<Project>/` i `tests/<Project>.Tests/`
- Wspólny plik `Directory.Build.targets` w korzeniu repozytorium
