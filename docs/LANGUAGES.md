# Oberflächensprache

Unter **Einstellungen → Allgemein → Sprache** eine Sprache wählen und **Speichern** drücken. Die Oberfläche wechselt sofort; ein Neustart ist nicht erforderlich. Die Auswahl bleibt beim nächsten Start erhalten. **Abbrechen** verwirft eine noch nicht gespeicherte Änderung.

| Sprache | Auswahlname | Kennung |
|---|---|---|
| Deutsch | Deutsch | de |
| Englisch | English | en |
| Polnisch | Polski | pl |
| Französisch | Français | fr |
| Spanisch | Español | es |
| Türkisch | Türkçe | tr |
| Chinesisch, vereinfacht | 简体中文 | zh |
| Russisch | Русский | ru |
| Japanisch | 日本語 | ja |

Alle neun Sprachen umfassen die Oberfläche, Dialoge, Auswahltexte, Anwendungsmeldungen und die 84 Schaltflächen-Tooltips. Die 772 Meldungs-/Oberflächentexte und die Tooltips sind lokal eingebettet; die Anwendung benötigt für Sprachwechsel keine Internetverbindung. Standard ist Deutsch. Regionale Kennungen wie fr-CA, es-MX, tr-TR, zh-Hans, ru-RU oder ja-JP verwenden den jeweiligen verfügbaren Katalog; unbekannte Kennungen fallen auf Deutsch zurück. Für Chinesisch wird der vereinfachte Katalog verwendet.

Für Chinesisch und Japanisch wählt die Oberfläche passende Windows-Schriftfamilien mit Ersatzschriften. Längere Beschriftungen können in Werkzeugleisten und Reitern umbrechen. Die Queue passt schmale Spaltenüberschriften an.

Zahleneingaben folgen weiterhin den Windows-Zahlenformaten. Ein Sprachwechsel übersetzt keine Dateipfade, Benutzernamen für Presets, vorhandenen Untertitel oder Mediendaten. Gespeicherte Encoderparameter und FFmpeg-Befehle bleiben unverändert. FFmpeg-eigene Encoderhilfe und Prozessausgaben behalten die vom jeweiligen Build gelieferten Texte.

Die Katalogprüfung kontrolliert Vollständigkeit, Formatargumente samt Formatangaben und Tooltip-Zuordnung in allen Sprachen. Native WPF-Prüfungen bedienen Auswahl, Speichern und Abbrechen, prüfen die Wiederherstellung, aktuelle Tooltips und unveränderte Befehle und erzeugen Ansichten für alle neun Sprachen. Nachweise: `artifacts/tests-languages-0.10.19`.
