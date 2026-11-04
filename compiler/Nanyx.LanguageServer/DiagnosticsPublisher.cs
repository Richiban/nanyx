using Microsoft.Extensions.Logging;
using NyxCompiler;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using Diagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Nanyx.LanguageServer;

/// <summary>Compiles a document and sends the resulting diagnostics to the client.</summary>
public sealed class DiagnosticsPublisher
{
    /// <summary>The <c>source</c> field on every diagnostic, so the editor can attribute them.</summary>
    public const string Source = "nanyx";

    private readonly ILanguageServerFacade _server;
    private readonly ILogger<DiagnosticsPublisher> _logger;

    public DiagnosticsPublisher(ILanguageServerFacade server, ILogger<DiagnosticsPublisher> logger)
    {
        _server = server;
        _logger = logger;
    }

    /// <summary>Compiles <paramref name="text"/> as the document at <paramref name="uri"/> and publishes what the compiler reports.</summary>
    public void Publish(DocumentUri uri, string text, int? version)
    {
        var diagnostics = Compute(uri, text);

        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Version = version,
            Diagnostics = new Container<Diagnostic>(diagnostics)
        });
    }

    /// <summary>Tells the client a document has no diagnostics any more (it was closed).</summary>
    public void Clear(DocumentUri uri)
    {
        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Diagnostics = new Container<Diagnostic>()
        });
    }

    /// <summary>The LSP diagnostics for a document, computed by calling the compiler as a library.</summary>
    public IReadOnlyList<Diagnostic> Compute(DocumentUri uri, string text)
    {
        CompileResult result;
        try
        {
            result = Compile(uri, text);
        }
        catch (Exception ex)
        {
            // A compiler bug must not take the server down with it: report it where the user will see it.
            _logger.LogError(ex, "The compiler failed on {Uri}", uri);
            return
            [
                new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Source = Source,
                    Range = new Range(0, 0, 0, 0),
                    Message = $"Internal compiler error: {ex.Message}"
                }
            ];
        }

        return result.Diagnostics.Select(diagnostic => ToLsp(diagnostic, text)).ToList();
    }

    private static CompileResult Compile(DocumentUri uri, string text)
    {
        // A file on disk gets its imports resolved relative to itself; anything else (an untitled buffer, say)
        // can still import the standard library.
        var path = uri.Scheme == "file" ? uri.GetFileSystemPath() : null;
        return path is null ? Compiler.compile(text) : Compiler.compileSource(path, text);
    }

    private static Diagnostic ToLsp(NyxCompiler.Diagnostic diagnostic, string text) =>
        new()
        {
            Severity = diagnostic.Severity.IsWarningSeverity ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
            Source = Source,
            Message = diagnostic.Message,
            Range = ToRange(diagnostic, text)
        };

    /// <summary>
    /// The compiler's ranges are a zero-based start position only, so the end is found by extending over the
    /// identifier at that position (or one character where there is none). A diagnostic without a range is
    /// shown at the start of the file.
    /// </summary>
    private static Range ToRange(NyxCompiler.Diagnostic diagnostic, string text)
    {
        if (diagnostic.Range is null)
        {
            return new Range(0, 0, 0, 0);
        }

        var (line, column) = (diagnostic.Range.Value.Item1, diagnostic.Range.Value.Item2);
        var lineText = LineAt(text, line);
        column = Math.Clamp(column, 0, lineText.Length);

        var end = column;
        while (end < lineText.Length && IsIdentifierChar(lineText[end]))
        {
            end++;
        }

        if (end == column && end < lineText.Length)
        {
            end++;
        }

        return new Range(line, column, line, end);
    }

    private static string LineAt(string text, int line)
    {
        var lines = text.Split('\n');
        return line >= 0 && line < lines.Length ? lines[line].TrimEnd('\r') : string.Empty;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
