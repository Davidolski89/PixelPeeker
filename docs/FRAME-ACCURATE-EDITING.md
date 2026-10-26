# Videobereiche framegenau entfernen

Ab Version 0.10.14 können mehrere Videobereiche manuell markiert und beim Encoding entfernt werden. Version 0.10.16 ergänzt durchgehende Quellwiedergabe mit schneller Suche. Die Originaldatei bleibt erhalten.

## Bedienung

1. Eine Datei importieren und unter **Vorschau** rechts neben dem Bild den Reiter **Schnitt** öffnen. Dort befindet sich auch **Filter**; Codec und Ausgabe werden unter **Encodierung** eingestellt. Die Vorschaufläche bleibt 16:9; das Video wird darin ohne Verzerrung eingepasst. Die verbleibende Breite steht den Optionen zur Verfügung.
2. Mit **Abspielen** die Eingabedatei mit Ton ansehen. Zeitregler und **−10 s / +10 s** springen schnell, zunächst ungefähr an Schlüsselbildern. **Pause** oder **Stopp** lädt anschließend das genaue Quellbild an der aktuellen Position; Stopp gibt zusätzlich den Decoder frei und behält die Position bei. Mit Zeitregler, Zeitfeld und Pfeiltasten am Zeitregler das **erste zu entfernende Bild** suchen. Während der genauen Suche erscheint ein Kreisel direkt über dem Vorschaubild. Erst nach dem Laden lässt sich **Start setzen** verwenden. Leertaste am Zeitregler: Abspielen/Pause; Pfeiltasten: während Wiedergabe zehn Sekunden, im Stand ein Bild.
3. Das **erste danach zu erhaltende Bild** suchen und **Ende setzen + entfernen** wählen. Dieses Endbild bleibt erhalten; der Bereich davor wird entfernt.
4. Für weitere Bereiche wiederholen. Für einen Bereich am Ende nach der Startmarke **Bis Dateiende entfernen** wählen. Das entfernt auch den letzten Frame.
5. Rote Abschnitte am Zeitregler und die Bereichsliste zeigen die Schnittentscheidung. Überlappende oder direkt angrenzende Bereiche werden zusammengeführt.
6. Einen Bereich auswählen und **Zum Start** bzw. **Zum Ende** oberhalb der Schnittliste zum Kontrollieren verwenden. **Bereich behalten** nimmt einen Schnitt zurück. **Alle Bereiche behalten** leert die Liste. Das Häkchen **Schnittbereiche beim Export entfernen** kann die ganze Liste vorübergehend deaktivieren.
7. Mit **Zur Queue** einen Exportauftrag anlegen und starten. Jeder Job erhält eine unabhängige Kopie der Schnittliste. Nachträgliche Änderungen gelten für neue Jobs. Namen bleiben gegen Überschreiben geschützt, auch bei parallelen Aufträgen derselben Quelle.

Die Vorschau bleibt auf der ursprünglichen Quellzeitachse und zeigt auch entfernte Bilder zur Kontrolle. Rechts im Reiter **Schnitt** steht, ob ein Frame entfernt wird oder wo die noch offene Startmarke liegt. Ein Einzelbild kann durch Start auf diesem Bild und Ende auf dem unmittelbar nächsten Bild entfernt werden.

Durchgehende Wiedergabe zeigt die Originaldatei. Sie überspringt keine Schnittbereiche und wendet keine Encodingfilter an. **Originalframe** bzw. **Filtervorschau** im rechten Reiter **Filter** oder ein Sprung zu einer Schnittgrenze pausieren die Wiedergabe und laden wieder ein genaues Bild. Die Wahl des Filtermodus bleibt anschließend für Einzelbilder wirksam. Erneutes Abspielen wechselt zur Originalquelle. Lautstärke und Stummschaltung betreffen nur die Wiedergabe, nicht den Encode.

Schnittlisten gehören zur jeweiligen Datei. Anwenden eines Encodingpresets erhält die Liste dieser Datei. Übertragen von Einstellungen auf andere Dateien, Speichern/Exportieren eines Presets und Ordnerüberwachung übertragen keine fremden Schnittmarken. Dauerhaft gespeichert werden Listen mit den Queue-Aufträgen; die Medienbibliothek wird nicht als Schnittprojekt gespeichert.

## Bild, Ton und Untertitel

- Video wird neu codiert. Die Grenzen folgen bestätigten Frame-PTS, auch bei variabler Bildrate, B-Frames und langen GOPs. Nach dem Ausschließen der markierten Bilder werden die Zeitstempel um die entfernte Dauer verschoben. Eine ausdrücklich gewählte FPS-Umrechnung erfolgt danach.
- Alle erhaltenen Audiospuren werden auf derselben Quellzeitachse sampleweise geschnitten und zusammengesetzt. Beim Hinzufügen eines Schnitts werden kopierte Tonspuren auf Encoding umgestellt; die Einstellung lässt sich im Audio-Reiter prüfen. Erneutes Kopieren bei aktivem Schnitt wird mit einem Hinweis abgewiesen. Verlustbehaftete Audioencoder können ihre übliche Encoderverzögerung bzw. Endauffüllung erzeugen; Containerinformationen zur Verzögerung bleiben wirksam.
- Textuntertitel werden an Schnittgrenzen geteilt und auf die neue Zeitachse verschoben. ASS-Styles bleiben erhalten. Eingebettete Schriftarten werden für Burn-in bereitgestellt bzw. mit kopierten ASS-Spuren im MKV erhalten. Leere Untertitelspuren bleiben zulässig. Vorbereitete Dateien gehören ausschließlich zum jeweiligen temporären Jobpfad und werden nach Erfolg, Fehler oder Abbruch entfernt.
- **Bilduntertitel** (PGS/DVD/DVB usw.) müssen beim Schnitt entfernt werden. Ihre zeitliche Bearbeitung ist noch nicht implementiert. Die Oberfläche meldet diese Einschränkung vor dem Jobstart.
- **Kapitel** werden bei aktivem Schnitt weggelassen, damit ihre alten Positionen nicht auf falsche Szenen zeigen.
- CUDA-Skalierung wird bei neuen Schnittbereichen ausgeschaltet. NVENC-Encoding bleibt möglich. Freie Filterketten sind beim Schnitt gesperrt, da sie die Zeitzuordnung verändern können. Die angebotenen normalen Bildfilter bleiben verfügbar.

Testausschnitte beziehen sich auf die Quellzeit und werden mit den verbleibenden Bereichen geschnitten. Ein vollständig entfernter Testausschnitt wird abgewiesen. Queue-Fortschritt, Größenabschätzung, Hochrechnung, Bildvergleich und Qualitätsanalyse berücksichtigen den Schnitt. Die verbleibende Dauer ist vor dem Export eine Schätzung anhand der Quelldauer; die fertige Ausgabe wird erneut mit ffprobe geprüft.

## Technische Umsetzung und Prüfung

FrameNavigationService liest begrenzte Fenster tatsächlicher Zeitstempel und Keyframes mit ffprobe. Die Vorschau extrahiert nach Quell-PTS und bricht überholte Anfragen ab. Der Ladekreisel bleibt unabhängig von Import und Encoding.

Ab 0.10.18 hält AccuratePreviewService für die Hauptvorschau einen FFmpeg-Decoder offen. Ein kurzer Vorlauf anhand tatsächlicher Frames erlaubt Rückwärtsschritte; bis zu acht folgende Bilder werden vorgeladen. Bilddaten gelangen unkomprimiert über eine Pipe in den RAM, ohne PNG-Dateien. Original-PTS werden vor den visuellen Filtern erfasst und jedem ausgegebenen Bild zugeordnet. Der Puffer umfasst höchstens 24 Bilder bzw. 96 MiB. Vorausladen stoppt bei gefülltem Puffer; nach 20 Sekunden ohne weiteren Bedarf wird der Decoder beendet. Quellenwechsel und Wiedergabestart leeren den Puffer, beim Beenden werden alle eigenen Decoder abgewartet. Große Sprünge oder geänderte Filter starten neu. Freie Filterketten verwenden wegen möglicher Zeit-/Frameänderungen weiterhin isolierte Extraktion. Die Zeitleiste wartet 80 ms auf die letzte Bewegung, Einzelbildschritte haben keine zusätzliche Wartezeit.

CutTimeline vereinigt Ausschlussbereiche und bildet Quellzeit auf Ausgabezeit ab. Video verwendet select nach ursprünglichen PTS und setpts; Audio verwendet atrim, asetpts, Ausrichtung/Stille bei fehlenden Samples und concat. Quellzeitstempel bleiben bis zu den Schnittfiltern durch -copyts erhalten. Eine passende Encoderzeitbasis und VFR-Ausgabe verhindern die Quantisierung auf eine geschätzte durchschnittliche FPS.

Echte FFmpeg-Tests vergleichen Anzahl, Reihenfolge und Pixelprüfsummen aller behaltenen Frames mit einer unabhängigen sequenziellen Decodierung. Für FLAC werden die decodierten Samples beider Audiospuren mit unabhängig ausgeschnittenen PCM-Arrays verglichen. Geprüft werden CFR/VFR, B-Frames, Startversatz, mehrere Bereiche, Dateiende, Testausschnitt, SVT-AV1, NVIDIA NVENC, MKV, MP4/AAC, SRT/ASS, leere Spuren und Text-Burn-in. Weitere Prüfungen erfassen unabhängige parallele Queue-Jobs, SQLite-Wiederherstellung und die geschnittene Referenz für Bildvergleich und VMAF/SSIM/PSNR. Die native Bedienprüfung kontrolliert Markierungen, Bereichsliste, Grenzsprünge, Zurücknehmen, Ladekreisel und Sprachwechsel in Deutsch, Englisch und Polnisch.

Nachweise: artifacts/tests-cuts und artifacts/portable-smoke-0.10.14. Wiedergabe, schnelle Suche und Rückkehr zu genauen Frames: artifacts/tests-playback und artifacts/portable-smoke-0.10.16.

Beschleunigte genaue Vorschau (0.10.18): Pixel-/Puffertests, AV1-Rohmesswerte und native Oberflächenprüfungen in artifacts/tests-buffered-preview; veröffentlichte EXE einschließlich Decoder-Ruheprüfung in artifacts/portable-smoke-0.10.18.

## Referenzen

- [FFmpeg: select und setpts](https://ffmpeg.org/ffmpeg-filters.html#select_002c-aselect)
- [FFmpeg: atrim und concat](https://ffmpeg.org/ffmpeg-filters.html#atrim)
- [FFmpeg: copyts, fps_mode und enc_time_base](https://ffmpeg.org/ffmpeg.html#Advanced-options)
- [ffprobe: Frameinformationen](https://ffmpeg.org/ffprobe.html#Main-options)
