# Performance-Baseline

Gemessen am 14. August 2026 unter Windows x64 mit .NET 10 und Restic 0.19.1.
Die Zeiten sind Orientierungswerte und keine harten CI-Grenzwerte.

| Messung | Vorher | Nachher |
|---|---:|---:|
| Inkrementeller Release-Build ohne Restore | 8,6 s | 1,3 s |
| Eingebetteter Linux-Helfer | 39.256.893 Bytes | 13.494.162 Bytes |
| Suche nach der neuesten Datei | bis zu ein Restic-Prozess je Snapshot | genau ein Restic-Prozess |
| SSH-Sitzungen je VPS-Prüfung bei vorhandenem Helfer | 4 | 2 |

Die Helfergröße sank durch Trimming und JSON-Source-Generation um 65,6 %. Ein unveränderter
Build veröffentlicht den Helfer dank MSBuild-Inputs und -Outputs nicht erneut.

## Reproduzierbare Großdatenfälle

Der Test-Runner enthält zusätzlich zu den Integrationstests synthetische Lastfälle. Beim obigen
Messlauf benötigte die Filterung von 10.000 Snapshots 63 ms. Die zeilenweise Analyse von 100.000
Dateieinträgen einschließlich Kategorie-, Ordner- und Top-15-Aggregation benötigte 857 ms.

Die Werte werden bei jedem manuellen Testlauf als `METRIK` ausgegeben. Entscheidend für die
Regressionstests sind außerdem die Ergebnisanzahl, das Cache-Budget, genau eine Collection-Reset-
Benachrichtigung und genau ein Restic-Aufruf für die snapshotübergreifende Suche.

## NativeAOT-Entscheidung

Der getrimmte, selbstenthaltende Single-File-Helfer erfüllt bereits das Ziel einer Reduktion um
mindestens 30 % und besteht die Linux- und OpenSSH-End-to-End-Tests. NativeAOT wird erst auf einem
Linux-Buildsystem übernommen, wenn dieselben Tests bestehen, die Datei nochmals kleiner wird und
die gemittelte Kaltstartzeit aus mindestens fünf Läufen höchstens 10 % schlechter ist. Bis dahin
bleibt der getestete Trim-Build der veröffentlichte Standard.
