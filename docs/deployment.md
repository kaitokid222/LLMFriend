# Betrieb auf Linux und Windows

Diese Anleitung bereitet den Betrieb vor. Sie veröffentlicht noch keinen produktiven Dienst. Benötigt werden .NET 10, MySQL 8.4, eine Discord-Developer-Anwendung und ein HTTPS-Endpunkt für die API. In Discord muss exakt die öffentliche Callback-URI `https://<api-host>/api/accounts/discord/callback` eingetragen werden.

## Gemeinsame Schritte

1. `dotnet publish src/LLMFriend.Api -c Release -o <api-verzeichnis>` und `dotnet publish src/LLMFriend.Headless -c Release -o <worker-verzeichnis>` ausführen. WPF wird getrennt auf Windows gebaut.
2. In MySQL eine eigene Datenbank und einen Nutzer mit Rechten nur auf diese Datenbank anlegen. Datenbankzugriff auf API und Worker begrenzen; für entfernte Verbindungen TLS verlangen.
3. Für beide Prozesse `LLMFRIEND_DB`, `LLMFRIEND_ENCRYPTION_KEY`, `LLMFRIEND_DISCORD_CLIENT_ID`, `LLMFRIEND_DISCORD_CLIENT_SECRET` und `LLMFRIEND_DISCORD_REDIRECT_URI` über einen Secret-Store oder geschützte Dienstkonfiguration bereitstellen. Der Schlüssel ist Base64 mit 32 zufälligen Bytes. Dieselbe Schlüsselversion muss auf beiden Prozessen verfügbar sein. Schlüssel und MySQL-Backup getrennt sichern.
4. Vor dem ersten Start und nach Schemaänderungen `<worker-verzeichnis>/LLMFriend.Headless db migrate` ausführen. Den ersten Administrator einmalig mit `admin bootstrap <name>` anlegen; das ausgegebene Startpasswort nur sicher an den Administrator weitergeben.
5. API und genau eine Worker-Instanz starten. `/health/live` zeigt den Prozessstatus, `/health/ready` prüft die Datenbank. Nach einem Rollout den OAuth-Callback und einen gezielten Abgleich mit einem eigens autorisierten Testkonto prüfen.

## Linux

API mit Kestrel nur an `127.0.0.1` binden und über einen TLS-Reverse-Proxy veröffentlichen. Für `systemd` getrennte Units mit `WorkingDirectory`, `ExecStart=/usr/bin/dotnet <verzeichnis>/LLMFriend.Api.dll` beziehungsweise `ExecStart=/usr/bin/dotnet <verzeichnis>/LLMFriend.Headless.dll worker` anlegen. Den Diensten einen eigenen Systemnutzer und eine geschützte `EnvironmentFile` außerhalb des Repositorys geben; die Datei nur diesem Nutzer lesbar machen. Der Proxy muss den öffentlichen HTTPS-Host korrekt weiterleiten. Nach dem Start die systemd-Journale und die beiden Health-Endpunkte prüfen.

## Windows

Die API als ASP.NET-Core-Anwendung hinter IIS mit HTTPS-Binding und ASP.NET-Core-Hosting-Bundle betreiben. Den Worker aus dem Publish-Verzeichnis als Windows-Dienst mit dem Argument `worker` einrichten, unter einem dedizierten Dienstkonto. Dienst-Umgebungsvariablen beziehungsweise ein geschützter Secret-Store müssen für beide Prozesse erreichbar sein. Der Worker nutzt die Windows-Service-Lifetime, wenn er als Dienst gestartet wird. Nach dem Start Windows-Ereignisprotokoll, IIS-Logs und Health-Endpunkte prüfen.

## Prüfung und Rückweg

Der Deploymentlauf setzt eine vollständig grüne Headless-Kernsuite voraus; UI-Tests werden separat behandelt. Vor einem Schema-Rollout Datenbank und Schlüssel sichern. Bei Fehlern den vorherigen API-/Worker-Build wiederherstellen und die MySQL-Migration prüfen; Schema-Migrationen werden nicht automatisch zurückgerollt. Tokens und Passwörter gehören weder in Logs noch in das öffentliche Repository.
