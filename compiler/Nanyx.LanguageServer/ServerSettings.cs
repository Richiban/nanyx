using NyxCompiler;

namespace Nanyx.LanguageServer;

/// <summary>Choices fixed when the server starts, set by <c>nanyx lsp</c>'s options.</summary>
/// <param name="TypeVarNames">How hover names the type variables the compiler made up.</param>
public sealed record ServerSettings(TypeVarNames TypeVarNames)
{
    public static ServerSettings Default { get; } = new(TypeVarNames.Greek);
}
