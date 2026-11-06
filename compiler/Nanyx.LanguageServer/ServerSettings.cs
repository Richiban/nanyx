using NyxCompiler;

namespace Nanyx.LanguageServer;

/// <summary>Choices fixed when the server starts, set by <c>nanyx lsp</c>'s options.</summary>
/// <param name="TypeVarNames">How hover names the type variables the compiler made up.</param>
/// <param name="TopLevelInlayHints">
/// Show the inferred types of top-level defs as inlay hints after their names, rather than in lenses above them.
/// </param>
public sealed record ServerSettings(TypeVarNames TypeVarNames, bool TopLevelInlayHints = false)
{
    public static ServerSettings Default { get; } = new(TypeVarNames.Greek);
}
