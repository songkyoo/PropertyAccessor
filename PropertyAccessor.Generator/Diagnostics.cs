using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

namespace Macaron.PropertyAccessor;

internal static class Diagnostics
{
    private static readonly DiagnosticDescriptor TypeMustBePartialRule = new(
        id: "MAPA0001",
        title: "Type must be partial",
        messageFormat: "Type '{0}' must be partial to generate properties.",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor InvalidPropertyNameAfterPrefixRemovalRule = new(
        id: "MAPA0002",
        title: "Cannot generate property name after prefix removal",
        messageFormat: "Field '{0}' with prefix pattern '{1}' results in an empty property name.",
        category: "Naming",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor PropertyNameSameAsFieldNameRule = new(
        id: "MAPA0003",
        title: "Generated property name is same as field name",
        messageFormat: "Field '{0}' with prefix pattern '{1}' results in property name '{2}', which is the same as the field name.",
        category: "Naming",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor InvalidPrefixPatternRule = new(
        id: "MAPA0004",
        title: "Invalid prefix pattern",
        messageFormat: "Prefix pattern '{0}' is not a valid regular expression.",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor StaticFieldNotSupportedRule = new(
        id: "MAPA0005",
        title: "Static fields are not supported",
        messageFormat: "Field '{0}' must not be static.",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor InvalidGetterConversionRule = new(
        id: "MAPA0006",
        title: "Field type cannot be converted to property type",
        messageFormat: "Field '{0}' of type '{1}' cannot be cast to property type '{2}'.",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor ConflictingAccessorAttributesRule = new(
        id: "MAPA0007",
        title: "Get and GetSet cannot be used together",
        messageFormat: "Field '{0}' cannot declare both [Get] and [GetSet].",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor InvalidSetterMethodNameRule = new(
        id: "MAPA0008",
        title: "Invalid setter method name",
        messageFormat: "Field '{0}' has an invalid setter method name '{1}'.",
        category: "Naming",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor ReadonlySetterMethodRule = new(
        id: "MAPA0009",
        title: "Cannot generate setter method for readonly field",
        messageFormat: "Field '{0}' is readonly and cannot be assigned by setter method '{1}'.",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor ConflictingSetterMethodRule = new(
        id: "MAPA0010",
        title: "Setter method conflicts with another member",
        messageFormat: "Setter method '{0}' for field '{1}' conflicts with another member.",
        category: "Naming",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor IncompatiblePropertyOverrideRule = new(
        id: "MAPA0011",
        title: "Generated property cannot override inherited property",
        messageFormat: "Generated property '{0}' for field '{1}' cannot override '{2}': {3}.",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor InvalidProjectOptionRule = new(
        id: "MAPA0012",
        title: "Invalid project generation option",
        messageFormat: "Project generation option '{0}' has an invalid value '{1}'.",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static Diagnostic TypeMustBePartial(string typeName, Location location)
    {
        return Diagnostic.Create(TypeMustBePartialRule, location, messageArgs: [typeName]);
    }

    public static Diagnostic InvalidPropertyNameAfterPrefixRemoval(IFieldSymbol field, Regex? prefixRegex)
    {
        return Diagnostic.Create(
            InvalidPropertyNameAfterPrefixRemovalRule,
            field.Locations.FirstOrDefault(),
            messageArgs: [field.Name, prefixRegex]
        );
    }

    public static Diagnostic PropertyNameSameAsFieldName(IFieldSymbol field, Regex? prefixRegex, string propertyName)
    {
        return Diagnostic.Create(
            PropertyNameSameAsFieldNameRule,
            field.Locations.FirstOrDefault(),
            messageArgs: [field.Name, prefixRegex, propertyName]
        );
    }

    public static Diagnostic InvalidPrefixPattern(object? prefixPatternValue, Location? location)
    {
        return Diagnostic.Create(InvalidPrefixPatternRule, location, messageArgs: [prefixPatternValue]);
    }

    public static Diagnostic StaticFieldNotSupported(IFieldSymbol field)
    {
        return Diagnostic.Create(
            StaticFieldNotSupportedRule,
            field.Locations.FirstOrDefault(),
            messageArgs: [field.Name]
        );
    }

    public static Diagnostic InvalidGetterConversion(
        IFieldSymbol field,
        ITypeSymbol propertyType,
        Location? attributeLocation
    )
    {
        return Diagnostic.Create(
            InvalidGetterConversionRule,
            attributeLocation,
            messageArgs:
            [
                field.Name,
                field.Type.ToDisplayString(MinimallyQualifiedFormat),
                propertyType.ToDisplayString(MinimallyQualifiedFormat)
            ]
        );
    }

    public static Diagnostic ConflictingAccessorAttributes(IFieldSymbol field)
    {
        return Diagnostic.Create(
            ConflictingAccessorAttributesRule,
            field.Locations.FirstOrDefault(),
            messageArgs: [field.Name]
        );
    }

    public static Diagnostic InvalidSetterMethodName(IFieldSymbol field, object? setterNameValue)
    {
        return Diagnostic.Create(
            InvalidSetterMethodNameRule,
            field.Locations.FirstOrDefault(),
            messageArgs: [field.Name, setterNameValue]
        );
    }

    public static Diagnostic ReadonlySetterMethod(IFieldSymbol field, string setterName)
    {
        return Diagnostic.Create(
            ReadonlySetterMethodRule,
            field.Locations.FirstOrDefault(),
            messageArgs: [field.Name, setterName]
        );
    }

    public static Diagnostic ConflictingSetterMethod(IFieldSymbol field, string setterName)
    {
        return Diagnostic.Create(
            ConflictingSetterMethodRule,
            field.Locations.FirstOrDefault(),
            messageArgs: [setterName, field.Name]
        );
    }

    public static Diagnostic IncompatiblePropertyOverride(
        IFieldSymbol field,
        string propertyName,
        IPropertySymbol baseProperty,
        string mismatchReason
    )
    {
        return Diagnostic.Create(
            IncompatiblePropertyOverrideRule,
            field.Locations.FirstOrDefault(),
            messageArgs:
            [
                propertyName,
                field.Name,
                baseProperty.ToDisplayString(CSharpErrorMessageFormat),
                mismatchReason
            ]
        );
    }

    public static Diagnostic InvalidProjectOption(string key, string value)
    {
        return Diagnostic.Create(InvalidProjectOptionRule, Location.None, messageArgs: [key, value]);
    }
}
