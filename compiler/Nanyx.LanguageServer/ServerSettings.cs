using NyxCompiler;

namespace Nanyx.LanguageServer;

/// <summary>Choices fixed when the server starts, set by <c>nanyx lsp</c>'s options.</summary>
/// <param name="TypeVarNames">How hover names the type variables the compiler made up.</param>
/// <param name="TopLevelTypeHints">Where to show the inferred types of top-level defs that do not write theirs.</param>
public sealed record ServerSettings(TypeVarNames TypeVarNames, TopLevelTypeHints TopLevelTypeHints = TopLevelTypeHints.Lens)
{
    public static ServerSettings Default { get; } = new(TypeVarNames.Greek);
}

/// <summary>Where the inferred type of a top-level def that does not write its own is shown.</summary>
public enum TopLevelTypeHints
{
    /// <summary>In a lens above the def.</summary>
    Lens,

    /// <summary>In an inlay hint after the def's name, as for locals and parameters.</summary>
    Inlay,

    /// <summary>Nowhere (hover still shows it).</summary>
    Off
}
