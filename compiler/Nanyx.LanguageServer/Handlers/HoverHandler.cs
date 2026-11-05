using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using CompilerHover = NyxCompiler.Hover;

namespace Nanyx.LanguageServer.Handlers;

/// <summary>
/// Shows the inferred type of the expression under the pointer, read from the document's last compile. On a
/// call's function name it shows the function's type instantiated at that call. Nothing is shown in a document
/// that did not type-check.
/// </summary>
public sealed class HoverHandler : HoverHandlerBase
{
    private readonly DocumentStore _documents;

    public HoverHandler(DocumentStore documents)
    {
        _documents = documents;
    }

    public override Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken) =>
        Task.FromResult(Compute(request));

    private Hover? Compute(HoverParams request)
    {
        var document = _documents.TryGet(request.TextDocument.Uri);
        if (document?.Compiled is not { } compiled)
        {
            return null;
        }

        var positions = document.Positions;
        var info = CompilerHover.typeAt(compiled, positions.ToOffset(request.Position));
        if (info is null)
        {
            return null;
        }

        return new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = $"```nanyx\n{info.Value.Type}\n```"
            }),
            Range = positions.ToRange(info.Value.Span.Start, info.Value.Span.End)
        };
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability capability,
        ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = TextDocumentSyncHandler.Selector };
}
