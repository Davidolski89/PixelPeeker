# Änderungen

## 0.10.44 – 27. Oktober 2026

- `scripts/setup-linux.sh` richtet PixelPeeker auf einem frischen Linux-Rechner ein. Es klont `main` nach `~/PixelPeeker` oder aktualisiert ein vorhandenes Checkout mit `git pull --ff-only`, installiert fehlende Pakete nach einer Rückfrage (`--yes`, `--no-install`) und veröffentlicht eine selbstständige Avalonia-Build unter `artifacts/portable-linux-x64` bzw. `artifacts/portable-linux-arm64`. Fedora 44 ist das Ziel; apt, pacman und zypper werden ebenfalls bedient. Das .NET SDK kommt aus `dotnet-sdk-10.0` oder, wenn das Paket fehlt, über Microsofts `dotnet-install.sh` nach `~/.dotnet`. `ffmpeg-free` bleibt optional. Liegt `vlc-libs` in den aktivierten Fedora-Repos, wird es installiert; RPM Fusion wird nicht automatisch aktiviert und nur mit `--enable-rpmfusion` eingeschaltet. `--test`, `--run` und `--desktop-entry` sind optional. Der Einzeiler ist `curl -fsSL https://raw.githubusercontent.com/Davidolski89/PixelPeeker/main/scripts/setup-linux.sh | bash`.

## 0.10.43 – 27. Oktober 2026

- `scripts/test-linux.sh` prüft auf dem eigenen Linux-Rechner, ob PixelPeeker startet. Das Skript meldet Distribution, Architektur und glibc (dieselbe Grenze 2.28 bzw. musl wie der statische FFmpeg-Download), fehlende Avalonia-Bibliotheken, die Sitzung, dotnet, `libvlc` und ein ffmpeg/ffprobe im PATH. Es baut standardmäßig selbstständig für linux-x64 bzw. linux-arm64, führt `dotnet test` für `FFmpegStudio.Linux.slnf` aus und startet die Anwendung mit `--smoke` sowie für einige Sekunden mit Fenster. `--appimage` und `--tarball` nehmen ein CI-Artefakt. `--download-ffmpeg` lädt den statischen BtbN-GPL-Build in ein temporäres Verzeichnis, prüft SHA-256 und encodiert eine Sekunde. Es gibt kein sudo; fehlende Fedora-Pakete werden nur als `sudo dnf install ...` ausgegeben.

## 0.10.42 – 26. Oktober 2026

- Die Avalonia-Oberfläche schließt die offenen Lücken zur Windows-Ansicht: Filmstreifen, Verfügbarkeitsspur und Fokusbild im Vergleich, Achsen und sortierte Tabelle der Qualitätsanalyse, Themenmuster und Dateinamensvorschau der gewählten Datei, Audio-Bearbeitung, Hardwareanzeige, Ergebnistabelle, Tooltips und die bisherigen fest eingebauten Beschriftungen über den bestehenden Sprachkatalog.
- Nach einem vollständig erfolgreichen Queue-Lauf zeigt Avalonia einen abbrechbaren Countdown. Energiesparen und Herunterfahren nutzen unter Linux `systemctl` und, falls das fehlt, `loginctl` (`suspend` bzw. `poweroff`). Fehlt beides, passiert nichts und der Status erklärt das. Windows behält `SetSuspendState` und `shutdown.exe`.
- `Publish.ps1 -Linux` bzw. `Publish-Linux.ps1` erzeugt selbstständige linux-x64- und linux-arm64-Ordner unter `artifacts/portable-linux-<arch>` und `artifacts/portable-linux-<arch>-<version>`, dazu `PixelPeeker-<version>-linux-<arch>.tar.gz` und ein AppImage mit `.desktop`-Datei und Symbol. LICENSE, THIRD-PARTY-NOTICES.md und licenses/ liegen bei. libvlc wird nicht mitgeliefert; die Wiedergabe lädt die Distributionsbibliothek. FFmpeg bleibt ein Laufzeit-Download oder ein Paar im PATH. Die CI lädt die Pakete als Workflow-Artefakte hoch und prüft das x64-Archiv und das x64-AppImage mit `--smoke`. Es entsteht kein GitHub Release.

## 0.10.41 – 26. Oktober 2026

- GitHub Actions (`.github/workflows/ci.yml`) prüft Pull Requests und Pushes auf `main`. Windows baut `FFmpegStudio.sln` in Release und führt die Testprojekte aus, einschließlich der STA-Oberflächentests. Ubuntu baut `FFmpegStudio.Linux.slnf`, führt deren Tests aus und startet die Avalonia-Oberfläche mit `--smoke`. FFmpeg- und GPU-Integrationsläufe bleiben aus, solange `FFMPEG_STUDIO_TEST_DATA` und `PIXELPEEKER_LIVE_FFMPEG` nicht gesetzt sind.
- Phase 1 der Avalonia-Migration: Prozesssteuerung, Hardwarewerte und die verwaltete FFmpeg-Installation liegen hinter Schnittstellen. `FFmpegStudio.Shared` (net10.0) enthält die gemeinsame Bibliotheks-, Medieninfo- und Queue-Logik.
- Die Avalonia-Oberfläche bindet an `EditorViewModel`: Encoder, Qualität, Preset, Container, Skalierung, Crop, Schnitte, Presets, Befehlsvorschau und Queue-Befehle. Dateidialoge und Rückfragen bleiben in der jeweiligen Oberfläche. Die WPF-Anwendung ruft dieselbe Encoder-, Schnitt- und Befehlsvorschau-Logik auf.
- Wiedergabe läuft über `IPlaybackSession`. Windows behält LibVLCSharp.WPF und das mitgelieferte LibVLC. Avalonia nutzt LibVLCSharp mit der Systembibliothek libvlc. LibVLCSharp.Avalonia entfällt, weil es Avalonia 11 verlangt.
- Linux x64 und arm64 laden einen statischen GPL-Build von BtbN/FFmpeg-Builds (angeheftetes Autobuild, SHA-256). John Van Sickle ist die Ersatzquelle mit MD5. Ein ffmpeg/ffprobe im PATH kann stattdessen gewählt werden. Die Windows-Quelle bleibt gyan.dev.
- Statische Linux-Builds werden nur bei glibc 2.28 oder neuer geladen. musl (zum Beispiel Alpine) und älteres glibc bekommen keinen Download, sondern einen Hinweis auf `apk add ffmpeg` sowie apt, dnf und pacman. Danach kann das System-FFmpeg gewählt werden.
- Die Avalonia-Oberfläche übernimmt Encode-Vergleich, Qualitätsanalyse, den Einstellungsdialog, Pause auf das exakte Quellbild, Vorschaubilder, Expertenoptionen, Untertitelexport und den Testencode-Hinweis. Dieselbe Planungs- und Einstellungslogik nutzt auch die Windows-Oberfläche. Der WPF-Vergleich behält Filmstreifen und die genaue Bildfläche; die Qualitätsgrafik unter Windows bleibt die ausführlichere Ansicht.
- Die Avalonia-Ausgabe kopiert `LICENSE`, `THIRD-PARTY-NOTICES.md` und `licenses/` neben die Anwendung, wie die Windows-Ausgabe.

## 0.10.40 – 26. Oktober 2026

- PixelPeeker steht unter der GNU General Public License v3.0 oder jeder späteren Version. Copyright 2026 David Nagel. Der vollständige Lizenztext liegt in LICENSE und wird mit der portablen Ausgabe verteilt.

## 0.10.39 – 24. Oktober 2026

- Die erweiterten Codec-Einstellungen nutzen den Platz gleichmäßiger: SVT-AV1 zeigt Parallelität und Optimierung sowie Filmkorn und Deblocking paarweise; freie Parameter erhalten die ganze Breite.
- CPU-Modus und zugehörige Threadanzahl bzw. CPU-Anteil stehen nebeneinander. Nur das aktive Eingabefeld wird angezeigt. NVIDIA-Einstellungen beginnen links; Encoderoptionen und Prüfung stehen gemeinsam darunter.
- Maximalbitrate, VBV-Puffer, B-Frames und Referenzframes erhalten eigene Beschriftungen mit eindeutigen Einheiten bzw. Automatikwerten. Formatfelder bleiben auch bei mehrzeiligen Übersetzungen ausgerichtet; allgemeines Tuning erscheint nur bei unterstützenden Encodern.

## 0.10.38 – 24. Oktober 2026

- Der Encodierungsbereich übernimmt den Entwurf „Alles Wichtige“: Videoencoder und Qualitätssteuerung stehen oben nebeneinander, Qualitäts- und Geschwindigkeitsregler direkt darunter.
- Format und Kompatibilität, Bildstruktur und Bitratenlimits sowie CPU-/GPU- und Encoderoptionen sind in drei aufklappbaren Gruppen gebündelt. GOP, Profil, Tuning, SVT-Parameter und Threadsteuerung bleiben vollständig erreichbar.
- Abhängig vom Modus erscheint entweder das Qualitätsziel oder die Zielbitrate. Der Geschwindigkeitswert bleibt rechts neben seiner Beschriftung; Encoderstandards sind weiterhin gekennzeichnet.

## 0.10.37 – 24. Oktober 2026

- Der aktuelle Geschwindigkeitswert steht rechts neben „Geschwindigkeits-Preset“ in derselben Zeile, wie beim Qualitätsziel.
- Der separate Slider-Schritt „Encoder-Standard“ entfällt. Stattdessen ist der von FFmpeg gemeldete Standardwert mit „(Standard)“ gekennzeichnet, bei x264 und x265 beispielsweise „medium — ausgewogen (Standard)“. Bereits gespeicherte leere Vorgaben bleiben kompatibel.

## 0.10.36 – 23. Oktober 2026

- Die Videoencoder-Auswahl zeigt kompakt Codec, Encodername und Software-/Hardware-Kennzeichnung in einer Zeile. Der separate Beschreibungsblock entfällt; technische Details bleiben im Tooltip verfügbar.
- Geschwindigkeitspresets werden mit einem Slider gewählt. Der aktuelle Name erscheint oberhalb des Reglers; numerische Presets, NVENC-Presets und dessen Legacy-Aliase bleiben unterscheidbar. „Encoder-Standard“ lässt den Parameter weg. Die Auswahl nutzt die Encoderhilfe und bewahrt gespeicherte Werte beim Codec- oder Sprachwechsel.

## 0.10.35 – 23. Oktober 2026

- Der Schnittbereich heißt allgemein „Video schneiden“ („Cut video“). Anleitung, Bereichszähler, Schnittmarken-Hinweise, Tooltips und Validierungstexte verwenden neutrale Schnittbegriffe in allen neun Sprachen. Die Schnittfunktion bleibt unverändert.

## 0.10.34 – 23. Oktober 2026

- „Testencode“ steht direkt neben Position und Testdauer. Die gemeinsame Zeile bricht bei Platzmangel um.
- Die Vorschaufläche hat ein festes Seitenverhältnis von 16:9 und passt sich der verfügbaren Höhe an. Die übrige Breite steht den Schnitt-/Filteroptionen zur Verfügung; das Quellbild behält sein eigenes Seitenverhältnis.
- „Zum Start“, „Zum Ende“ und die Aktionen zum Behalten von Bereichen stehen direkt unter den Schnittmarken-Schaltflächen, oberhalb der Schnittliste. Längere Bedienhinweise folgen unterhalb der Liste.

## 0.10.33 – 23. Oktober 2026

- Unter dem Vorschaubild entfallen die Status-/Schnitthinweise und die zusätzliche Leiste mit Einzelbild-, Original-/Filter- und Schnittmarken-Schaltflächen. „Testencode“ bleibt verfügbar; Schnittaktionen stehen rechts im Reiter „Schnitt“.
- Original- und Filtervorschau stehen zusammen im rechten Reiter „Filter“. Die Pfeiltasten am Zeitregler wechseln weiterhin einzelne Frames; der Vorschaustatus ist als Tooltip über dem Bild verfügbar.

## 0.10.32 – 23. Oktober 2026

- Parallelitätsgrenzen und Effizienzmodus stehen bei ausreichender Breite neben den Queue-Schaltflächen. Bei schmaleren Fenstern wechseln sie automatisch in die nächste Zeile, damit die Jobliste auf breiten Fenstern mehr Platz erhält. Beschriftung und Auswahlfeld bleiben jeweils zusammen.

## 0.10.31 – 23. Oktober 2026

- Beim Absenden eines Testencodes erscheint ein Hinweis mit dem tatsächlichen Status: wartend in der Queue oder laufend. Der Hinweis aktualisiert sich beim Start und Abschluss des Jobs; die Queue arbeitet währenddessen weiter.
- „Nicht mehr anzeigen“ unterdrückt künftige Testencode-Hinweise dauerhaft. Die Auswahl wird beim Schließen gespeichert, auch über das Fensterkreuz. Unter Einstellungen → Allgemein kann der Hinweis wieder eingeschaltet werden. Das Schließen bricht den Encode nicht ab.
- Dialog, Einstellung und Schaltflächenhilfe stehen in allen neun Sprachen bereit.

## 0.10.30 – 23. Oktober 2026

- Beim Wechsel zwischen Queue-Encodes derselben Quelldatei behält Peek die aktuelle Quellposition. Das gilt für die Encode-Auswahl im Vergleichsfenster und für eine neue Queue-Auswahl über „Peek“.
- Die Position bleibt auch dann erhalten, wenn der neue Encode dort einen Schnitt oder keinen Testausschnitt enthält; stattdessen erscheint der Hinweis auf fehlendes Material. Bei einer anderen Quelldatei beginnt der Vergleich weiterhin am Start des neuen Ausschnitts.

## 0.10.29 – 23. Oktober 2026

- Peek zeigt für Encode A und B eigene Verfügbarkeitsspuren auf derselben Quellzeitachse. Vollflächen markieren erhaltenes Material, Schraffuren entfernte Bereiche oder Positionen außerhalb des Testausschnitts. Eine Positionslinie folgt dem Zeitregler; Klicks auf die Spuren springen zur entsprechenden Quellposition. Tooltips nennen Bereichsgrenzen.
- Externe Dateien erhalten eine gepunktete Spur für unbekannte Schnittzuordnung; bekannte Dateigrenzen und Zeitversätze werden berücksichtigt. Bei zwei externen Dateien umfasst die Zeitachse beide Dateien. Freie Filterketten werden ebenfalls als unbekannte Zuordnung gekennzeichnet.
- Fehlende Bilder werden direkt in der Bildfläche erklärt. Die Darstellung funktioniert auch in der Einzelansicht und enthält Beschriftungen in allen neun Sprachen.

## 0.10.28 – 23. Oktober 2026

- Im Peek-Vergleich ersetzt „Vergleichsmodus“ die Checkboxen „Zwei Encodes vergleichen“ und „Referenz anzeigen“. Zur Wahl stehen Encode A + Referenz, Encode A + Encode B sowie beide Encodes + Referenz. A/B-Auswahl und Quellposition bleiben beim Wechsel erhalten.
- Referenzoptionen erscheinen nur in Modi mit Referenz. „Referenz an Encode A anpassen“ ist nur bei vorhandener Referenz und bekannten Queue-Einstellungen von A verfügbar. A/B ohne Referenz löst durch diese Option keine unnötigen Ladevorgänge mehr aus.
- Fehlt eine Referenzdatei, zeigt der gewählte Referenzmodus einen ausdrücklichen Hinweis. Modusnamen und Hilfetexte sind in allen neun Sprachen verfügbar.

## 0.10.27 – 23. Oktober 2026

- Im Peek-Einzelvergleich ändern lange Referenznamen die Bildgröße und Bildposition nicht mehr. Auswahlfeld und Titel bleiben einzeilig; die Kopf- und Frameinfo-Zeilen reservieren eine feste Höhe. Lange Angaben sind vollständig im Tooltip verfügbar.
- Referenzbeschreibungen und Statusmeldungen vergrößern die Fußzeile nicht mehr durch Zeilenumbrüche.

## 0.10.26 – 21. Oktober 2026

- „Zur Queue“ verwendet denselben themenabhängigen Akzentstil wie „Peek“ und „Queue starten“.

## 0.10.25 – 21. Oktober 2026

- „Queue starten“ und „Queue stoppen“ stehen direkt über den Jobs neben „Ausgewählte starten“ und den übrigen Jobaktionen. Die Schaltflächen umbrechen bei schmalen Fenstern.
- CPU-, RAM- und GPU-Messwerte stehen oben rechts gegenüber den Importaktionen. Die vollständigen Hardwareangaben sind zusätzlich als Tooltip verfügbar.

## 0.10.24 – 21. Oktober 2026

- Die Vergleichsschaltfläche heißt in allen neun Sprachen **Peek** und verwendet denselben Akzentstil wie **Queue starten**. Die Farbe folgt dem gewählten Farbthema.
- Der Hinweis nach dem Start eines Testencodes und die Bedienungsanleitung verweisen ebenfalls auf **Peek**.

## 0.10.23 – 21. Oktober 2026

- „Framevergleich“ und „Encodes vergleichen“ sind zu „Vergleichen“ zusammengeführt. Ein abgeschlossener Job öffnet Quelle gegen Encode; zwei markierte Jobs öffnen A/B mit optionaler Referenz. Beide Modi verwenden dasselbe maximierte Fenster. Die getrennte alte Vergleichsansicht entfällt.
- Über „Zwei Encodes vergleichen“ lässt sich der Modus im Fenster wechseln. Die Referenz kann unverändert bleiben oder die Geometrie und räumlichen Bildfilter von Encode A übernehmen. Framerateänderungen, freie Filterketten und eingebrannte Untertitel werden nicht auf die Referenz angewendet. Die gemeinsame Quellzeit berücksichtigt weiterhin Schnitte, Testausschnitte, VFR und Containerstarts.
- Die Leertaste zeigt während des Haltens die bereits geladene Referenz; Loslassen stellt die vorherige Ansicht ohne erneutes Dekodieren wieder her. Fünf Beispielpositionen mit Vorschauleiste, 100/200/400 % Pixelzoom, synchrones Verschieben und externe Dateien bleiben erhalten. Der zusätzliche Pixelcache der Beispielbilder ist auf 192 MiB begrenzt; größere Beispiele werden bei Auswahl erneut aus dem Bildcache geladen.
- Beschriftungen und Aktionshilfen sind in allen neun Sprachen verfügbar. Auswahl, Position und Zoom bleiben beim Wiederöffnen erhalten.

## 0.10.22 – 21. Oktober 2026

- „Encodes vergleichen“ öffnet den Vergleich direkt in einem maximierten eigenen Fenster. Der kleine Vergleichsreiter im Hauptfenster und die zusätzliche Schaltfläche „Großansicht“ entfallen. Eine laufende Quellwiedergabe wird beim Öffnen pausiert.
- Zwei markierte abgeschlossene Jobs derselben Quelle werden übernommen. Ohne passende Markierung öffnet sich das Fenster ebenfalls; Queue-Einträge oder Datei A/B lassen sich dort wählen. Erneutes Klicken aktiviert das vorhandene Fenster und stellt es bei Bedarf aus der Taskleiste wieder her. Auswahl, Quellposition, Bilder und Zoom bleiben beim Schließen und erneuten Öffnen erhalten.
- A/B-Einzelansicht, optionales Original, Einzelbildnavigation, 100/200/400 % Pixelzoom und gemeinsames Verschieben bleiben verfügbar. Die Aktionshilfe ist in allen neun Sprachen angepasst.

## 0.10.21 – 20. Oktober 2026

- Neuer Hauptreiter „Encodierung“ direkt nach der Vorschau. Video, Audio, Untertitel und Ausgabe erhalten den gesamten Arbeitsbereich; Presets stehen darüber. Die Videoansicht trennt Codec, Qualitäts-/Bitratensteuerung und Geschwindigkeit links von GOP, Threadsteuerung und erweiterten Codec-Optionen rechts. Ausgabeformat, Zielordner, Dateinamen und automatische Qualitätsanalyse stehen unter „Ausgabe“.
- Schnitt und Filter bleiben neben Wiedergabe, Zeitleiste und Einzelbildsteuerung in der Vorschau. Mediathek und Queue bleiben über alle Hauptreiter sichtbar. Beim Wechsel aus der Vorschau pausiert die Quellwiedergabe und kehrt zur genauen Einzelbildposition zurück. Der FFmpeg-Befehl steht im eigenen Hauptreiter; die doppelte schmale Befehlsansicht entfällt.
- Neue Beschriftungen in allen neun Sprachen. Einstellungen, Codec-Erinnerung, Schnittlisten und gespeicherte Queue-Aufträge behalten ihre Werte.

## 0.10.20 – 20. Oktober 2026

- Die Anwendung heißt jetzt **PixelPeeker**. Fenster, Kopfzeile, Meldungen in allen neun Sprachen, Benachrichtigungen und Windows-Dateiinformationen tragen den neuen Namen. Die portable Datei heißt **PixelPeeker.exe**. FFmpeg bleibt als verwendete Encoding-Engine erkennbar.
- Neues Logo und EXE-/Fenster-/Benachrichtigungssymbol: ein mintfarbenes und cyanblaues Pixelauge mit quadratischer Pupille auf einer dunkelblauen Kachel. Das ICO enthält zehn Größen von 16 bis 256 Pixeln. PNG und Generierungs-Prompt liegen unter Assets/PixelPeeker.png und Assets/PixelPeeker-prompt.txt; das bisherige Logo bleibt im Quellprojekt erhalten.
- Bestehender Datenordner, Daten-Umgebungsvariable und Instanzsperre bleiben kompatibel. Einstellungen, Presets, installierte FFmpeg-Versionen, Cache und Queue werden weiterverwendet; die alte und die umbenannte Anwendung können dieselben Daten nicht gleichzeitig öffnen. Neue Standard-Ausgaben verwenden den Unterordner PixelPeeker. Gespeicherte Zielpfade vorhandener Jobs bleiben erhalten.

## 0.10.19 – 20. Oktober 2026

- Sechs zusätzliche Oberflächensprachen: Französisch, Spanisch, Türkisch, Chinesisch (vereinfacht), Russisch und Japanisch. Zusammen mit Deutsch, Englisch und Polnisch stehen neun Sprachen unter „Einstellungen → Allgemein → Sprache“ bereit. Die Auswahl wird beim Speichern sofort übernommen und nach einem Neustart wiederhergestellt.
- Vollständige Kataloge mit 772 Oberflächen-/Meldungstexten und 84 Aktionshilfen pro Sprache. Formatargumente und Zahlendarstellung, gespeicherte Codecwerte, Dateinamensvorlagen und FFmpeg-Befehle bleiben unabhängig von der Oberflächensprache. FFmpeg-eigene Hilfe und Prozessprotokolle behalten ihre Originalsprache.
- Passende Windows-Schriften samt Ersatzschriften für chinesische und japanische Zeichen in Hauptfenster, Dialogen, Tooltips und großer Vergleichsansicht. Werkzeugleiste und Queue-Spalten passen sich längeren Beschriftungen an. Die automatische Tooltip-Zuordnung berücksichtigt echte Aktionsnamen auch bei gleichlautenden Übersetzungen anderer Oberflächentexte.

## 0.10.18 – 19. Oktober 2026

- Beschleunigte genaue Hauptvorschau: ein FFmpeg-Decoder bleibt für benachbarte Einzelbilder geöffnet. Vier vorangehende Bilder und bis zu acht folgende Bilder werden anhand tatsächlicher Quell-PTS gepuffert. Vor- und Rückwärtsschritte verwenden den Puffer oder dekodieren fortlaufend weiter, statt für jedes Bild neu am Schlüsselbild zu starten.
- Direkte unkomprimierte Bildübertragung im Speicher ersetzt PNG-Encoding, temporäre Bilddateien und erneutes PNG-Laden. Der Puffer ist auf 24 Bilder bzw. 96 MiB begrenzt; Vorausladen endet automatisch. Nach 20 Sekunden ohne weiteren Bedarf wird der Decoder geschlossen, vorhandene Bilder bleiben im Puffer. Quellenwechsel, Wiedergabestart und Beenden geben ihn sofort frei.
- Die kurze Wartezeit beim Ziehen der Zeitleiste sinkt von 200 auf 80 ms. Einzelbildtasten bleiben ohne künstliche Wartezeit. Große Sprünge und geänderte Filter starten bei Bedarf einen neuen Decoder; VFR, B-Frames, Quellstartversatz, HDR-Tonemapping und bestätigte Schnittmarken bleiben berücksichtigt. Freie Filterketten behalten die isolierte Einzelbildextraktion. Der separate Encode-Vergleich verwendet weiterhin seine voll aufgelösten Bilder.

## 0.10.17 – 19. Oktober 2026

- Neuer Reiter „Encode-Vergleich“ für zwei abgeschlossene Queue-Encodes oder zwei vorhandene Videodateien, etwa QP 40 gegen QP 50. Optional erscheint das Original als dritte Ansicht. Dateinamen, Encoder, QP/CRF/CQ bzw. Bitrate und Geschwindigkeitspreset kennzeichnen Queue-Ergebnisse. Zwei abgeschlossene Jobs derselben Quelle lassen sich auch direkt aus der Queue an den Vergleich übergeben.
- Gemeinsame Quellzeit mit automatischem Nachladen und echter Einzelbildnavigation. Testausschnitte, entfernte Bereiche, VFR und containerbedingte Zeitversätze werden anhand tatsächlicher Frames berücksichtigt. Nicht enthaltene Bilder bleiben leer und erhalten einen Hinweis. Externe Dateien unterstützen einen manuellen Quellstart in Sekunden. Freie Filterketten mit unbekannter Zeittransformation werden für die automatische Zuordnung gekennzeichnet.
- Nebeneinanderansicht, sofortiges A/B-Umschalten ohne Nachladen, optionales Original in der Einzelansicht, Einpassen sowie 100/200/400 % Pixelzoom und gemeinsames Verschieben. Die Großansicht öffnet denselben Vergleich in einem frei skalierbaren Fenster. Quellwiedergabe wird beim Öffnen des Vergleichs pausiert.
- Voll aufgelöste RGB/SDR-Vorschaubilder und sichtbare Frame-/Quellzeiten mit Abweichung in Millisekunden. Das Original bleibt ohne Encodingfilter; Größen- und Filterunterschiede bleiben erkennbar. Deutsch, Englisch und Polnisch einschließlich neun neuer Aktionshilfen.

## 0.10.16 – 19. Oktober 2026

- Die Vorschau spielt Eingabedateien mit Bild und Ton direkt im Fenster ab. Eine mitgelieferte VLC-Engine übernimmt MKV, AVC, HEVC und AV1, ohne dass VLC installiert sein muss. Abspielen, Pause, Stopp, Lautstärke, Stummschaltung und Zehn-Sekunden-Sprünge stehen unter dem Bild bereit.
- Während der Wiedergabe sucht der Zeitregler schnell an Schlüsselbildern. Überholte Suchanfragen werden zusammengefasst. Pause und Stopp wechseln an der aktuellen Position automatisch zurück zur genauen FFmpeg-Einzelbildvorschau. Stopp erhält die Position und gibt den Decoder frei. Schnittmarken und Einzelbildschritte werden erst nach einem bestätigten Quellbild wieder freigegeben. Auch Dateiende und Quellenwechsel beenden die laufende Wiedergabe sicher.
- Am fokussierten Zeitregler schaltet die Leertaste zwischen Abspielen und Pause um. Links/Rechts springen während der Wiedergabe zehn Sekunden und im Stand ein Quellbild. Neue Beschriftungen und ausführliche Tooltips sind auf Deutsch, Englisch und Polnisch verfügbar.
- Wiedergabe zeigt die Originalquelle einschließlich markierter Werbung; Encodingfilter und Schnittentscheidungen werden weiterhin mit Einzelbildvorschau bzw. Testencode kontrolliert. Native Bibliotheken und Lizenztexte werden in die portable EXE aufgenommen und beim Start extrahiert.

## 0.10.15 – 18. Oktober 2026

- Videoencoder merken ihre zuletzt verwendeten Parameter unabhängig voneinander: Qualitätsmodus und Wert, Bitraten/VBV, Geschwindigkeitspreset, Tune, Profil/Level, Pixelformat, GOP, B-Frames/Referenzen, Threadsteuerung, GPU/CUDA und Expertenoptionen einschließlich der erweiterten SVT-AV1-Werte. Beim Zurückwechseln wird dieser Stand wiederhergestellt; erstmals verwendete Encoder erhalten eigene passende Standards.
- Audioencoder behalten ebenfalls eigene Bitraten-, Kompressions-, Kanal-, Abtastraten- und Moduseinstellungen. Der automatische Standardwechsel im Audiomodell überschreibt keine zuvor gespeicherten Werte mehr. Spurnummern, Sprache, Titel, Flags und Spuraktionen bleiben an der jeweiligen Datei gebunden.
- Neue importierte Dateien verwenden den zuletzt gewählten verfügbaren Video-, Audio- und Untertitelencoder. Die Auswahl und Parameter werden automatisch gespeichert und nach einem Neustart erneut verwendet. Bestehende Bibliotheksdateien und Queue-Jobs behalten ihre individuellen Einstellungen; ausdrücklich gewählte Presets bleiben maßgeblich. Fehlende Encoder führen beim neuen Import zur bisherigen verfügbaren Alternative.
- Beim ersten Umstieg können fehlende Codec-Erinnerungen einmalig aus zeitlich geordneten vorhandenen Queue-Aufträgen aufgebaut werden. Schnitte, Filter, Testausschnitte und Ausgabepfade werden dabei nicht übertragen. Ältere Einstellungsdateien bleiben kompatibel.

## 0.10.14 – 18. Oktober 2026

- Neuer Reiter „Schnitt“ für mehrere manuell markierte Werbeblöcke. Start ist das erste zu entfernende Bild, Ende das erste erhaltene Bild nach der Werbung. Marken verwenden ausschließlich bestätigte Quellframes. Bereiche lassen sich kontrollieren, einzeln zurücknehmen, vollständig leeren oder vorübergehend deaktivieren. Ein Schnitt bis Dateiende ist ebenfalls möglich. Rote Timeline-Abschnitte und Hinweise unter dem Bild zeigen die Entscheidung; Überschneidungen werden zusammengeführt.
- Der Export entfernt Bilder anhand ihrer ursprünglichen PTS und kürzt die Zeitachse, einschließlich VFR und Startversatz. Alle erhaltenen Tonspuren werden passend geschnitten und neu codiert. Textuntertitel werden geteilt und verschoben; ASS-Styles und erforderliche Schriftarten bleiben berücksichtigt. Leere Textspuren sind zulässig. Bilduntertitel, Audio-Streamcopy und freie Filterketten werden bei aktivem Schnitt verständlich abgewiesen. Kapitel entfallen, CUDA-Skalierung wird bei neuen Schnittbereichen ausgeschaltet; NVENC bleibt verfügbar.
- Schnittlisten werden pro Quelldatei geführt und mit Queue-Aufträgen unabhängig gespeichert. Presets und das Übertragen von Encodingeinstellungen geben keine fremden Schnittmarken weiter. Parallele Aufträge behalten eigene Listen und geschützte Ausgabepfade. Fortschritt, Größenabschätzung, Testausschnitte, Hochrechnung, Framevergleich und Qualitätsanalyse berücksichtigen die gekürzte Zeitachse.
- Bedienung und sieben neue Aktionshilfen unterstützen Deutsch, Englisch und Polnisch. Eine Anleitung und die Grenzen der Schnittfunktion stehen in docs/FRAME-ACCURATE-EDITING.md.

## 0.10.13 – 18. Oktober 2026

- Während ein Vorschaubild gesucht und geladen wird, erscheint ein animierter Ladekreisel direkt auf dem abgedunkelten Bild. Die bestätigte Bildzeit und Fehlermeldungen bleiben am Zeitregler sichtbar. Der Kreisel verschwindet bei Abschluss, Abbruch und Dateiwechsel.
- Die native Bedienprüfung bestätigt den Ladezustand im Bild sowie die bisherige Einzelbildnavigation und Filtervorschau.

## 0.10.12 – 18. Oktober 2026

- Die Vorschau lädt beim Verschieben oder Anklicken des Zeitreglers und bei geänderter Zeitangabe nach 200 ms Bewegungspause automatisch nach. Überholte Anfragen werden abgebrochen; verspätete Bilder dürfen die aktuelle Auswahl nicht ersetzen. Vorschau-Ladezustand und Fehler erscheinen unabhängig von Import und Encoding direkt am Zeitregler.
- Zwei neue Pfeilschaltflächen sowie Pfeil links/rechts bei fokussiertem Zeitregler navigieren genau ein Quellbild zurück oder weiter. Tatsächliche Frame-Zeitstempel ersetzen eine Schätzung über durchschnittliche FPS. Die Positionsanzeige springt auf das bestätigte Bild; dessen Zeit wird mit sechs Nachkommastellen angezeigt. Erster und letzter Frame bleiben an den Grenzen erhalten.
- Frame-Auswahl und Bildextraktion berücksichtigen variable Bildrate, B-Frames, GOPs, versetzte Startzeiten und ungenaue Dauerangaben. FFmpeg decodiert von einem geeigneten früheren Einstieg und wählt das Bild nach dem Quell-PTS aus. Begrenzte Zeitstempel-Fenster und der vorhandene PNG-Cache reduzieren wiederholte Arbeit. Gleichzeitige Anfragen desselben Cache-Bilds werden toleriert.
- Original- und Filtervorschau bleiben beim Navigieren als gewählter Modus aktiv. Die Filtervorschau folgt der Quellzeitachse; FPS-Umrechnung bleibt dem Export vorbehalten. Vorschaubilder sind auf maximal 1280 Pixel Breite begrenzt. Dateiwechsel, leere Auswahl und Beenden verwerfen alte Vorschauanforderungen. Hinweise und Bedienung unterstützen Deutsch, Englisch und Polnisch.
- Der Ausbaupfad für spätere Schnittmarken, mehrere Werbeblöcke und synchronen Export ist unter docs/FRAME-ACCURATE-EDITING.md festgehalten.

## 0.10.11 – 18. Oktober 2026

- Neuer Einstellungsreiter „Dateinamen“ mit zehn auswählbaren Angaben: Datum, Uhrzeit, Ausgabeauflösung, Codec, Encoder, CRF/CQ/QP, Video-Zielbitrate, Geschwindigkeitspreset, gespeicherter Presetname und Bildrate. Der Originalname bleibt enthalten. Ein erzeugtes Schema und eine Vorschau mit der aktuellen Datei bzw. einem Beispiel machen die Auswahl sichtbar.
- Die globale Namensauswahl lässt sich einschalten und gilt für neue Queue-Aufträge einschließlich Testencodes und Ordnerüberwachung. Ohne diese Option bleiben individuelle Namensschemata aktiv. Bereits angelegte Aufträge behalten ihre reservierten Namen und Einstellungen. Im Video-Reiter zeigt ein neues Feld den effektiven Ausgabedateinamen; das individuelle Schema ist bei aktiver globaler Auswahl ausgegraut.
- Qualitätsparameter und Video-Zielbitrate werden nur im passenden Modus angefügt. Unbekannte Bildraten und leere Geschwindigkeitspresets entfallen. Datum und Uhrzeit werden beim Anlegen des Jobs gespeichert. Auflösung und FPS berücksichtigen die konfigurierten Ausgabewerte. Die neuen Platzhalter sind auch in individuellen Schemata nutzbar.
- Auswahl, Erklärungen und Vorschau unterstützen Deutsch, Englisch und Polnisch. Speichern übernimmt die Auswahl, Abbrechen verwirft sie. Originalnamen mit Unterstrichen oder Text wie {codec} bleiben erhalten. Vorhandene oder bereits reservierte Namen erhalten weiterhin _2, _3 usw.; der Überschreibschutz bleibt aktiv.

## 0.10.10 – 18. Oktober 2026

- Verständliche Tooltips für sämtliche Aktionsschaltflächen ergänzt, einschließlich Einstellungs-, FFmpeg-Verwaltungs-, Experten-, Bildvergleichs- und Analysedialogen. Die Texte erklären Wirkung und Voraussetzungen der jeweiligen Aktion auf Deutsch, Englisch und Polnisch. Auch ausgegraute Schaltflächen zeigen Hilfe.
- Unter „Einstellungen → Allgemein“ lassen sich Tooltips vollständig abschalten und ihre Wartezeit zwischen 0 und 30 Sekunden einstellen. Standard sind zwei Sekunden; jeder neue Hinweis wartet die eingestellte Zeit. Speichern übernimmt die Auswahl dauerhaft, Abbrechen verwirft Änderungen.
- Die globale Einstellung gilt auch für vorhandene Feldhinweise und neu geöffnete Dialoge. Ausschalten schließt einen bereits sichtbaren Hinweis; Tooltips folgen weiterhin dem gewählten Farbschema.

## 0.10.9 – 18. Oktober 2026

- Zehn vollständige Farbthemen ergänzt: Studio Mint, Mitternacht, Nordlicht, Amethyst, Glut, Wald, Sakura, Tageslicht, Sandstein und Gletscher. Sieben dunkle und drei helle Paletten besitzen eigene Hintergrund-, Flächen-, Text-, Auswahl-, Warn- und Akzentfarben mit dazu passender Schaltflächenbeschriftung.
- Neuer Einstellungsreiter „Darstellung“ mit zehn Farbkarten und sofortiger Vorschau in der gesamten Anwendung. „Speichern“ übernimmt das Thema dauerhaft; „Abbrechen“ oder Schließen stellt das vorherige Thema wieder her.
- Bestehende Hell-/Dunkel-Einstellungen werden beim Laden auf Tageslicht bzw. Studio Mint übertragen. Unbekannte Themenkennungen verwenden einen passenden Standard. Namen und Beschreibungen sind auf Deutsch, Englisch und Polnisch verfügbar.
- Fensterhintergründe, markierte Texte und Tabellenzellen sowie bereits geöffnete Diagramme und Dialoge folgen der Palette. Schaltflächenbeschriftungen übernehmen ihre jeweilige Vordergrundfarbe, damit helle und dunkle Akzentflächen lesbar bleiben. Video-Vorschauflächen bleiben neutral dunkel.

## 0.10.8 – 18. Oktober 2026

- Softwareencoder erhalten eine eigene CPU-Threadsteuerung: automatisch, feste Anzahl oder Anteil der logischen CPU-Kerne pro Job. x264/x264rgb, OpenH264, AOM-AV1, rav1e und VVenC verwenden die Video-Threadoption; x265 erhält einen Worker-Pool über pools. Für AOM wird bei einer Threadvorgabe row-mt aktiviert, sofern verfügbar und nicht ausdrücklich überschrieben. Konflikte mit entsprechenden Expertenparametern werden gemeldet.
- SVT-AV1 bietet seine native Parallelitätsstufe lp (0/leer = CPU-abhängige Automatik, 1–6 = Parallelitätsstufen) im eigenen erweiterten Bereich. Die Oberfläche erklärt, dass lp keine feste Threadanzahl darstellt. Werte, Vorschau, Presets und Queue-Aufträge bleiben synchron.
- Neue Encodes starten standardmäßig mit Windows-Effizienzmodus: EcoQoS und niedriger Prozesspriorität. Der tatsächliche Windows-Zustand wird nach dem Setzen abgefragt und in Jobdetails und Protokoll gespeichert. Bei fehlender Unterstützung wird der verfügbare Zustand angezeigt. Der Modus ist direkt an der Queue und in den Einstellungen abschaltbar und gilt für neue Starts, einschließlich Test- und Hardwareencodes.
- Parallelitätsgrenzen für alle Jobs (1–16) und NVIDIA-Jobs (1–8) stehen direkt an der Queue und werden gespeichert. Mehrfachauswahl mit Strg/Umschalt und „Auswahl starten“ startet ausgewählte Jobs unter Beachtung beider Grenzen. „Queue starten“ verarbeitet alle wartenden Jobs. Eine abgesenkte Grenze bricht laufende Encodes nicht ab.
- Alle neuen Beschriftungen und Hinweise sind auf Deutsch, Englisch und Polnisch verfügbar.

## 0.10.7 – 18. Oktober 2026

- GOP / Keyframe-Abstand direkt im Video-Reiter sichtbar, mit Einheit Frames und Hinweis auf 0 = Encoderstandard. Der Wert wird als -g übergeben.
- Für libsvtav1 gibt es eigene erweiterte Einstellungen für tune, film-grain und enable-dlf sowie eine synchronisierte Parameterliste für weitere Optionen. Das Beispiel tune=0:film-grain=0:enable-dlf=2 wird als ein einziges -svtav1-params-Argument übergeben; 0 bleibt ausdrücklich gesetzt, leere Felder verwenden den Encoderstandard.
- Die Werte bleiben in Presets und Queue-Aufträgen erhalten und sind mit dem vorhandenen Expertendialog synchronisiert. Ungültige bekannte Werte, doppelte Parameter und ein widersprüchliches GOP/keyint werden vor dem Start gemeldet. Beschriftungen und Hinweise sind auf Deutsch, Englisch und Polnisch verfügbar.

## 0.10.6 – 17. Oktober 2026

- Fehlende Geschwindigkeitspresets für libsvtav1 ergänzt. Numerische Presetbereiche aus der tatsächlichen FFmpeg-Encoderhilfe werden jetzt als Auswahlliste angeboten; der installierte Build bietet 0–13. Automatische/interne negative Werte werden nicht als normale Geschwindigkeitspresets aufgelistet.
- Dieselbe Erkennung ergänzt numerische Geschwindigkeitslisten für AOM-AV1 und rav1e. Benannte Presets, etwa bei NVENC, bleiben unverändert. Gespeicherte Werte und freie Eingabe bleiben erhalten; an FFmpeg gehen weiterhin die ursprünglichen Zahlenwerte.
- Ein Hinweis bei SVT-AV1 erklärt: kleinere Nummern bedeuten langsamere, effizientere Kompression, größere Nummern höhere Geschwindigkeit. Der Hinweis ist auf Deutsch, Englisch und Polnisch verfügbar.

## 0.10.5 – 17. Oktober 2026

- Bei unveränderter Geometrie entfallen die bisher immer angehängten Filter scale und setsar=1. „Original“ mit gerader Bildgröße und quadratischen Pixeln erzeugt ohne weitere Filter kein -vf. Auch 100 Prozent oder ein Größenlimit ohne nötige Verkleinerung vermeiden die unnötige Skalierung.
- Die Entscheidung berücksichtigt automatische Quelldrehung, Zuschneiden und manuelle Drehung. Notwendige Größenänderungen, gerade Ausgabeabmessungen und die Normalisierung nicht quadratischer Pixel bleiben erhalten. Bei ungewöhnlichen, nicht rechtwinkligen Rotationsmatrizen bleibt die bisherige Normalisierung bestehen.
- Unveränderte CUDA-Frames können direkt an NVENC gehen; eine tatsächliche Größenänderung verwendet weiterhin scale_cuda. Die Fähigkeitsprüfung verlangt scale/setsar nur, wenn sie tatsächlich erzeugt werden.
- Qualitätsanalyse und Bildvergleich verarbeiten auch eine vollständig leere Referenzfilterkette korrekt. Vorschau und gestartete Aufrufe verwenden denselben korrigierten Befehlsaufbau.

## 0.10.4 – 17. Oktober 2026

- Neuer Reiter „FFmpeg-Befehl“ neben Video, Audio, Untertitel und Filter. Die schreibgeschützte Vorschau aktualisiert sich bei geänderten Encoder-, Audio-, Untertitel-, Filter- und Ausgabeeinstellungen; der Aufruf kann kopiert werden. Ungültige Einstellungen zeigen die Fehler und deaktivieren Kopieren. Ohne ausgewählte Datei bleibt kein alter Aufruf stehen.
- „Große Ansicht“ öffnet dieselbe Darstellung im vorhandenen mittleren Reiter „Befehl“, damit lange Aufrufe ausreichend Platz haben. Beide Ansichten verwenden dieselbe Auswahl zwischen aktuellen Einstellungen und dem ausgewählten Queue-Job.
- Gestartete Encode-Aufrufe werden nach erfolgreichem Prozessstart am Job gespeichert und nach einem Neustart wieder angezeigt. Vor dem Start wird die Vorschau ausdrücklich als solche gekennzeichnet; bei älteren Jobs verweist die Oberfläche auf das Jobprotokoll. Der tatsächlich verwendete temporäre Ausgabepfad ist im gespeicherten Aufruf enthalten.
- Die Vorschau verwendet dieselben Regeln für Ausgabeordner und Dateinamen wie die Queue. Alle neuen Beschriftungen sind auf Deutsch, Englisch und Polnisch verfügbar.

## 0.10.3 – 17. Oktober 2026

- CRF, CQ und CQP/QP verwenden einen Slider statt des Zahlenfelds. Der aktuelle Wert sowie die untere und obere Grenze sind sichtbar; ein kurzer Hinweis erklärt die Richtung der Skala, bei NVENC die Bedeutung von CQ 0 und bei SVT-AV1 die Verwendung des Encoder-Standards bei 0.
- Grenzen und Schrittweite richten sich nach Encoder, Qualitätsmodus und der tatsächlichen FFmpeg-Encoderhilfe. CRF/CQ mit Fließkommawerten erlauben halbe Schritte, ganzzahlige Qualitätsparameter ganze Schritte. Beispielsweise zeigt AV1 NVENC für CQ 0–63 und für QP 1–255. QP 0 fordert Lossless an und wurde von der Test-GPU abgewiesen; der Slider schließt diesen Wert aus.
- Sliderbedienung funktioniert per Maus und Tastatur. Bei Zielbitrate/CBR bleibt er ausgegraut. Beim Laden von Presets oder Wechsel der Quelle werden gespeicherte Qualitätswerte nicht durch eine kurzzeitig andere Skala überschrieben; ungültige gespeicherte Werte werden mit einer Meldung abgewiesen.
- Alle Hinweise sind auf Deutsch, Englisch und Polnisch verfügbar. Neue Prüfungen erfassen Grenzen, Schrittweiten, tatsächliche FFmpeg-Argumente und Medienwechsel zwischen unterschiedlich großen Skalen.

## 0.10.2 – 17. Oktober 2026

- Videoencoder-Auswahl auf Softwareencoder und NVIDIA NVENC, Intel Quick Sync sowie AMD AMF begrenzt. VA-API, Vulkan, Direct3D 12, Media Foundation und andere Plattform-Backends werden nicht mehr angeboten oder mit Encoderhilfe geladen. Beim derzeitigen FFmpeg-Build bleiben 16 statt 28 Videoencoder übrig.
- Alte Presets und Queue-Jobs mit entfernten Encodern werden vor dem Start mit einer verständlichen Meldung abgewiesen. Im Editor kann ein anderer Encoder gewählt werden. Die Meldung ist auf Deutsch, Englisch und Polnisch verfügbar.
- Regressionstests prüfen die native Auswahl, die Wiederherstellung gültiger Einstellungen und das Abweisen alter Encoderwerte auch ohne Fähigkeitenliste. Audio, Untertitel und die vorhandenen FFmpeg-Decodierfunktionen bleiben erhalten.

## 0.10.1 – 17. Oktober 2026

- Aussagekräftige Geschwindigkeitsbezeichnungen direkt im Presetfeld und der Auswahlliste. Bei NVENC werden slow als P7 mit zwei Durchläufen in voller Auflösung sowie medium/fast als P4/P1 mit einem Durchlauf gekennzeichnet. Die gespeicherten Presetwerte und freie Eingabe bleiben erhalten.
- Die Qualitätsmodi heißen encoderabhängig Qualitätsziel (CRF), Qualitätsziel (CQ) oder Fester Quantisierer (CQP/QP). Kurze Erläuterungen zeigen den Unterschied; bei VVenC/rav1e wird auf den identischen QP-Parameter hingewiesen.
- Bei Qualitätsziel oder QP ist das Zielbitratefeld deaktiviert und ausgegraut; bei Zielbitrate/CBR das Qualitätsfeld. Werte bleiben beim Umschalten erhalten. Unbenutzte Qualitätswerte werden nicht validiert. Optionale Maximalbitrate und VBV-Puffer bleiben separate Begrenzungen.
- Alle neuen Texte sind auf Deutsch, Englisch und Polnisch verfügbar. Native WPF-Tests prüfen die tatsächliche Auswahl, unveränderte FFmpeg-Argumente und die aktiven Eingabefelder.

## 0.10.0 – 17. Oktober 2026

- Sprachwahl Deutsch, English und Polski unter Einstellungen → Allgemein → Sprache. Speichern aktualisiert die Oberfläche sofort; die Auswahl bleibt beim nächsten Start erhalten. Abbrechen verwirft die Änderung.
- Übersetzte Hauptansicht, Einstellungen, FFmpeg-Verwaltung, Vergleichs- und Analysedialoge, Auswahllisten, Validierung und Statusmeldungen. Encoderhilfe und FFmpeg-Protokolle bleiben im Original.
- Vorhandene Einstellungen verwenden weiterhin Deutsch. Unbekannte Sprachcodes fallen auf Deutsch zurück. Gespeicherte Enum-Werte, Presetnamen, FFmpeg-Argumente und Windows-Zahlenformate bleiben stabil.
- Neue Tests prüfen die Übersetzungsvorlagen, gespeicherte Sprachen und den tatsächlichen WPF-Sprachwechsel einschließlich unveränderter Encoder-Einstellungen und Befehle.

## 0.9.3 – 17. Oktober 2026

- Eigenes mint-/cyanfarbenes Studio-Icon für EXE, Explorer, Fenster und Taskleiste. Die ICO-Datei enthält zehn Auflösungen von 16 bis 256 Pixeln mit transparenten Ecken.
- Das neue Icon erscheint auch im Kopfbereich der Anwendung. PNG-Original, ImageGen-Prompt und das Skript zur ICO-Erstellung liegen im Projekt.

## 0.9.2 – 17. Oktober 2026

- Qualitätsanalyse: Direkt am Job erscheinen Vorbereitung, Wartephase und der aktuelle VMAF-/SSIM-/PSNR-Durchlauf. Echte FFmpeg-Fortschrittswerte zeigen Prozent, verarbeitete Videodauer, Geschwindigkeit und geschätzte Restzeit je Durchlauf; unbekannter Fortschritt bleibt eine Aktivitätsanzeige.
- Ein eigener Button bricht laufende oder wartende Analysen ab. Abschluss, Abbruch und Fehler werden sichtbar angezeigt. Automatische Analysen verwenden dieselbe Anzeige.
- Analyse und Messwertauswertung laufen außerhalb des GUI-Threads. Die Analyse sperrt Import, Vorschau und andere Operationen nicht mehr global. Beim Beenden wartet die Anwendung auf den Analyseabbruch.
- Tests prüfen echte Fortschrittsmeldungen aller drei Metriken, Wartephase, Abbruch, Neustart, gespeicherte Ergebnisse und die nativen WPF-Anzeigen.

## 0.9.1 – 17. Oktober 2026

- Codecwechsel: Vorübergehend leere WPF-Auswahlen überschreiben keine gültigen Encoder- oder Pixelformatwerte mehr. Die abhängigen Listen werden ohne vollständiges Leeren aktualisiert; Geschwindigkeitspreset, Tuning und Profil bleiben korrekt gebunden.
- Leere optionale Encoderwerte führen in Validierung und Befehlsvorschau nicht mehr zu einer NullReferenceException.
- Extrem große Skalierungswerte werden in der Quelle-Ziel-Anzeige als Fehler dargestellt.
- Hintergrunddienste und die Anwendung können ihren Beendigungspfad mehrfach aufrufen, ohne bereits freigegebene Ressourcen erneut zu verwenden.
- Neue native WPF-Regressionstests prüfen die Auswahlfelder, Medien- und Presetwechsel, Audioqualität, erneute Fähigkeitserkennung, Skalierung und das Beenden der Anwendung.
