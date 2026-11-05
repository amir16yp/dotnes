using System.Collections.Concurrent;

namespace dotnes.lsp;

/// <summary>
/// In-memory open document text keyed by URI.
/// </summary>
sealed class DocumentStore
{
    readonly ConcurrentDictionary<string, string> _documents = new(StringComparer.Ordinal);

    public void Set(string uri, string text) => _documents[uri] = text;

    public void Remove(string uri) => _documents.TryRemove(uri, out _);

    public bool TryGet(string uri, out string text) => _documents.TryGetValue(uri, out text!);

    public string GetOrRead(string uri)
    {
        if (TryGet(uri, out var text))
            return text;

        var path = UriToPath(uri);
        if (path != null && File.Exists(path))
        {
            text = File.ReadAllText(path);
            _documents[uri] = text;
            return text;
        }

        return string.Empty;
    }

    public static string? UriToPath(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return null;
        if (parsed.IsFile)
            return parsed.LocalPath;
        return null;
    }
}
