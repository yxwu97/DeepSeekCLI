using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using System.Text.RegularExpressions;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DshBrowserSession : IDshBrowserSession
{
    private static readonly Regex Announcement = new(
        @"^dsh web: (?<url>https?://[^\s]+)(?: \(LAN: [^\s]+\))?$",
        RegexOptions.CultureInvariant);
    private static readonly Regex TokenQuery = new(
        @"^\?token=[A-Za-z0-9_-]{43}$",
        RegexOptions.CultureInvariant);
    private readonly object _sync = new();
    private Uri? _origin;
    private Uri? _authenticationUri;
    private bool _external;

    public void Begin(Uri origin)
    {
        lock (_sync)
        {
            _origin = ServiceUriValidator.NormalizeOrThrow(origin);
            _authenticationUri = null;
            _external = false;
        }
    }

    public void CaptureOutput(string line)
    {
        if (line.Length > OutputLineProcessor.MaximumLineLength) return;
        var match = Announcement.Match(line);
        if (!match.Success
            || match.Groups["url"].Value.IndexOf('%') >= 0
            || !Uri.TryCreate(match.Groups["url"].Value, UriKind.Absolute, out var candidate)
            || !ServiceUriValidator.IsAllowedLoopbackTarget(candidate)
            || candidate.AbsolutePath != "/"
            || candidate.Fragment.Length != 0
            || !TokenQuery.IsMatch(candidate.Query)) return;

        lock (_sync)
        {
            if (_origin is not null && MatchesLaunchOrigin(candidate, _origin))
            {
                _authenticationUri ??= candidate;
            }
        }
    }

    public Uri? GetAuthenticationUri(Uri origin)
    {
        lock (_sync)
        {
            return _origin is not null && (CodeWebViewService.IsSameOrigin(origin, _origin)
                || (_authenticationUri is not null && CodeWebViewService.IsSameOrigin(origin, _authenticationUri)))
                ? _authenticationUri
                : null;
        }
    }

    public bool TryBeginExternal(Uri origin, string authenticationLink)
    {
        // Explicit user input uses the same fixed URL grammar as the official announcement.
        if (authenticationLink.Length > 2048 || authenticationLink.IndexOf('%') >= 0
            || !Uri.TryCreate(authenticationLink.Trim(), UriKind.Absolute, out var candidate)
            || !ServiceUriValidator.TryNormalize(origin, out origin, out _)
            || !ServiceUriValidator.IsAllowedLoopbackTarget(candidate)
            || candidate.AbsolutePath != "/" || candidate.Fragment.Length != 0
            || !TokenQuery.IsMatch(candidate.Query) || !MatchesLaunchOrigin(candidate, origin)) return false;
        lock (_sync)
        {
            _origin = origin;
            _authenticationUri = candidate;
            _external = true;
        }
        return true;
    }

    public void ClearExternal()
    {
        lock (_sync)
        {
            if (_external) Clear();
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _origin = null;
            _authenticationUri = null;
            _external = false;
        }
    }

    private static bool MatchesLaunchOrigin(Uri candidate, Uri configured) =>
        CodeWebViewService.IsSameOrigin(candidate, configured)
        || (configured.Host is "localhost" or "[::1]"
            && candidate.Host == "127.0.0.1"
            && candidate.Scheme == configured.Scheme
            && candidate.Port == configured.Port);
}
