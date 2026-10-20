# Codex + Claude Usage Widget

Windows tray app for Codex and Claude Code quota windows. Hovering the tray icon shows a short summary; clicking it opens the compact WPF panel.

## Requirements and run

- Windows 10/11
- .NET 10 SDK

From this directory:

```powershell
dotnet restore
dotnet run
```

Build the Windows x64 installer (developer machine only; requires the .NET 10 SDK and Inno Setup 6):

```powershell
.\build-installer.ps1
```

The build creates `dist\CodexClaudeUsageWidget-Setup.exe`. End users do not need PowerShell or .NET: double-click the setup executable and follow the installer. It installs per-user, adds a Start menu shortcut, and offers an optional desktop shortcut. Uninstall it through **Settings → Apps**; saved sign-ins and usage cache are kept. The installer is not code-signed, so Windows SmartScreen may show a warning.

## Downloads

Open [GitHub Releases](https://github.com/jepicode/codex-claude-usage-monitor/releases) and download the newest listed Windows x64 setup executable. Each release includes `CodexClaudeUsageWidget-Setup.exe` and `SHA256SUMS.txt` to verify the download.

Releases are built from version tags (`v*`) by GitHub Actions. The workflow runs the quota parser tests, builds the Inno Setup installer, verifies its checksum, and publishes both files with generated release notes.


Run quota response tests with:

```powershell
dotnet test .\tests\QuotaParser.Tests.csproj
```

The panel opens when the app starts. Closing it hides it to the tray; click the tray icon or use **Open usage** in its right-click menu to show it again. The tray menu also offers refresh and quit. Startup-at-login is disabled. Usage refreshes every five minutes; after a provider rate limit, that provider is paused for 15 minutes. The latest successful quota response is cached locally and stays visible with an error if the provider is temporarily unavailable.

## Login

Click **Connect account** to open the provider login page in your default browser. Public PKCE OAuth client IDs from the upstream Codex and Claude Code CLI clients are built in; users do not need to find or enter them. They are public identifiers, not account credentials, and providers may change or restrict them. [Source notes](https://github.com/diegosouzapw/OmniRoute/blob/main/docs/security/PUBLIC_CREDS.md).

Codex returns to `http://localhost:1455/auth/callback`; that local port must be available. Claude returns a callback code through its hosted page; paste the full callback URL or `code#state` value into the panel to complete sign-in.

## Data source and limitations

After OAuth, the app requests:

- Codex: `https://chatgpt.com/backend-api/wham/usage`
- Claude: `https://api.anthropic.com/api/oauth/usage`

These are undocumented provider-client endpoints and can change without notice. Authentication rules, response fields, availability, or account eligibility can change; unknown quota responses fail closed rather than showing invented values. Official usage pages remain linked in the cards.

The displayed percentages and reset times come from provider responses. They are not invoice amounts or token totals, and do not imply usage across products/devices that a provider excludes.

## Local security

- Codex and Claude OAuth tokens are protected with Windows DPAPI for the current user and stored under `%LOCALAPPDATA%\CodexClaudeUsageWidget`.
- Public OAuth client IDs are embedded in the app; they are not account credentials.
- The application sends requests directly to providers. It has no telemetry or remote backend.
- Authorization headers and token values are not displayed or written to logs.
- Disconnect removes the provider's locally stored token.

## Scope

Live usage requires connected Codex and Claude accounts. Their quota endpoints are not documented public APIs; keep the official usage dashboards as a fallback.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).
