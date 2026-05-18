# AnimalDiary – Chronische Tiergesundheit & Medikamentenmanagement

 Eine mobile App für Besitzer chronisch kranker Haustiere. Gebaut mit .NET MAUI.


## Hintergrund

Wer ein Tier mit Diabetes, Niereninsuffizienz, Epilepsie oder einer anderen Dauererkrankung pflegt, weiß: Symptome, Medikamentengaben und Gewichtsverläufe lassen sich mit Notizzetteln und Erinnerungen kaum zuverlässig dokumentieren. AnimalDiary schafft dafür eine strukturierte, ruhige Grundlage und bereitet die gesammelten Daten direkt für den nächsten Tierarztbesuch auf.

## Aktueller Stand

- Plattformübergreifende Entwicklung mit .NET MAUI (Android, iOS, Windows, macOS)
- MVVM-basierte Architektur mit klarer Trennung von View, ViewModel und Model
- Lokale Datenspeicherung mit SQLite
- Erste DI-Integration über Microsoft Dependency Injection
- Kalenderbasierte Datenerfassung für Tageswerte (Gewicht, Stimmung)
- Dynamische UI mit Eingabefeldern, Zustandswechseln und Validierung
- Laufende UI-Optimierung für mobile Endgeräte

## Technischer Stack

- Sprache: C#
- UI: .NET MAUI (XAML + Code-Behind)
- Architektur: MVVM
- Datenbank: SQLite (lokal)
- Dependency Injection: Microsoft.Extensions.DependencyInjection
- Logging: Microsoft.Extensions.Logging.Debug

## Funktionen (aktuell implementiert)

- Erfassung von Haustieren
- Speicherung und Anzeige von Gewichts- und Stimmungsdaten
- Kalenderbasierte Auswahl von Einträgen
- Dynamische Eingabeformulare mit Zustandsteuerung
- Lokale Persistenz über SQLite

## Ziel des Projekts

- Alltagsnahe Dokumentation von Haustiergesundheit
- Übersichtlicher Verlauf wichtiger Gesundheitswerte
- Vorbereitung eines PDF-Exports für Tierarztbesuche

## Geplante Erweiterungen

- Medikamenten-Tracking
- Verbesserte Validierung und Fehlerhandling
- PDF-Export für Tierarztberichte
- Erweiterte Visualisierung von Verlaufsdaten
- UI/UX-Polish und Accessibility-Verbesserungen

## Entwicklungsfokus

Dieses Projekt dient als Lern- und Portfolioarbeit mit Fokus auf:

- saubere C#-Architektur
- MAUI Cross-Platform Entwicklung
- MVVM-Pattern und Datenflussverständnis
- Datenbanken (SQLite)
- iteratives Refactoring und UI-Design

## Hinweis

Das Projekt befindet sich aktiv in Entwicklung (WIP) und wird kontinuierlich erweitert und refaktoriert.