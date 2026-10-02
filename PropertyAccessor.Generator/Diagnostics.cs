using Microsoft.CodeAnalysis;

namespace Macaron.PropertyAccessor;

public sealed class Diagnostics
{
    public static readonly DiagnosticDescriptor InvalidPropertyNameAfterPrefixRemovalRule = new(
        id: "MPROP0001",
        title: "Cannot generate property name after prefix removal",
        messageFormat: "Field '{0}' with prefix pattern '{1}' results in an empty property name",
        category: "Naming",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor PropertyNameSameAsFieldNameRule = new(
        id: "MPROP0002",
        title: "Generated property name is same as field name",
        messageFormat: "Field '{0}' with prefix pattern '{1}' results in property name '{2}', which is the same as the field name",
        category: "Naming",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor InvalidPrefixPatternRule = new(
        id: "MPROP0003",
        title: "Invalid prefix pattern",
        messageFormat: "Prefix pattern '{0}' is not a valid regular expression",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor StaticFieldNotSupportedRule = new(
        id: "MPROP0004",
        title: "Static fields are not supported",
        messageFormat: "Field '{0}' must not be static",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor InvalidGetterConversionRule = new(
        id: "MPROP0005",
        title: "Field type cannot be converted to property type",
        messageFormat: "Field '{0}' of type '{1}' cannot be cast to property type '{2}'",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor ConflictingAccessorAttributesRule = new(
        id: "MPROP0006",
        title: "Get and GetSet cannot be used together",
        messageFormat: "Field '{0}' cannot declare both [Get] and [GetSet]",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor InvalidSetterMethodNameRule = new(
        id: "MPROP0007",
        title: "Invalid setter method name",
        messageFormat: "Field '{0}' has an invalid setter method name '{1}'",
        category: "Naming",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor ReadonlySetterMethodRule = new(
        id: "MPROP0008",
        title: "Cannot generate setter method for readonly field",
        messageFormat: "Field '{0}' is readonly and cannot be assigned by setter method '{1}'",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor ConflictingSetterMethodRule = new(
        id: "MPROP0009",
        title: "Setter method conflicts with another member",
        messageFormat: "Setter method '{0}' for field '{1}' conflicts with another member",
        category: "Naming",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
}
