using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nanyx.LanguageServer.Handlers;
using OmniSharp.Extensions.LanguageServer.Server;

namespace Nanyx.LanguageServer;

/// <summary>
/// The Nanyx language server: a Language Server Protocol endpoint that calls the compiler as a library.
/// The <c>nanyx lsp</c> command runs it over stdio; tests run it over in-memory streams.
/// </summary>
/// <remarks>
/// This first slice publishes diagnostics only. Each editor feature is one handler class in
/// <c>Handlers/</c>, registered in <see cref="Configure"/>. To add a feature, add a handler that
/// derives from the matching OmniSharp base class and register it:
/// <list type="bullet">
///   <item><c>textDocument/hover</c> — a <c>HoverHandlerBase</c>, reading the typed module from <see cref="DocumentStore"/>.</item>
///   <item><c>textDocument/definition</c> — a <c>DefinitionHandlerBase</c>.</item>
///   <item><c>textDocument/completion</c> — a <c>CompletionHandlerBase</c>.</item>
/// </list>
/// These are deliberately not implemented yet: they need symbol ranges from the compiler and a settled
/// module/import story first.
/// </remarks>
public static class NanyxLanguageServer
{
    /// <summary>Runs the server over the process's standard input and output until the client asks it to exit.</summary>
    public static async Task RunStdioAsync(CancellationToken cancellationToken = default)
    {
        // The protocol owns stdout: nothing else in the process may write to it while the server runs.
        await RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput(), cancellationToken);
    }

    /// <summary>Runs the server over the given streams until the client asks it to exit.</summary>
    public static async Task RunAsync(Stream input, Stream output, CancellationToken cancellationToken = default)
    {
        var server = await OmniSharp.Extensions.LanguageServer.Server.LanguageServer.From(
            options => Configure(options).WithInput(input).WithOutput(output),
            cancellationToken);

        await server.WaitForExit;
    }

    /// <summary>Registers the services and handlers that make up the server.</summary>
    public static LanguageServerOptions Configure(LanguageServerOptions options) =>
        options
            .WithServices(services =>
            {
                services.AddSingleton<DocumentStore>();
                services.AddSingleton<DiagnosticsPublisher>();
            })
            .ConfigureLogging(logging =>
            {
                // Logs go to the client as window/logMessage (VS Code shows them in the Output panel), never to stdout.
                logging.AddLanguageProtocolLogging();
                logging.SetMinimumLevel(LogLevel.Warning);
            })
            .WithHandler<TextDocumentSyncHandler>();
}
