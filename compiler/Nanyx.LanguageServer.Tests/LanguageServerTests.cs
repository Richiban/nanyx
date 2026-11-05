using System.IO.Pipelines;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using Xunit;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Nanyx.LanguageServer.Tests;

/// <summary>
/// Drives the server in-process through a real LSP client over a pair of pipes, so what is tested is the
/// same wire protocol an editor speaks.
/// </summary>
public sealed class LanguageServerTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly Pipe _clientToServer = new();
    private readonly Pipe _serverToClient = new();
    private readonly CancellationTokenSource _cancellation = new(Timeout);
    private readonly List<PublishDiagnosticsParams> _published = [];
    private readonly SemaphoreSlim _publishedSignal = new(0);

    private Task _serverTask = Task.CompletedTask;
    private LanguageClient _client = null!;

    public async Task InitializeAsync()
    {
        _serverTask = NanyxLanguageServer.RunAsync(
            _clientToServer.Reader.AsStream(),
            _serverToClient.Writer.AsStream(),
            _cancellation.Token);

        _client = LanguageClient.PreInit(options => options
            .WithInput(_serverToClient.Reader)
            .WithOutput(_clientToServer.Writer)
            .OnPublishDiagnostics(diagnostics =>
            {
                lock (_published)
                {
                    _published.Add(diagnostics);
                }

                _publishedSignal.Release();
            }));

        await _client.Initialize(_cancellation.Token);
    }

    public async Task DisposeAsync()
    {
        // Shutdown() sends the shutdown request followed by the exit notification.
        await _client.Shutdown();
        await _serverTask;
        _client.Dispose();
    }

    [Fact]
    public void Server_advertises_full_document_sync()
    {
        var sync = _client.ServerSettings.Capabilities.TextDocumentSync;

        Assert.NotNull(sync);
        Assert.Equal(TextDocumentSyncKind.Full, sync.Options?.Change);
        Assert.True(sync.Options?.OpenClose);
    }

    [Fact]
    public async Task Type_error_is_published_with_its_range_on_open()
    {
        var uri = DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "type-error.nyx"));

        // An unknown identifier is the type error the compiler currently gives a position to
        // (a plain type mismatch has no range yet, and lands at the start of the file).
        Open(uri, "import nanyx/console\n\nprintln(missing)\n");

        var diagnostics = await NextPublished(uri);
        var diagnostic = Assert.Single(diagnostics.Diagnostics!);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("nanyx", diagnostic.Source);
        Assert.Contains("missing", diagnostic.Message.String);
        Assert.Equal(new Range(2, 8, 2, 15), diagnostic.Range);
    }

    [Fact]
    public async Task Diagnostics_are_cleared_when_the_change_fixes_the_error()
    {
        var uri = DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "fixed.nyx"));

        Open(uri, "import nanyx/console\n\nprintln(missing)\n");
        var withError = await NextPublished(uri);
        Assert.NotEmpty(withError.Diagnostics!);

        Change(uri, version: 2, "import nanyx/console\n\nprintln(\"found\")\n");
        var fixedResult = await NextPublished(uri);
        Assert.Empty(fixedResult.Diagnostics!);
        Assert.Equal(2, fixedResult.Version);
    }

    [Fact]
    public async Task Parse_error_yields_one_diagnostic_instead_of_nothing()
    {
        var uri = DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "parse-error.nyx"));

        Open(uri, "def = \n");

        var diagnostics = await NextPublished(uri);
        var diagnostic = Assert.Single(diagnostics.Diagnostics!);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.True(diagnostic.Range.Start.Line >= 0);
    }

    [Fact]
    public async Task Standard_library_imports_resolve_for_an_untitled_document()
    {
        var uri = DocumentUri.Parse("untitled:Untitled-1");

        Open(uri, "import nanyx/text\n\ndef shout = toUpper(\"hi\")\n");

        var diagnostics = await NextPublished(uri);
        Assert.Empty(diagnostics.Diagnostics!);
    }

    [Fact]
    public async Task Closing_a_document_clears_its_diagnostics()
    {
        var uri = DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "closed.nyx"));

        Open(uri, "def = \n");
        Assert.NotEmpty((await NextPublished(uri)).Diagnostics!);

        _client.DidCloseTextDocument(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier(uri)
        });

        Assert.Empty((await NextPublished(uri)).Diagnostics!);
    }

    [Fact]
    public void Server_advertises_hover()
    {
        Assert.NotNull(_client.ServerSettings.Capabilities.HoverProvider);
    }

    [Fact]
    public async Task Hover_shows_the_inferred_type_of_the_expression_under_the_pointer()
    {
        var uri = DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "hover.nyx"));

        // CRLF line endings: positions are still counted per line, as the editor counts them.
        Open(uri, "def id = { x -> x }\r\ndef total = id(40) + 2\r\n");
        Assert.Empty((await NextPublished(uri)).Diagnostics!);

        // The literal `40`, innermost inside the call and the addition.
        var literal = await HoverAt(uri, 1, 16);
        Assert.NotNull(literal);
        Assert.Equal("```nanyx\nint\n```", literal.Contents.MarkupContent?.Value);
        Assert.Equal(MarkupKind.Markdown, literal.Contents.MarkupContent?.Kind);
        Assert.Equal(new Range(1, 15, 1, 17), literal.Range);

        // The polymorphic `id`, instantiated at this call.
        var callee = await HoverAt(uri, 1, 13);
        Assert.Equal("```nanyx\nint -> int\n```", callee?.Contents.MarkupContent?.Value);
        Assert.Equal(new Range(1, 12, 1, 14), callee?.Range);

        // The lambda in its own definition, with its type variable named for reading.
        var lambda = await HoverAt(uri, 0, 9);
        Assert.Equal("```nanyx\n'a -> 'a\n```", lambda?.Contents.MarkupContent?.Value);
    }

    [Fact]
    public async Task Hover_outside_any_expression_returns_nothing()
    {
        var uri = DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "hover-nothing.nyx"));

        Open(uri, "def answer = 42\n");
        Assert.Empty((await NextPublished(uri)).Diagnostics!);

        Assert.Null(await HoverAt(uri, 0, 1));
    }

    [Fact]
    public async Task Hover_in_a_document_that_does_not_type_check_returns_nothing()
    {
        var uri = DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "hover-error.nyx"));

        Open(uri, "def answer = missing + 1\n");
        Assert.NotEmpty((await NextPublished(uri)).Diagnostics!);

        Assert.Null(await HoverAt(uri, 0, 23));
    }

    private Task<Hover?> HoverAt(DocumentUri uri, int line, int character) =>
        _client.TextDocument.RequestHover(
            new HoverParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(line, character) },
            _cancellation.Token);

    private void Open(DocumentUri uri, string text) =>
        _client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "nanyx",
                Version = 1,
                Text = text
            }
        });

    private void Change(DocumentUri uri, int version, string text) =>
        _client.DidChangeTextDocument(new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = uri, Version = version },
            ContentChanges = new Container<TextDocumentContentChangeEvent>(new TextDocumentContentChangeEvent { Text = text })
        });

    private async Task<PublishDiagnosticsParams> NextPublished(DocumentUri uri)
    {
        while (true)
        {
            await _publishedSignal.WaitAsync(_cancellation.Token);

            lock (_published)
            {
                var index = _published.FindIndex(p => p.Uri == uri);
                if (index >= 0)
                {
                    var found = _published[index];
                    _published.RemoveAt(index);
                    return found;
                }
            }
        }
    }
}
