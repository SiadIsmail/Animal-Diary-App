# Felova

**Android-App für Menschen mit chronisch kranken Haustieren.** Medikation,
Symptome und Krankheitsverlauf an einem Ort, mit zuverlässigen Erinnerungen und
einem PDF-Bericht für den Tierarztbesuch.

[![build](https://github.com/SiadIsmail/Animal-Diary-App/actions/workflows/build.yml/badge.svg)](https://github.com/SiadIsmail/Animal-Diary-App/actions/workflows/build.yml)

**[Im Google Play Store](https://play.google.com/store/apps/details?id=com.felova.app)** · **[felova.app](https://felova.app)**

<!-- Screenshots: drei Bilder nach docs/screenshots/ legen und hier einbinden, z. B.
<img src="docs/screenshots/journal.png" width="240"> -->

Entwickelt von **Siad Ismail**, allein, von der ersten Idee bis zur
Veröffentlichung. Angefangen hat Felova als kleines Werkzeug für den chronisch
kranken Hund meiner Freundin.

## Auf einen Blick

| | |
|---|---|
| Plattform | Android (Google Play), gebaut mit .NET MAUI |
| Sprache | C# (.NET 10) und XAML |
| Daten | SQLite auf dem Gerät, optional PostgreSQL über Supabase |
| Tests | 370 Unit-Tests mit xUnit, automatisch bei jedem Push per GitHub Actions |
| Sprachen der App | Deutsch und Englisch, zur Laufzeit umschaltbar |
| Zeitraum | seit Februar 2026, über 250 Commits |

## Was die App kann

- Tierprofile mit Pflegeplänen für eine oder mehrere Erkrankungen, zum Beispiel
  Diabetes, Niereninsuffizienz oder Epilepsie
- Medikamentenpläne mit Erinnerungen, die auch einen Neustart des Handys
  überstehen, und ein Protokoll jeder Gabe
- Tagebuch für Gewicht, Stimmung, Blutzucker, Appetit, Wasser, Anfälle und
  eigene Messwerte, alles auf einer gemeinsamen Zeitachse
- PDF-Bericht für den Tierarzt: nur das, was der Halter eingetragen hat, ohne
  Bewertung
- Optional: Konto, Backup, Synchronisation zwischen Geräten und das Teilen
  eines Tiers mit weiteren Betreuern per Einladungscode

## Technische Schwerpunkte

**Local-First und Datenschutz.** Die App funktioniert komplett offline und ohne
Konto. SQLite auf dem Gerät ist die einzige Quelle der Wahrheit. Die Cloud ist
ausdrücklich optional: Solange niemand ein Konto anlegt und das Backup
einschaltet, verlässt kein Datensatz das Gerät.

**Synchronisation.** Im Hintergrund gleicht die App SQLite mit PostgreSQL ab:
erst holen, dann anwenden, dann hochladen. Datensätze bekommen ihre ID (GUID)
schon auf dem Gerät, gelöschte Einträge werden als Tombstones markiert statt
entfernt, und bei Konflikten gewinnt der letzte Schreibzugriff. Wer welche Daten
sehen darf, erzwingt die Datenbank selbst über Row Level Security.

**Zuverlässige Erinnerungen.** Wiederholungsregeln werden nie an das
Betriebssystem übergeben. Die App rechnet Medikamentenpläne in eine begrenzte
Zahl konkreter Termine um und plant sie nach jedem App-Start und jedem Neustart
des Geräts neu.

**PDF-Bericht ohne native Bibliotheken.** PDFsharp/MigraDoc erzeugt das
Dokument, SkiaSharp die Diagramme. Die Dokumentschicht hängt nicht von MAUI ab.

**Abo-Modell.** Google Play Billing über RevenueCat. Aufschreiben ist dauerhaft
kostenlos; bezahlt werden die Funktionen, für die sich die gesammelten Daten
lohnen: der gestaltete Tierarztbericht, das Backup und weitere Tiere. Ein
Webhook (Supabase Edge Function in TypeScript) hält den Abo-Status auf dem
Server aktuell, damit ein Betreuer weiß, ob der Besitzer des Tiers abonniert
hat.

**Qualität.** 370 Unit-Tests (xUnit) für die Kernlogik, etwa Medikationspläne,
Abo-Zugang, Datenimport und Tierarztbesuche. Die CI prüft bei jedem Push die
Tests, den Android-Build (Warnungen gelten als Fehler) und zwei Regeln, die der
Compiler nicht erzwingt: Jeder Text existiert auf Deutsch und Englisch, und
keine asynchrone Aufgabe wird unbeobachtet verworfen.

## Ein echter Fehler: verschwundene Tiere nach dem Kontowechsel

Beim Testen mit mehreren Betreuern ist auf einem Gerät Folgendes passiert:
angemeldet mit Konto X, abgemeldet, mit Konto Y angemeldet, zurück zu X. Die
Tiere von X waren danach auf dem Gerät verschwunden und kamen nicht wieder.

Die Daten auf dem Server waren nie in Gefahr. Die Ursache waren drei Fehler, die
nur zusammen auftraten. Die Abmeldung löschte nur die Sitzung, deshalb lief beim
Anmelden mit Y eine normale Synchronisation statt der Einrichtung. Die
Synchronisation holt nur Datensätze, die neuer sind als der zuletzt gesehene
Zeitstempel. Nach dem Abgleich mit Y war dieser Zeitstempel neuer als alle
Datensätze von X, also fragte das Gerät sie nie wieder ab.

Bei der Analyse kam ein vierter, schlimmerer Fall heraus: Unter bestimmten
Umständen hätten die Gesundheitsdaten von X still in das Konto von Y kopiert
werden können. Verhindert hatte das nur die zufällige Reihenfolge zweier
Abläufe.

Die Lösung war eine klare Regel statt eines Pflasters: Abmelden entfernt die
Daten des Kontos sofort vom Gerät, das Abschalten des Backups nicht. Beim
erneuten Anmelden kommt alles zurück. Die vollständige Analyse steht in
[docs/history/ACCOUNT_LIFECYCLE_PLAN.md](docs/history/ACCOUNT_LIFECYCLE_PLAN.md).


## Architektur

Views binden an ViewModels, ViewModels rufen Services auf, und nur Services
greifen auf SQLite und die Geräte-APIs zu. Die Services sind in Subsysteme mit
eigenen Schnittstellen aufgeteilt: **Data**, **Notifications**, **Journal**,
**Reports**, **Billing**, **Analytics** und **Cloud**. Ist die Cloud
abgeschaltet, wird eine Null-Implementierung registriert, und die App läuft
vollständig ohne Netzwerk.

| Bereich | Technik |
|---|---|
| Oberfläche | .NET MAUI 10 (XAML), MVVM, Microsoft Dependency Injection |
| Lokale Daten | SQLite (`sqlite-net-pcl`) |
| Erinnerungen | `Plugin.LocalNotification` |
| PDF | PDFsharp/MigraDoc, SkiaSharp |
| Cloud (optional) | Supabase: PostgreSQL, Auth, Row Level Security, Edge Function; eigener HTTP-Client ohne SDK |
| Abo | RevenueCat, Google Play Billing |
| Nutzungsanalyse | PostHog, anonym, eigener Client ohne SDK |
| Tests und CI | xUnit, GitHub Actions |

## Selbst bauen

Voraussetzung: .NET 10 SDK mit dem MAUI-Workload (`dotnet workload install maui`).

```bash
git clone https://github.com/SiadIsmail/Animal-Diary-App.git
cd Animal-Diary-App

# Unit-Tests (kein MAUI-Workload nötig)
dotnet test "Animal Diary App.Tests/Animal Diary App.Tests.csproj"

# Schnellster Kompiliertest (Windows)
dotnet build "Animal Diary App/Animal Diary App.csproj" -f net10.0-windows10.0.19041.0 -c Debug

# Android
dotnet build "Animal Diary App/Animal Diary App.csproj" -f net10.0-android -c Debug
```

Die App läuft vollständig ohne Backend. Für die Cloud-Funktionen braucht es ein
eigenes Supabase-Projekt, eingerichtet nach [supabase/README.md](supabase/README.md);
danach `Data/Services/Cloud/CloudConfig.cs` auf die Projekt-URL und den
Publishable Key setzen.

## Weitere Dokumente

- [docs/CONVENTIONS.md](docs/CONVENTIONS.md): Regeln, die die App korrekt halten (Englisch)
- [docs/history/](docs/history/): Designpläne und Audits (Englisch)
- [supabase/README.md](supabase/README.md): Datenbank, Migrationen und Server

## Kontakt

Siad Ismail · [siadkml2007@gmail.com](mailto:siadkml2007@gmail.com) ·
[LinkedIn](https://www.linkedin.com/in/siad-ismail-967a423aa/)
