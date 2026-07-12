namespace Macaron.PropertyAccessor;

internal sealed record PropertyModel(
    PropertyAccessModifier AccessModifier,
    string TypeName,
    string Name,
    string FieldName,
    PropertyAccessorKind AccessorKind,
    bool IsInitAccessor,
    bool GetterRequiresExplicitConversion
);
