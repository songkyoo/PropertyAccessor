namespace Macaron.PropertyAccessor;

internal sealed record PropertyModel(
    string FieldName,
    PropertyAccessModifier AccessModifier,
    string TypeName,
    string Name,
    PropertyAccessorKind AccessorKind,
    bool GetterRequiresExplicitConversion,
    bool IsInitAccessor,
    string? SetterMethodName
);
