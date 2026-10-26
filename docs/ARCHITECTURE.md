# PixelPeeker – Architektur

Native Windows-Anwendung (.NET 10, WPF, x64). Die UI unterstützt neun Sprachen; siehe LANGUAGES.md. Das Desktop-Assembly und die EXE heißen PixelPeeker. Quellprojekt-/Namespace-Namen sowie die bisherigen Daten- und Sperrkennungen bleiben zur Kompatibilität erhalten. Externe Prozesse werden ausschließlich mit ArgumentList gestartet. Keine Shell zum Encoding.

## Projekte

- Core: Medienmodell, beobachtbare Einstellungen, Encoderfähigkeiten, Skalierung, Validierung, Argumentaufbau, Presets und Fortschrittsmodell.
- Infrastructure: Prozesslaufzeit, Gyan-Download und atomare Aktivierung, ffprobe, Vorschaubilder, Qualitätsanalyse, SQLite-Queue, Ordnerüberwachung, Sensoren.
- Desktop: WPF-Ressourcen, MVVM, Dialoge, Framevergleich. Dependency Injection verbindet die Dienste.
- Ein gemeinsamer Vergleich ersetzt die frühere ComparisonWindow-Ansicht und den Encode-Vergleich: EncodeComparisonService akzeptiert einen oder zwei Encodes und eine optionale räumlich angepasste Referenz. EncodeComparisonViewModel verwaltet Modi, Leertasten-Referenz und fünf Beispielpositionen. Deren zusätzlicher Pixelcache ist auf 192 MiB begrenzt; die Vorschauleiste behält darüber hinaus nur kleine Thumbnails und Frame-Metadaten. Alte Generierungen werden bei neuen Suchanfragen abgebrochen und dürfen den aktuellen Frame nicht ersetzen.
- Hauptarbeitsbereich: Vorschau mit Schnitt/Filtern → Encodierung mit Video/Audio/Untertiteln/Ausgabe → Medieninfo → Befehl → Log. Der Encode-Vergleich öffnet über die Queue-Schaltfläche direkt ein maximiertes eigenes Fenster; pro Hauptfenster und Vergleichsmodell wird eine vorhandene Instanz wiederverwendet. Das Vergleichsmodell bleibt im MainViewModel und erhält Auswahl, Position und Zoom beim Schließen. Codec-Optionen bleiben im MainWindow-Namensraum und verwenden dieselben MVVM-Bindings. Beim Verlassen der Vorschau oder Öffnen des Vergleichs wird die native Wiedergabe pausiert. Mediathek und Queue liegen außerhalb der Reiter.
- Tests: gezielte Unit- und Integrationstests. Verify: reproduzierbarer Test mit selbst erzeugten Medien und echter FFmpeg-Laufzeit.

## Sicherheits- und Datenmodell

Die Sprachkataloge sind als Ressource im Core eingebettet. Deutsche Quelltexte dienen als Schlüssel; englische und polnische Übersetzungen erhalten dieselben Formatargumente. WPF-Ressourcen und Anzeige-Bindings aktualisieren Beschriftungen, ohne gebundene Enum- oder Encoderwerte zu ersetzen. Die Auswahl liegt als de/en/pl in AppSettings.Language. Betriebssystem-Zahlenformate, FFmpeg-Syntax und persistierte Enum-Namen sind davon getrennt.

Jeder Job speichert einen eigenen JSON-Snapshot der Optionen und den konkreten FFmpeg/ffprobe-Pfad. Updates installieren parallel; bestehende Versionen bleiben erhalten. Ein aktiver Versionszeiger wird erst nach Hashprüfung, sicherer Extraktion und erfolgreichen Programmaufrufen ersetzt. Lokale Installationen werden nur gelesen.

Encodes schreiben mit -n in eine neue temporäre Datei im Zielordner. Erst nach erfolgreichem Prozessende und ffprobe-Kontrolle wird ohne Überschreiben auf den endgültigen Namen umbenannt. Abbruch beendet den Prozessbaum und wartet auf dessen Ende. Unvollständige Dateien werden als solche belassen. SQLite speichert Jobs und Analysen mit Schema-Versionierung. Nach Neustart werden ehemals laufende Jobs als unterbrochen markiert, nie als erfolgreich.

Encoderoptionen werden aus der Hilfe des konkreten Encoders gelesen. Die Videofamilien sind AVC, HEVC, VVC und AV1. NVENC wird zusätzlich durch einen kurzen realen Encode geprüft. Unbekannte Sensoren, Medienwerte und Dauern werden nicht erfunden.

## Umsetzung und Prüfung

Reihenfolge: Grundlage/Installation → Import/Analyse → Encoder/CommandBuilder → Audio/Untertitel → Filter/Presets → Queue → Framevergleich → Metriken → Automatisierung → portable Veröffentlichung und Dokumentation. Nach zusammenhängenden Schritten wird kompiliert; sicherheitsrelevante Transformationen erhalten Tests. Der abschließende Funktionsstand und noch nicht praktisch geprüfte Fälle stehen in STATUS.md.
