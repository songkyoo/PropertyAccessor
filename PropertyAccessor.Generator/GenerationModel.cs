using System.Collections.Immutable;

namespace Macaron.PropertyAccessor;

internal sealed record GenerationModel(
    string HintName,
    TypeGenerationModel Type,
    ImmutableArray<PropertyModel> Properties
);
