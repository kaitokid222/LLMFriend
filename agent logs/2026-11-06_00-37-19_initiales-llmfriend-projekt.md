# Initiales LLMFriend-Projekt

Zeitpunkt: 2026-11-06T00:37:19+01:00 (Europe/Berlin)

Agent: Codex (GPT-6)

Ausgangsstand: Leerer Ordner ohne Git-Repository und ohne Commit. Das öffentliche Repository `kaitokid222/LLMFriend` existierte nicht.

Auftrag: Öffentliches MIT-Projekt mit verbindlichen Agentenregeln, Discord-OAuth2-Monitoring, ASP.NET-Core-API, Headless-Worker/CLI, MySQL-Datenschicht und WPF-Client erstellen.

Änderung und Begründung: Git-Repository und GitHub-Remote eingerichtet; Schichten und sichere Kontenverwaltung implementiert, um persönliche Discord-Profile und Serverlisten regelkonform zu verwalten. Die Trennung von API, Worker, Connector und WPF erlaubt später weitere Dienste. Ein öffentlicher MIT-Lizenztext, Entwicklungsanleitung, Bereitstellungshinweise und CI machen das Projekt nutzbar und überprüfbar. Es wurde keine Version erhöht.

Angefasste Dateien (vollständige Liste mit Wirkung):

- `.env.example` – benennt benötigte Umgebungsvariablen ohne echte Geheimnisse.
- `.gitattributes` – vereinheitlicht Textdatei-Zeilenenden im Git-Repository.
- `.github/workflows/ci.yml` – führt Headless-Kernsuite mit MySQL und separaten WPF-Build aus.
- `.gitignore` – verhindert Commit von Builds, lokalen Geheimnissen und Testergebnissen.
- `AGENTS.md` – enthält die vorgegebenen Log-, Test-, Versions- und Deploymentregeln.
- `CLAUDE.md` – verweist Claude auf die verbindlichen Regeln.
- `LICENSE` – stellt das Projekt unter die MIT-Lizenz.
- `LLMFriend.sln` – verbindet alle Produktions- und Testprojekte.
- `README.md` – erklärt Umfang, Einrichtung, API, Rechte und Prüfungen.
- `compose.yaml` – stellt eine lokale MySQL-8.4-Datenbank bereit.
- `docker/mysql/001-test.sql` – legt bei frischer lokaler Datenbank eine isolierte Testdatenbank an.
- `docs/deployment.md` – dokumentiert Linux- und Windows-Betrieb sowie Sicherheitsvoraussetzungen.
- `src/LLMFriend.Api/ApiEndpoints.cs` – definiert Authentifizierung, Nutzer-, Konto-, Freigabe- und OAuth-Endpunkte.
- `src/LLMFriend.Api/LLMFriend.Api.csproj` – definiert das ASP.NET-Core-Projekt und Infrastrukturreferenz.
- `src/LLMFriend.Api/Program.cs` – konfiguriert Dienste, Sitzungsprüfung, Rate-Limit und Health-Endpunkte.
- `src/LLMFriend.Api/Properties/launchSettings.json` – setzt den lokalen API-Start auf Port 5000.
- `src/LLMFriend.Api/appsettings.Development.json` – enthält nicht-sensitive Entwicklungs-Loggingvorgaben.
- `src/LLMFriend.Api/appsettings.json` – enthält nicht-sensitive Basis-Loggingvorgaben.
- `src/LLMFriend.Core/Accounts.cs` – definiert Connector-Vertrag, Rechte, PKCE-Hilfen und Änderungsvergleich.
- `src/LLMFriend.Core/LLMFriend.Core.csproj` – definiert die plattformneutrale Kernbibliothek.
- `src/LLMFriend.Headless/LLMFriend.Headless.csproj` – konfiguriert Worker-, HTTP- und Dienst-Abhängigkeiten.
- `src/LLMFriend.Headless/Program.cs` – stellt Migration, Administrator-CLI, Einmalprüfung und Worker-Start bereit.
- `src/LLMFriend.Headless/Properties/launchSettings.json` – enthält nicht-sensitive lokale Worker-Starteinstellungen.
- `src/LLMFriend.Headless/Worker.cs` – prüft fällige Konten unabhängig und regelmäßig.
- `src/LLMFriend.Headless/appsettings.Development.json` – enthält nicht-sensitive Entwicklungs-Loggingvorgaben.
- `src/LLMFriend.Headless/appsettings.json` – enthält nicht-sensitive Basis-Loggingvorgaben.
- `src/LLMFriend.Infrastructure/AccountService.cs` – speichert Konten, OAuth-Vorgänge, Freigaben, Monitoring und Ereignisse.
- `src/LLMFriend.Infrastructure/Database.cs` – öffnet MySQL-Verbindungen mit String-Mapping für `CHAR(36)`-IDs, migriert das Schema und definiert Datenzeilen.
- `src/LLMFriend.Infrastructure/DiscordConnector.cs` – implementiert Discord-OAuth2, Profildaten, Serverliste und Rate-Limits.
- `src/LLMFriend.Infrastructure/LLMFriend.Infrastructure.csproj` – bindet Dapper, MySqlConnector und eingebettete Migration ein.
- `src/LLMFriend.Infrastructure/Migrations/001_initial.sql` – legt Nutzer-, Sitzungs-, Konto-, Freigabe-, Ereignis- und OAuth-Tabellen an.
- `src/LLMFriend.Infrastructure/Security.cs` – hasht Passwörter und Sitzungen und verschlüsselt OAuth-Tokens.
- `src/LLMFriend.Wpf/ApiClient.cs` – kapselt authentifizierte API-Aufrufe und Client-Datenmodelle.
- `src/LLMFriend.Wpf/App.xaml` – definiert den WPF-Anwendungseinstieg.
- `src/LLMFriend.Wpf/App.xaml.cs` – stellt die WPF-Anwendungsklasse bereit.
- `src/LLMFriend.Wpf/AssemblyInfo.cs` – enthält WPF-Theme-Metadaten.
- `src/LLMFriend.Wpf/DialogService.cs` – bündelt Eingabe-, Bestätigungs- und Passwortdialoge.
- `src/LLMFriend.Wpf/InverseBooleanToVisibilityConverter.cs` – steuert die Sichtbarkeit der Login-Ansicht.
- `src/LLMFriend.Wpf/LLMFriend.Wpf.csproj` – definiert das Windows-WPF-Projekt.
- `src/LLMFriend.Wpf/MainViewModel.cs` – verwaltet Login, Kontoliste, Details, Freigaben und Admin-Befehle.
- `src/LLMFriend.Wpf/MainWindow.xaml` – stellt die deutsche Konten-, Verlaufs- und Freigabeoberfläche dar.
- `src/LLMFriend.Wpf/MainWindow.xaml.cs` – reicht Passwortfeld-Eingaben an das ViewModel weiter.
- `tests/LLMFriend.Core.Tests/CoreRulesTests.cs` – prüft Rechte, Änderungsvergleich und PKCE-Vertrag.
- `tests/LLMFriend.Core.Tests/LLMFriend.Core.Tests.csproj` – definiert die kurze Headless-Kernsuite.
- `tests/LLMFriend.Integration.Tests/DatabaseFlowTests.cs` – prüft Migration, Authentifizierung, Isolation und Tokenrotation mit MySQL.
- `tests/LLMFriend.Integration.Tests/LLMFriend.Integration.Tests.csproj` – definiert den getrennten MySQL-Integrationstest.
- `agent logs/2026-11-06_00-37-19_initiales-llmfriend-projekt.md` – dokumentiert diesen zusammenhängenden Change und seine tatsächlichen Prüfungen.

Prüfungen:

- `dotnet build LLMFriend.sln -v:q`: erfolgreich, 0 Warnungen, 0 Fehler.
- `dotnet test tests/LLMFriend.Core.Tests/LLMFriend.Core.Tests.csproj -v:normal`: 3 Tests gefunden, 3 bestanden.
- `git diff --cached --check`: keine Whitespace-Fehler nach Bereinigung.
- WPF-Client gestartet und Windows-Accessibility-Struktur geprüft: Fenster, API-Adresse, Nutzername, Passwort und Anmeldebutton sichtbar. Screenshot-Capture und Klicktest scheiterten an der UI-Capture-Schnittstelle; kein erfolgreicher visueller oder End-to-End-UI-Test behauptet.
- Freigegebener lokaler MySQL-Integrationstest: **nicht ausgeführt**. Docker Desktop stellte keinen Engine-Prozess bereit; `Start-Service com.docker.service` scheiterte an fehlenden Windows-Rechten. Eine grüne MySQL-Prüfung liegt lokal nicht vor.
- Live-Discord-OAuth-Test: nicht ausgeführt, da noch keine Discord-Developer-App und keine Zugangsdaten vorhanden sind.
- GitHub Actions für Commit `9137232`: WPF-Build und 3 Kernprüfungen erfolgreich; MySQL-Vertragstest fehlgeschlagen. Der erste CI-Lauf unterdrückte den konkreten Fehler durch `--verbosity quiet`; die Ausgabe wurde für den nächsten Lauf erweitert.
- GitHub Actions für Commit `ab87348`: WPF-Build und Kernprüfungen erfolgreich; MySQL-Vertragstest scheiterte bei `UserService.FindAsync` an Dappers String-Mapping einer von MySqlConnector als `Guid` gelesenen `CHAR(36)`-ID. `GuidFormat=None` in `Database` behebt die Ursache.
- `dotnet build LLMFriend.sln -v:q` nach der MySQL-Mapping-Korrektur: erfolgreich, 0 Warnungen, 0 Fehler.
- GitHub Actions für Commit `3280d3d` ([Lauf 42131631717](https://github.com/kaitokid222/LLMFriend/actions/runs/42131631717)): `headless-core` einschließlich der MySQL-Vertragsprüfung erfolgreich; separater `wpf-build` erfolgreich.
- Abschließender Quellenabgleich des Logeintrags mit GitHub-Actions-Lauf 42131631717 sowie `git diff --check` und Diff-Prüfung: Link und Ergebnisse stimmen; keine Whitespace-Fehler. Für diesen reinen Log-Nachtrag wurden keine Anwendung oder weiteren lokalen Tests gestartet.

Quellen (nur gelesen, keine Änderungsdateien): Discord- und .NET-Dokumentation, MySQL-8.4-Dokumentation, die fremden GitHub-Projekte `PomeloFoundation/Pomelo.EntityFrameworkCore.MySql` und `mysql-net/MySqlConnector`.

Offen: Für einen Live-OAuth-Test sind Discord-App-Konfiguration und private Zugangsdaten erforderlich; für ein Deployment fehlt der konkrete Zielserver. Der lokale Docker-Dienst braucht einen Start mit ausreichenden Windows-Rechten, falls die MySQL-Prüfung auch lokal wiederholt werden soll.
