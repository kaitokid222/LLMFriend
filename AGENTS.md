# Projektregeln für Agenten

## Agentenlogs sind Pflicht

Für jeden zusammenhängenden Change einschließlich reiner Dokumentationsänderungen eine eigene Markdown-Datei in `agent logs/` anlegen und vor Abschluss vervollständigen. Ein Change darf mehrere Dateien enthalten; es braucht keinen Log pro Datei und keinen rekursiven Log für das Schreiben des Logs.

Dateiname: `YYYY-MM-DD_HH-mm-ss_aenderung-in-kebab-case.md`.

- Datum/Uhrzeit aus `Europe/Berlin` verwenden; im Inhalt vollständigen ISO-Zeitstempel mit UTC-Offset angeben.
- Bestehende Logs nicht überschreiben. Bei Namenskollision `-2`, `-3` usw. vor `.md` ergänzen.
- Agent/Modell nur soweit tatsächlich bekannt benennen; keine erfundene Identität.
- Die Änderungsdateien vollständig aufführen, einschließlich Doku, Regeln und Log selbst, jeweils mit ihrer Wirkung. Nur gelesene Fremdprojekte getrennt als Quellen benennen.
- Prüfungen mit konkretem Befehl oder Vorgehen und Ergebnis dokumentieren. Ausgelassene Tests begründen; „nicht ausgeführt“ ist nicht „grün“.
- Offene Probleme und verbleibende Voraussetzungen angeben. Laufende Angaben vor Abschluss durch tatsächliche Ergebnisse ersetzen.

Mindestinhalt:

```text
# Kurztitel des Changes
Zeitpunkt: <ISO-Zeit mit Offset; Europe/Berlin>
Agent: <Name, Modell falls bekannt>
Ausgangsstand: <Commit/Arbeitsbaum>
Auftrag: <Ziel>
Änderung und Begründung: <Was und warum>
Angefasste Dateien: <vollständige Liste mit Wirkung>
Prüfungen: <Befehl/Vorgehen, Ergebnis, ausgelassene Tests mit Grund>
Offen: <Probleme/Voraussetzungen oder keine>
```

## Tests mit Aussagekraft

- Profiling beginnt mit kleinen synthetischen Datensätzen und wenigen Wiederholungen (z. B. 10 bis 100 Einträge, ein Durchlauf). Erst anhand der gemessenen Laufzeit einen größeren Umfang begründen; keine großen Messmatrizen oder Dauerläufe als Einstieg.
- Vor jedem Performance-Test und vor potenziell langlaufenden Tests ausdrücklich beim Nutzer nachfragen. Dabei Zweck, Datensatz, Wiederholungen, erwartete Laufzeit und Abbruchgrenze nennen. Ein allgemeiner Implementierungsauftrag oder eine im Plan genannte Messung ersetzt diese konkrete Freigabe nicht. Bei unbekannter Laufzeit konservativ von einem potenziell langen Lauf ausgehen. Nicht ohne erneute Freigabe vergrößern oder wiederholen.
- Bei normalen Änderungen nur passende Prüfungen für den betroffenen Bereich ausführen. Den Umfang erst bei Fehlern, neuen Änderungen oder belegten Querschnittsrisiken erweitern.
- Neue Tests nur für fachliche Regeln, relevante Schnittstellenverträge oder konkrete Regressionen. Keine Getter-/Setter-Tests, Implementierungsduplikate oder Testmengen ohne Erkenntnisgewinn.
- Die dauerhafte Headless-Kernsuite klein halten: nur wichtige, stabile Regressionen und geschäftskritische Verträge aufnehmen. Eine gezielte Prüfung muss nicht automatisch Teil dieser Suite werden.
- Headless-Tests und UI-Tests separat auswählbar halten. Headless bedeutet ohne sichtbare UI/Benutzerinteraktion und ohne UI-Automation; Windows oder STA können trotzdem erforderlich sein.
- UI-Änderungen gezielt mit passenden UI-Prüfungen prüfen. UI-Tests beim Deploy überspringen; der Deploymentlauf verwendet die Headless-Kernsuite.
- Für reine Dokumentationsänderungen genügen Quellenabgleich, Linkprüfung und Diff-Prüfung. Keine Anwendung starten und keine Testinfrastruktur allein dafür einführen.

## Versionserhöhung und Deployment

1. Versionen nur nach ausdrücklichem Nutzerauftrag erhöhen; weder aus einem Bugfix noch aus „fertig“ einen Versionsauftrag ableiten.
2. Vor einer beauftragten Erhöhung die vollständige Headless-Kernsuite gegen den aktuellen Codezustand ausführen. Nur ein nachgewiesener vollständig grüner Lauf erfüllt die Voraussetzung; nach weiteren verhaltensrelevanten Änderungen erneut prüfen.
3. Fehlt die Suite, ist sie nicht ausführbar, enthält sie übersprungene Kernprüfungen oder schlägt sie fehl: Version unverändert lassen und die Erhöhung ausdrücklich ablehnen. Fehler, betroffene Bereiche und konkrete Bugfixvorschläge beziehungsweise fehlende Voraussetzungen nennen.
4. Keine Tests entfernen, überspringen oder Erwartungen abschwächen, um eine Freigabe zu erzwingen. Ein leerer Testlauf ist keine Freigabe.
5. UI-Tests sind kein Versionsgate. Sie bleiben für betroffene UI-Änderungen getrennte Prüfungen und werden beim Deploy ausgelassen.
6. Eine Versionserhöhung autorisiert nicht automatisch Veröffentlichung oder Deployment. Bei einem beauftragten Deployment Headless-Kernsuite prüfen; fehlende oder rote Prüfung nicht als erfolgreiche Freigabe behandeln.
