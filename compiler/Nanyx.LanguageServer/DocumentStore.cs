using System.Collections.Concurrent;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace Nanyx.LanguageServer;

/// <summary>The current text of every document the client has open, keyed by URI.</summary>
/// <remarks>
/// Handlers for hover, definitions and completion will read from here rather than from disk, because the
/// editor's buffer is usually ahead of the saved file. When those arrive, the natural extension is to cache
/// the last <c>CompileResult</c> per document alongside its text.
/// </remarks>
public sealed class DocumentStore
{
    private readonly ConcurrentDictionary<DocumentUri, string> _documents = new();

    /// <summary>Records the full text of a document after it was opened or changed.</summary>
    public void Set(DocumentUri uri, string text) => _documents[uri] = text;

    /// <summary>Forgets a document the client has closed.</summary>
    public void Remove(DocumentUri uri) => _documents.TryRemove(uri, out _);

    /// <summary>The text of an open document, or <c>null</c> if the client has not opened it.</summary>
    public string? TryGet(DocumentUri uri) => _documents.TryGetValue(uri, out var text) ? text : null;
}
