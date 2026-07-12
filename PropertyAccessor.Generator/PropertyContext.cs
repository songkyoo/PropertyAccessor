using Microsoft.CodeAnalysis;

namespace Macaron.PropertyAccessor;

public sealed record PropertyContext(
    PropertyAccessModifier AccessModifier,
    ITypeSymbol TypeSymbol,
    string Name,
    string FieldName,
    PropertyAccessorKind AccessorKind,
    bool IsInitAccessor,
    bool GetterRequiresExplicitConversion
);
