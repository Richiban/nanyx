using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using Diagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Nanyx.LanguageServer;

/// <summary>Sends the diagnostics from a document's compile to the client.</summary>
public sealed class DiagnosticsPublisher
{
    /// <summary>The <c>source</c> field on every diagnostic, so the editor can attribute them.</summary>
    public const string Source = "nanyx";

    private readonly ILanguageServerFacade _server;

    public DiagnosticsPublisher(ILanguageServerFacade server)
    {
        _server = server;
    }

    /// <summary>Publishes what the compiler reported for the document at <paramref name="uri"/>.</summary>
    public void Publish(DocumentUri uri, OpenDocument document, int? version)
    {
        var diagnostics = Compute(document);

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

    /// <summary>The LSP diagnostics for a compiled document.</summary>
    public static IReadOnlyList<Diagnostic> Compute(OpenDocument document)
    {
        if (document.Compiled is null)
        {
            // The compiler threw: report it where the user will see it.
            return
            [
                new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Source = Source,
                    Range = new Range(0, 0, 0, 0),
                    Message = $"Internal compiler error: {document.CompilerFailure?.Message}"
                }
            ];
        }

        return document.Compiled.Diagnostics.Select(diagnostic => ToLsp(diagnostic, document.Text)).ToList();
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
