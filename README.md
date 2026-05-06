# Animal Diary App

Eine Work-in-Progress Mobile-App mit .NET MAUI zur strukturierten Erfassung von Haustier- und Gesundheitsdaten.

## Kurzprofil

Ziel der App ist es, wichtige Daten rund ums Haustier alltagstauglich zu dokumentieren, z. B. Gewicht, Stimmung, Medikamentengaben und weitere Gesundheitsnotizen. Langfristig soll ein exportierbarer Bericht (PDF) entstehen, der direkt bei Tierarztbesuchen genutzt werden kann.


## Projektstaerken im aktuellen Stand

- Plattformuebergreifende Entwicklung: Ein Code-Stand fuer Android, iOS, macOS (Mac Catalyst) und Windows.
- Solide Architektur-Basis: Trennung von Views, ViewModel und Modellen.
- Nachvollziehbarer Datenfluss
- Sauberer Einstieg in Dependency Injection mit dem MAUI Service Container.
- Lokale Datenspeicherung mit SQLite.
- Aktive Weiterentwicklung mit klarem WIP-Status und ausbaubarer Roadmap.

## Produktvision

- Dokumentation von Gesundheitsdaten wie Gewicht, Stimmung und Medikamente.
- Verlauf ueber die Zeit zur besseren Einschaetzung von Veraenderungen.
- Export als strukturierter PDF-Bericht zur Weitergabe an Tieraerztinnen und Tieraerzte.

## Technischer Stack

- Sprache: C#
- UI/Framework: .NET MAUI (XAML + Code-Behind)
- Muster: MVVM
- Dependency Injection: `Microsoft.Extensions.DependencyInjection`
- Logging (Debug): `Microsoft.Extensions.Logging.Debug`
- Datenbank: SQLite (lokale Persistenz)

## Setup und Start

Voraussetzungen:

- .NET SDK mit MAUI-Workload (passend zu .NET 10 Target Frameworks im Projekt)
- Visual Studio 2022 (oder neuer) mit MAUI-Unterstuetzung

Projekt starten (Beispiel):

```bash
dotnet restore
dotnet build "Animal Diary App/Animal Diary App.csproj"
```

Hinweis: Das Projekt ist auf mehrere Zielplattformen konfiguriert. Die verfuegbaren Targets haengen vom Host-Betriebssystem und den installierten Workloads ab.

## Geplante Erweiterungen

- Tracking von Gewicht, Stimmung und Medikamentengaben
- Bessere Eingabevalidierung und Fehlermeldungen
- PDF-Export fuer Tierarzttermine
- UI-Polish und Accessibility-Verbesserungen

## Entwicklungsfokus

Dieses Projekt ist bewusst als Lern- und Portfolio-Projekt angelegt, mit Fokus auf:

- saubere C#-Grundlagen
- SQL Grundlagen und Servers
- MAUI App-Struktur und Navigation
- zustandsbasiertes Denken im UI-Kontext
- iteratives, nachvollziehbares Weiterentwickeln

## Kontakt

Wenn Sie mehr ueber meinen Entwicklungsansatz, Lernfortschritt oder die geplanten naechsten Schritte erfahren moechtest, freue ich mich ueber den Austausch.