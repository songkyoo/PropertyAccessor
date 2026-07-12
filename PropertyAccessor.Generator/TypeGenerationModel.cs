using System.Collections.Immutable;

namespace Macaron.PropertyAccessor;

internal sealed record TypeGenerationModel(
    string NamespaceName,
    ImmutableArray<string> Declarations
);
