# LLMFriend

LLMFriend verbindet persönliche Discord-Konten per OAuth2 mit einer ASP.NET-Core-API. Ein Headless-Dienst prüft Profil, Serverliste und Verbindungszustand. Der Windows-Client zeigt Konten, Änderungen und gezielte Freigaben. Das Repository steht unter der [MIT-Lizenz](LICENSE).

Die reguläre Discord-OAuth2-API erlaubt **keinen allgemeinen Zugriff auf private Nachrichten, Freundeslisten oder Präsenz**. LLMFriend verwendet nur `identify` und `guilds`; es gibt keine Self-Bot-Funktion. Siehe [Discord OAuth2](https://docs.discord.com/developers/topics/oauth2) und [Discords Self-Bot-Regeln](https://support.discord.com/hc/en-us/articles/115002192352-Automated-User-Accounts-Self-Bots).

## Projekte

| Projekt | Aufgabe |
| --- | --- |
| `LLMFriend.Core` | Connector-Vertrag, Berechtigungen und Änderungsvergleich |
| `LLMFriend.Infrastructure` | Discord-Connector, verschlüsselte Token, Dapper/MySQL, Nutzer und Konten |
| `LLMFriend.Api` | Authentifizierte HTTP-API und OAuth-Callback |
| `LLMFriend.Headless` | Monitoring-Worker und Verwaltungs-CLI |
| `LLMFriend.Wpf` | Deutscher Windows-Client |

## Lokaler Start

Erforderlich sind .NET 10, MySQL 8.4 (oder Docker Desktop) und eine eigene Anwendung im [Discord Developer Portal](https://discord.com/developers/applications). Im Portal die Redirect-URI `http://localhost:5000/api/accounts/discord/callback` für lokale Entwicklung eintragen. Für einen erreichbaren Server eine HTTPS-URI verwenden. Client-ID und Client-Secret gehören nie ins Repository.

1. `.env.example` als lokale Vorlage verwenden. Die Werte als **Prozess-Umgebungsvariablen** für API und Headless-App setzen; die Datei wird nicht automatisch geladen. `LLMFRIEND_ENCRYPTION_KEY` muss ein Base64-kodierter 32-Byte-Schlüssel sein. Er kann zum Beispiel mit PowerShell erzeugt werden: `[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))`. Sicher aufbewahren: Ohne ihn können gespeicherte OAuth-Tokens nicht entschlüsselt werden.
2. Für die lokale Datenbank `docker compose --env-file .env up -d mysql` ausführen. Docker Desktop muss gestartet sein. Alternativ eine eigene MySQL-8.4-Datenbank mit einem dedizierten Nutzer bereitstellen.
3. Schema anlegen: `dotnet run --project src/LLMFriend.Headless -- db migrate`.
4. Ersten Administrator anlegen: `dotnet run --project src/LLMFriend.Headless -- admin bootstrap <name>`. Das Startpasswort wird einmalig im Terminal angezeigt und beim ersten Login geändert.
5. In getrennten Terminals `dotnet run --project src/LLMFriend.Api` und `dotnet run --project src/LLMFriend.Headless -- worker` starten. Die API-Adresse kann über `ASPNETCORE_URLS=http://localhost:5000` gesetzt werden.
6. Den Windows-Client mit `dotnet run --project src/LLMFriend.Wpf` starten, API-Adresse und Zugangsdaten eingeben und „Discord verbinden“ wählen.

Weitere CLI-Befehle: `admin create-user <name>`, `admin reset-password <user-id>` und `monitor once <account-id>`. Nur eine Worker-Instanz je Datenbank betreiben. Der Worker prüft fällige Konten alle 30 Sekunden und plant nach Erfolg den nächsten Abgleich nach 15 Minuten. Rate-Limits verschieben den nächsten Versuch; der Verlauf bleibt erhalten, bis das Konto gelöscht wird.

## API und Rechte

`POST /api/auth/login` gibt ein sieben Tage gültiges, widerrufbares Bearer-Token zurück. `POST /api/auth/change-password` ist beim ersten Login Pflicht und widerruft bisherige Sitzungen. `POST /api/auth/logout` widerruft die aktuelle Sitzung. Administratoren legen über `/api/admin/users` Nutzer an oder setzen Passwörter zurück.

`/api/accounts` listet eigene und freigegebene Konten. `POST /api/accounts/discord/link` liefert eine Autorisierungs-URL und eine Vorgangs-ID; der Browser kehrt zum Callback der API zurück. Der Client fragt den Vorgangsstatus ab. Kontodetails, Verlauf (`?before=<event-id>`), Freigaben, manuelle Aktualisierung, Trennen und Löschen sind unter `/api/accounts/{id}` erreichbar. Besitzer können löschen und ihr Discord-Konto erneut autorisieren. Freigegebene Bearbeiter können lesen, aktualisieren, weitere Freigaben setzen und trennen. Geheimnisse werden nie über die API zurückgegeben.

## Betrieb unter Linux und Windows

API und Headless-App zielen auf plattformneutrales .NET 10; nur WPF benötigt Windows. Beide Serverprozesse brauchen dieselbe MySQL-Verbindung und denselben Verschlüsselungsschlüssel. Zuerst `db migrate` ausführen, dann API und genau eine Worker-Instanz starten. Unter Linux können API und Worker als `systemd`-Dienste laufen; unter Windows die API hinter IIS und der Worker als Windows-Dienst. Konfiguration und Geheimnisse als geschützte Umgebungsvariablen beziehungsweise Secret-Store bereitstellen. MySQL nicht öffentlich freigeben. Die API ausschließlich über HTTPS bereitstellen, etwa mit einem TLS-Reverse-Proxy; lokales HTTP ist nur für Entwicklung vorgesehen. Datenbank und Schlüssel getrennt sichern. Konkrete Schritte stehen in [docs/deployment.md](docs/deployment.md). Für einen produktiven Rollout fehlen derzeit ein konkreter Server und Discord-App-Zugangsdaten.

## Prüfungen

Die Headless-Kernsuite besteht aus `dotnet test tests/LLMFriend.Core.Tests` und `dotnet test tests/LLMFriend.Integration.Tests` (letzterer benötigt `LLMFRIEND_TEST_DB` auf einer **isolierten** MySQL-Testdatenbank). Der WPF-Build ist `dotnet build src/LLMFriend.Wpf`. UI-Prüfungen sind getrennt und kein Deployment-Gate. Vor potenziell langlaufenden oder Performance-Tests gelten die Freigaberegeln in [AGENTS.md](AGENTS.md).
