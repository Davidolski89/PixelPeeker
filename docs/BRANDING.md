# PixelPeeker

Ab 0.10.20 heißt die Anwendung PixelPeeker. Der Name passt zur Einzelbildsuche, zum Prüfen von Schnittgrenzen und zum Vergleich verschiedener Encodes. Der Start erfolgt über **PixelPeeker.exe**. Vorhandene Verknüpfungen können auf diese neue Datei umgestellt werden.

## Logo

Das neue Symbol ist ein geometrisches Auge mit quadratischer Pixel-Pupille und drei abgesetzten Pixeln. Mint und Cyan auf einer dunkelblauen Kachel passen zum Standardthema und bleiben auch auf hellen Oberflächen erkennbar. Das Symbol wird im Hauptfenster, als Windows-EXE-/Fenstersymbol und bei Abschlussbenachrichtigungen verwendet.

- Rastervorlage: `src/FFmpegStudio.Desktop/Assets/PixelPeeker.png`.
- Windows-Icon: `src/FFmpegStudio.Desktop/Assets/PixelPeeker.ico`, mit 16, 20, 24, 32, 40, 48, 64, 96, 128 und 256 Pixeln.
- Vollständiger Design-Prompt: `src/FFmpegStudio.Desktop/Assets/PixelPeeker-prompt.txt`.
- Erzeugt mit dem eingebauten ImageGen-Werkzeug. Die PNG-Vorlage wurde unverändert ins Projekt übernommen. Das ICO lässt sich mit `scripts/Create-AppIcon.ps1` aus der PNG-Vorlage erneut erstellen.

Die bisherigen Bilddateien bleiben im Quellprojekt als frühere Variante erhalten. Die neue Anwendung bindet die PixelPeeker-Dateien ein.

## Bestehende Daten

Die Umbenennung verschiebt oder löscht keine Benutzerdaten. Der Datenordner bleibt `%LOCALAPPDATA%\FFmpegStudio`, ebenso die optionale Umgebungsvariable `FFMPEG_STUDIO_DATA`. Einstellungen, Presets, FFmpeg-Installationen, Cache, Messwerte und SQLite-Queue bleiben dadurch verfügbar. Die gemeinsame Instanzsperre verhindert gleichzeitigen Zugriff durch die vorherige und die umbenannte Anwendung.

Neue Dateien erhalten als Standard-Ausgabeverzeichnis den Unterordner **PixelPeeker** neben der Quelle. Vorhandene Queue-Aufträge behalten ihre gespeicherten Zielpfade. Der Projektordner, die Solution und die internen Namespaces bleiben unverändert; das Desktop-Assembly, die EXE und die Windows-Produktinformationen verwenden PixelPeeker.
