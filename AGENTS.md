# AGENTS.md — CSharpDllGraph

## Produkt

- Główne źródło prawdy: `docs/PROJECT_DEFINITION.md` — przeczytaj przed planowaniem, implementacją, przeglądem lub odpowiadaniem na pytania związane z produktem.
- Traktuj `docs/PROJECT_DEFINITION.md` jako kanoniczną dokumentację zakresu i terminologii produktu.
- Jeśli żądanie jest sprzeczne z `docs/PROJECT_DEFINITION.md`, zgłoś konflikt i zapytaj, czy najpierw zaktualizować dokument.
- Przed każdą ostateczną odpowiedzią wykonaj sprawdzenie synchronizacji produktu: jeśli zadanie zmieniło zakres produktu, użytkowników, przepływy, reguły, ograniczenia lub mierniki sukcesu — zaktualizuj `docs/PROJECT_DEFINITION.md` przed finalizacją.
- W ostatecznej odpowiedzi zawsze umieść jedno z:
  - `Product definition: updated`
  - `Product definition: no update required`

---

## Polityka językowa

| Kontekst | Język |
|---|---|
| Kod, identyfikatory, komentarze, commit messages | **angielski** |
| `docs/PROJECT_DEFINITION.md` | **polski** |
| `docs/REQUIREMENT_DEFINITION.md` | **polski** |
| Ten plik (`AGENTS.md`) | **polski** |
| Pozostałe markdown poza `docs/` | **angielski** |

---

## Styl pisania — Caveman Compression

Pisz zwięźle. Usuń zbędną gramatykę. Zachowaj fakty, liczby, nazwy, ograniczenia.

- Usuń spójniki i wypełniacze: "dlatego", "jednak", "ponieważ", "w celu", "zasadniczo"
- Krótkie zdania — jedna myśl na zdanie
- Czasowniki akcji: "dodaj", "sprawdź", "napraw", "uruchom"
- Strona czynna: "oblicz wartość" nie "wartość jest obliczana"

Stosuj do: kroków planu, instrukcji dla sub-agentów, własnych odpowiedzi.
Nie stosuj do: treści UI widocznych dla użytkownika końcowego.

---

## Domyślne zachowania

- NIE twórz testów, chyba że wyraźnie zażądano.
- NIE generuj dokumentacji, chyba że wyraźnie zażądano.
- NIE dodawaj komentarzy do kodu jeśli nie są konieczne.
- **Commity git**: NIE commituj po każdym zadaniu. Zrób jeden commit po zakończeniu WSZYSTKICH zadań planu. Subagenty pomijają krok "Commit your work" i raportują bez commitowania.
- **Edycja plików**: minimalizuj odczyty bash. Edytuj pliki bezpośrednio narzędziami Read/Edit/Write.

---

## Symbole statusu

Używane wyłącznie w `docs/REQUIREMENT_DEFINITION.md`:

| Symbol | Znaczenie |
|---|---|
| `[x]` | Gotowe — wymaganie spełnione i zweryfikowane |
| `[~]` | Częściowe — praca rozpoczęta, niekompletna lub niezweryfikowana |
| `[ ]` | Do zrobienia — nie rozpoczęto |

Nigdy nie oznaczaj `[x]` bez zaliczonego testu lub ręcznej weryfikacji.

---

## Jakość testów

- Testy muszą weryfikować **rzeczywiste obserwowalne zachowanie** — wejścia, wyjścia, efekty uboczne.
- **Tautologiczne mocki są zabronione.** Mock zawsze zwracający oczekiwaną wartość bez uruchamiania logiki nie jest testem.
- Testy muszą nie przechodzić przed poprawną implementacją i przechodzić po niej.

---

## Reguła STOP — jedna faza na raz

Struktura faz i bramki VALIDATE-STOP: [`plans/README.md`](plans/README.md).

- Realizuj **jedną fazę na raz**.
- Na końcu każdej fazy: wykonaj listę VALIDATE-STOP z pliku fazy.
- Wydrukuj podpisane podsumowanie: co zrobiono, co zweryfikowano.
- **ZATRZYMAJ SIĘ. Nie otwieraj pliku następnej fazy.**
- Czekaj na jawną zgodę człowieka przed przejściem do następnej fazy.

Automatyczne przejście do następnej fazy bez potwierdzenia jest **bezwzględnie zabronione**.

---

## Aktualizacja definicji wymagań

Po zakończeniu pracy w fazie:

1. Otwórz `docs/REQUIREMENT_DEFINITION.md`.
2. Zaktualizuj statusy wszystkich wymagań dotkniętych w tej fazie.
3. Użyj `[x]` tylko po weryfikacji, `[~]` jeśli częściowe, `[ ]` jeśli nieruszone.
4. Commituj zaktualizowany `REQUIREMENT_DEFINITION.md` jako część commitu zamykającego fazę.

---

## Stack

- .NET 10
- `ModelContextProtocol` v1.1.0, transport `WithStdioServerTransport`
- Format solucji: `.slnx`
- Struktura: `src/<Project>/` i `tests/<Project>.Tests/`
- `Directory.Build.targets` w korzeniu repozytorium
