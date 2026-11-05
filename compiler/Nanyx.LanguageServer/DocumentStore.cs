using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NyxCompiler;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace Nanyx.LanguageServer;

/// <summary>An open document: its text and the result of compiling that text.</summary>
/// <param name="Text">The editor's buffer, which is usually ahead of the saved file.</param>
/// <param name="Compiled">What the compiler made of <paramref name="Text"/>; <c>null</c> if the compiler threw.</param>
/// <param name="CompilerFailure">The exception the compiler threw, if it did.</param>
public sealed record OpenDocument(string Text, CompileResult? Compiled, Exception? CompilerFailure)
{
    /// <summary>Converts positions in <see cref="Text"/>, built once per change rather than on every request.</summary>
    public SourcePositions Positions { get; } = new(Text);
}

/// <summary>
/// Every document the client has open, keyed by URI, with the last compile of its text. A document is compiled
/// once per change, and diagnostics and hover both read that one result.
/// </summary>
public sealed class DocumentStore
{
    private readonly ConcurrentDictionary<DocumentUri, OpenDocument> _documents = new();
    private readonly ILogger<DocumentStore> _logger;

    public DocumentStore(ILogger<DocumentStore> logger)
    {
        _logger = logger;
    }

    /// <summary>Compiles the full text of a document after it was opened or changed, and records both.</summary>
    public OpenDocument Update(DocumentUri uri, string text)
    {
        var document = Compile(uri, text);
        _documents[uri] = document;
        return document;
    }

    /// <summary>Forgets a document the client has closed.</summary>
    public void Remove(DocumentUri uri) => _documents.TryRemove(uri, out _);

    /// <summary>An open document, or <c>null</c> if the client has not opened it.</summary>
    public OpenDocument? TryGet(DocumentUri uri) => _documents.TryGetValue(uri, out var document) ? document : null;

    private OpenDocument Compile(DocumentUri uri, string text)
    {
        try
        {
            // A file on disk gets its imports resolved relative to itself; anything else (an untitled buffer, say)
            // can still import the standard library.
            var path = uri.Scheme == "file" ? uri.GetFileSystemPath() : null;
            var result = path is null ? Compiler.compile(text) : Compiler.compileSource(path, text);
            return new OpenDocument(text, result, null);
        }
        catch (Exception ex)
        {
            // A compiler bug must not take the server down with it: it is reported as a diagnostic instead.
            _logger.LogError(ex, "The compiler failed on {Uri}", uri);
            return new OpenDocument(text, null, ex);
        }
    }
}
