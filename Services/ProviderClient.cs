using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using CodexClaudeUsageWidget.Models;

namespace CodexClaudeUsageWidget.Services;

public sealed class ProviderClient(LocalStorage storage) : IDisposable
{
    private const string CodexRedirect = "http://localhost:1455/auth/callback";
    private const string ClaudeRedirect = "https://platform.claude.com/oauth/code/callback";
    private const string CodexTokenUrl = "https://auth.openai.com/oauth/token";
    private const string ClaudeTokenUrl = "https://api.anthropic.com/v1/oauth/token";
    private static readonly string[] ClaudeScopes = ["user:profile", "user:inference"];
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly object _credentialsGate = new();
    private (string State, string Verifier)? _claudePending;

    public async Task StartCodexLoginAsync(CancellationToken cancellationToken = default)
    {
        var clientId = ProviderOAuthClients.Codex;
        var state = RandomToken(24);
        var verifier = RandomToken(48);
        var challenge = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = CodexRedirect,
            ["scope"] = "openid profile email offline_access",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["id_token_add_organizations"] = "true",
            ["codex_cli_simplified_flow"] = "true",
            ["originator"] = "codex_cli_rs",
            ["prompt"] = "login",
            ["state"] = state,
        };
        var authorizeUrl = AddQuery("https://auth.openai.com/oauth/authorize", query);

        using var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:1455/");
        listener.Start();
        try
        {
            Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true });
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
            if (!string.Equals(context.Request.Url?.AbsolutePath, "/auth/callback", StringComparison.Ordinal))
            {
                await RespondAsync(context.Response, 404, "Not found. Return to the usage widget.");
                throw new InvalidOperationException("Codex returned to an unexpected callback URL.");
            }
            if (!string.Equals(context.Request.QueryString["state"], state, StringComparison.Ordinal))
            {
                await RespondAsync(context.Response, 400, "Sign-in state check failed. Close this tab and retry.");
                throw new InvalidOperationException("Codex sign-in state did not match.");
            }
            var code = context.Request.QueryString["code"];
            if (string.IsNullOrWhiteSpace(code))
            {
                await RespondAsync(context.Response, 400, "No authorization code was returned. Close this tab and retry.");
                throw new InvalidOperationException("Codex did not return an authorization code.");
            }
            await RespondAsync(context.Response, 200, "Codex connected. You can close this tab and return to the usage widget.");
            var tokens = await ExchangeCodexCodeAsync(clientId, code, verifier, cancellationToken);
            var accountId = ReadCodexAccountId(tokens);
            if (string.IsNullOrWhiteSpace(accountId))
                throw new InvalidOperationException("Sign-in succeeded, but the token response did not include a Codex account ID.");
            SaveTokens("codex", ToProviderTokens(tokens, accountId));
        }
        catch (HttpListenerException exception) when (exception.ErrorCode == 183)
        {
            throw new InvalidOperationException("Port 1455 is in use. Close another Codex sign-in flow and retry.", exception);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Codex sign-in timed out. Start sign-in again.");
        }
        finally
        {
            listener.Stop();
        }
    }

    public string BeginClaudeLogin()
    {
        var clientId = ProviderOAuthClients.Claude;
        var state = RandomToken(24);
        var verifier = RandomToken(48);
        var challenge = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));
        _claudePending = (state, verifier);
        var query = new Dictionary<string, string>
        {
            ["code"] = "true",
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = ClaudeRedirect,
            ["scope"] = string.Join(' ', ClaudeScopes),
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["prompt"] = "login",
        };
        var url = AddQuery("https://claude.ai/oauth/authorize", query);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return "Complete sign-in in the browser, then paste the callback code or full callback URL below.";
    }

    public async Task CompleteClaudeLoginAsync(string codeOrCallback, CancellationToken cancellationToken = default)
    {
        var pending = _claudePending ?? throw new InvalidOperationException("Start Claude sign-in before submitting a callback code.");
        var (code, callbackState) = ParseClaudeCallback(codeOrCallback, pending.State);
        var body = new
        {
            code,
            state = callbackState,
            grant_type = "authorization_code",
            client_id = ProviderOAuthClients.Claude,
            redirect_uri = ClaudeRedirect,
            code_verifier = pending.Verifier,
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, ClaudeTokenUrl)
        {
            Content = JsonContent.Create(body),
        };
        using var response = await _http.SendAsync(request, cancellationToken);
        var tokens = await ReadTokenResponseAsync(response, "Claude", cancellationToken);
        SaveTokens("claude", ToProviderTokens(tokens, null));
        _claudePending = null;
    }

    public async Task<QuotaSnapshot> GetUsageAsync(string provider, CancellationToken cancellationToken = default)
    {
        var tokens = ReadTokens(provider);
        if (tokens.ExpiresAt is not null && tokens.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(1))
            tokens = await RefreshTokensAsync(provider, tokens, cancellationToken);
        using var response = await SendUsageAsync(provider, tokens, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized && !string.IsNullOrWhiteSpace(tokens.RefreshToken))
        {
            tokens = await RefreshTokensAsync(provider, tokens, cancellationToken);
            using var retry = await SendUsageAsync(provider, tokens, cancellationToken);
            return await ParseUsageResponseAsync(provider, retry, cancellationToken);
        }
        return await ParseUsageResponseAsync(provider, response, cancellationToken);
    }


    public bool IsConnected(string provider) => ReadCredentials().ContainsKey(provider);

    public void Disconnect(string provider)
    {
        lock (_credentialsGate)
        {
            var credentials = storage.ReadCredentials();
            credentials.Remove(provider);
            storage.WriteCredentials(credentials);
        }
    }

    private async Task<QuotaSnapshot> ParseUsageResponseAsync(string provider, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException("Login expired or usage access was denied. Sign in again.");
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("Provider rate-limited the usage check. The widget will retry later.");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Usage endpoint returned HTTP {(int)response.StatusCode}.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var fetchedAt = DateTimeOffset.UtcNow;
        return provider == "codex"
            ? QuotaParser.ParseCodex(document.RootElement, fetchedAt)
            : QuotaParser.ParseClaude(document.RootElement, fetchedAt);
    }

    private async Task<HttpResponseMessage> SendUsageAsync(string provider, ProviderTokens tokens, CancellationToken cancellationToken)
    {
        var url = provider switch
        {
            "codex" => "https://chatgpt.com/backend-api/wham/usage",
            "claude" => "https://api.anthropic.com/api/oauth/usage",
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        request.Headers.Accept.ParseAdd("application/json");
        if (provider == "codex")
        {
            if (string.IsNullOrWhiteSpace(tokens.AccountId)) throw new InvalidOperationException("Codex account ID is missing. Sign in again.");
            request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", tokens.AccountId);
            request.Headers.TryAddWithoutValidation("Origin", "https://chatgpt.com");
            request.Headers.Referrer = new Uri("https://chatgpt.com/");
        }
        else
        {
            request.Headers.TryAddWithoutValidation("User-Agent", "claude-code/2.1.222");
            request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
        }
        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private async Task<ProviderTokens> RefreshTokensAsync(string provider, ProviderTokens previous, CancellationToken cancellationToken)
    {
        var clientId = ClientIdFor(provider);
        if (string.IsNullOrWhiteSpace(previous.RefreshToken))
            throw new UnauthorizedAccessException("Login expired. Sign in again.");
        HttpRequestMessage request;
        if (provider == "codex")
        {
            request = new HttpRequestMessage(HttpMethod.Post, CodexTokenUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = previous.RefreshToken,
                    ["client_id"] = clientId,
                }),
            };
        }
        else
        {
            request = new HttpRequestMessage(HttpMethod.Post, ClaudeTokenUrl)
            {
                Content = JsonContent.Create(new
                {
                    grant_type = "refresh_token",
                    refresh_token = previous.RefreshToken,
                    client_id = clientId,
                }),
            };
        }
        using (request)
        using (var response = await _http.SendAsync(request, cancellationToken))
        {
            var refreshed = await ReadTokenResponseAsync(response, provider, cancellationToken);
            var updated = ToProviderTokens(refreshed, previous.AccountId);
            SetTokens(provider, updated);
            return updated;
        }
    }

    private async Task<JsonElement> ExchangeCodexCodeAsync(string clientId, string code, string verifier, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, CodexTokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = clientId,
                ["code"] = code,
                ["redirect_uri"] = CodexRedirect,
                ["code_verifier"] = verifier,
            }),
        };
        using var response = await _http.SendAsync(request, cancellationToken);
        return await ReadTokenResponseAsync(response, "Codex", cancellationToken);
    }

    private async Task<JsonElement> ReadTokenResponseAsync(HttpResponseMessage response, string provider, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = document.RootElement.TryGetProperty("error_description", out var description)
                ? description.GetString()
                : document.RootElement.TryGetProperty("error", out var error) ? error.ToString() : null;
            throw new HttpRequestException($"{provider} authentication failed: {detail ?? $"HTTP {(int)response.StatusCode}"}.");
        }
        return document.RootElement.Clone();
    }


    private static (string Code, string State) ParseClaudeCallback(string callback, string expectedState)
    {
        var value = callback.Trim();
        string? code = null;
        string? state = null;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            var values = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .ToDictionary(parts => Uri.UnescapeDataString(parts[0]), parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "", StringComparer.Ordinal);
            values.TryGetValue("code", out code);
            values.TryGetValue("state", out state);
        }
        if (string.IsNullOrWhiteSpace(code))
        {
            var separator = value.LastIndexOf('#');
            if (separator >= 0)
            {
                code = value[..separator];
                state = value[(separator + 1)..];
            }
            else code = value;
        }
        if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("Paste the Claude authorization code or callback URL.");
        if (string.IsNullOrWhiteSpace(state)) throw new InvalidOperationException("The Claude callback did not include OAuth state. Paste the full callback URL or code#state value.");
        if (!string.Equals(state, expectedState, StringComparison.Ordinal))
            throw new InvalidOperationException("Claude sign-in state did not match. Start again.");
        return (code, state);
    }

    private static string? ReadCodexAccountId(JsonElement tokens)
    {
        var jwt = ReadString(tokens, "id_token") ?? ReadString(tokens, "access_token");
        if (jwt is null) return null;
        try
        {
            var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            if (!document.RootElement.TryGetProperty("https://api.openai.com/auth", out var auth)) return null;
            return ReadString(auth, "chatgpt_account_id") ?? ReadString(auth, "account_id") ?? ReadString(auth, "chatgpt_user_id");
        }
        catch (Exception exception) when (exception is FormatException or JsonException or IndexOutOfRangeException) { return null; }
    }

    private static ProviderTokens ToProviderTokens(JsonElement token, string? accountId)
    {
        var accessToken = ReadString(token, "access_token") ?? throw new InvalidDataException("Provider token response had no access_token.");
        var refreshToken = ReadString(token, "refresh_token");
        DateTimeOffset? expiresAt = token.TryGetProperty("expires_in", out var expires) && expires.TryGetInt64(out var seconds)
            ? DateTimeOffset.UtcNow.AddSeconds(seconds)
            : null;
        return new ProviderTokens(accessToken, refreshToken, expiresAt, accountId);
    }

    private ProviderTokens ReadTokens(string provider) =>
        ReadCredentials().TryGetValue(provider, out var tokens)
            ? tokens
            : throw new UnauthorizedAccessException("Account is not connected.");

    private Dictionary<string, ProviderTokens> ReadCredentials()
    {
        lock (_credentialsGate) return storage.ReadCredentials();
    }

    private void SaveTokens(string provider, ProviderTokens tokens) => SetTokens(provider, tokens);

    private void SetTokens(string provider, ProviderTokens tokens)
    {
        lock (_credentialsGate)
        {
            var credentials = storage.ReadCredentials();
            credentials[provider] = tokens;
            storage.WriteCredentials(credentials);
        }
    }

    private static string ClientIdFor(string provider) => provider switch
    {
        "codex" => ProviderOAuthClients.Codex,
        "claude" => ProviderOAuthClients.Claude,
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };

    private static string RandomToken(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string AddQuery(string baseUrl, IReadOnlyDictionary<string, string> query) =>
        baseUrl + "?" + string.Join('&', query.Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static async Task RespondAsync(HttpListenerResponse response, int status, string message)
    {
        var bytes = Encoding.UTF8.GetBytes($"<!doctype html><meta charset=utf-8><title>Sign-in</title><p>{WebUtility.HtmlEncode(message)}</p>");
        response.StatusCode = status;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    public void Dispose() => _http.Dispose();
}
