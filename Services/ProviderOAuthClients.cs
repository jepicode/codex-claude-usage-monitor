namespace CodexClaudeUsageWidget.Services;

/// <summary>
/// Public PKCE client identifiers distributed by the upstream native CLIs.
/// These are identifiers, not secrets; provider policy or redirect allowlists may change.
/// </summary>
internal static class ProviderOAuthClients
{
    public const string Codex = "app_EMoamEEZ73f0CkXaXp7hrann";
    public const string Claude = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
}
