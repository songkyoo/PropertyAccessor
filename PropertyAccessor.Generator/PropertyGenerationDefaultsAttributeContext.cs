using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Macaron.PropertyAccessor;

internal sealed record PropertyGenerationDefaultsAttributeContext(
    INamedTypeSymbol Symbol,
    ImmutableArray<AttributeData> Attributes,
    CSharpCompilation Compilation
);
