using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Macaron.PropertyAccessor;

internal sealed record PropertyGenerationDefaultsAttributeContext(
    INamedTypeSymbol Symbol,
    ImmutableArray<AttributeData> Attributes
);
