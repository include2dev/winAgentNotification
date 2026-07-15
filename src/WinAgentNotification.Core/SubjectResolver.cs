namespace WinAgentNotification.Core;

public static class SubjectResolver
{
    public static IReadOnlyList<string> Resolve(
        IEnumerable<string> templates, string hostname, string username)
    {
        var host = SanitizeToken(hostname);
        var user = SanitizeToken(username);

        return templates
            .Select(t => t.Replace("{hostname}", host).Replace("{username}", user))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToList();
    }

    public static string SanitizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        var sanitized = value.Trim().ToLowerInvariant()
            .Select(c => char.IsWhiteSpace(c) || c is '.' or '*' or '>' ? '-' : c);

        return new string(sanitized.ToArray());
    }
}
