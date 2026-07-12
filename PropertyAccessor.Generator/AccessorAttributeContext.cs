using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Macaron.PropertyAccessor;

internal sealed record AccessorAttributeContext(
    IFieldSymbol Symbol,
    PropertyAccessorKind Kind,
    CSharpCompilation Compilation
);
