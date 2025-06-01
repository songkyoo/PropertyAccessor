using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using static Macaron.PropertyAccessor.SourceGenerationHelpers;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;

namespace Macaron.PropertyAccessor;

[Generator]
public sealed class PropertyAccessorGenerator : IIncrementalGenerator
{
    #region Constants
    private const string AutoPropertyAttributeString = "Macaron.PropertyAccessor.AutoPropertyAttribute";
    private const string GetterAttributeString = "Macaron.PropertyAccessor.GetterAttribute";
    private const string SetterAttributeString = "Macaron.PropertyAccessor.SetterAttribute";
    private const string ReadOnlyPropertyString = "Macaron.PropertyAccessor.IReadOnlyProperty<";
    private const string ReadWritePropertyString = "Macaron.PropertyAccessor.IReadWriteProperty<";
    #endregion

    #region Types
    private sealed record TypeContext(
        INamedTypeSymbol Symbol,
        PropertyAccessModifier AccessModifier,
        Regex Prefix,
        PropertyNamingRule NamingRule
    );

    private sealed record PropertyContext(
        PropertyAccessModifier AccessModifier,
        ITypeSymbol TypeSymbol,
        string Name,
        string FieldName,
        bool HasGetter,
        bool HasSetter,
        bool IsDelegated
    );
    #endregion

    #region Static
    private static readonly DiagnosticDescriptor ReadonlyFieldWithSetterAttributeRule = new(
        id: "PA0001",
        title: "SetterAttribute cannot be applied to readonly fields",
        messageFormat: "Field '{0}' is marked readonly but has Setter attribute",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
    private static readonly DiagnosticDescriptor DelegatedPropertyMustBeReadonlyRule = new(
        id: "PA0002",
        title: "Delegated property fields must be readonly",
        messageFormat: "Field '{0}' must be marked readonly",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
    private static readonly DiagnosticDescriptor GetterRedundantForDelegatedPropertyRule = new(
        id: "PA0003",
        title: "Getter is not allowed for delegated properties",
        messageFormat: "Getter on field '{0}' is not allowed",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
    private static readonly DiagnosticDescriptor SetterRedundantForDelegatedPropertyRule = new(
        id: "PA0004",
        title: "Setter is not allowed for delegated properties",
        messageFormat: "Setter on field '{0}' is not allowed",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
    private static readonly DiagnosticDescriptor InvalidPropertyNameAfterPrefixRemovalRule = new(
        id: "PA0005",
        title: "Cannot generate property name after prefix removal",
        messageFormat: "Field '{0}' with prefix pattern '{1}' results in an empty property name",
        category: "Naming",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
    private static readonly DiagnosticDescriptor PropertyNameSameAsFieldNameRule = new(
        id: "PA0006",
        title: "Generated property name is same as field name",
        messageFormat: "Field '{0}' with prefix pattern '{1}' results in property name '{2}' which is same as field name",
        category: "Naming",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );
    private static readonly DiagnosticDescriptor InvalidPrefixPatternRule = new(
        id: "PA0007",
        title: "Invalid prefix pattern",
        messageFormat: "Prefix pattern '{0}' is not a valid regular expression",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly Regex DefaultRegex = new(pattern: "^(_|m_)", RegexOptions.Compiled);

    private static ImmutableArray<(PropertyContext?, ImmutableArray<Diagnostic>)> GetFieldContexts(
        TypeContext typeContext
    )
    {
        var (typeSymbol, accessModifier, prefix, namingRule) = typeContext;

        return typeSymbol
            .GetMembers()
            .OfType<IFieldSymbol>()
            .Select(symbol => GetGenerationContext(symbol, accessModifier, prefix, namingRule))
            .ToImmutableArray();
    }

    private static (PropertyContext?, ImmutableArray<Diagnostic>) GetGenerationContext(
        IFieldSymbol fieldSymbol,
        PropertyAccessModifier accessModifier,
        Regex prefix,
        PropertyNamingRule namingRule
    )
    {
        var fieldName = fieldSymbol.Name;
        var typeSymbol = fieldSymbol.Type;
        var autoPropertyAttribute = (AttributeData?)null;
        var getterAttribute = (AttributeData?)null;
        var setterAttribute = (AttributeData?)null;
        var hasGetter = false;
        var hasSetter = false;
        var isDelegatedProperty = false;
        var diagnosticsBuilder = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (var attributeData in fieldSymbol.GetAttributes())
        {
            switch (attributeData.AttributeClass?.ToDisplayString())
            {
                case AutoPropertyAttributeString:
                {
                    autoPropertyAttribute = attributeData;
                    break;
                }
                case GetterAttributeString:
                {
                    getterAttribute = attributeData;
                    hasGetter = true;
                    break;
                }
                case SetterAttributeString:
                {
                    setterAttribute = attributeData;
                    hasSetter = true;
                    break;
                }
            }
        }

        var prefixArgument = autoPropertyAttribute?.ConstructorArguments[1].Value;
        var memberLevelPrefix = GetPrefix(prefixArgument, prefix);

        if (memberLevelPrefix == null)
        {
            diagnosticsBuilder.Add(Diagnostic.Create(
                descriptor: InvalidPrefixPatternRule,
                location: autoPropertyAttribute?.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
                messageArgs: [prefixArgument]
            ));

            return (null, diagnosticsBuilder.ToImmutable());
        }

        var propertyName = GetPropertyName(
            fieldName: fieldName,
            prefix: memberLevelPrefix,
            namingRule: GetNamingRule(autoPropertyAttribute?.ConstructorArguments[2].Value, namingRule)
        );

        if (propertyName.Length < 1)
        {
            diagnosticsBuilder.Add(Diagnostic.Create(
                descriptor: InvalidPropertyNameAfterPrefixRemovalRule,
                location: fieldSymbol.Locations.FirstOrDefault(),
                messageArgs: [fieldName, prefix]
            ));

            return (null, diagnosticsBuilder.ToImmutable());
        }

        if (propertyName == fieldName)
        {
            diagnosticsBuilder.Add(Diagnostic.Create(
                descriptor: PropertyNameSameAsFieldNameRule,
                location: fieldSymbol.Locations.FirstOrDefault(),
                messageArgs: [fieldName, prefix, propertyName]
            ));

            return (null, diagnosticsBuilder.ToImmutable());
        }

        var fieldTypeSymbolString = typeSymbol.ToDisplayString();
        if (fieldTypeSymbolString.StartsWith(ReadOnlyPropertyString))
        {
            if (!fieldSymbol.IsReadOnly)
            {
                diagnosticsBuilder.Add(Diagnostic.Create(
                    descriptor: DelegatedPropertyMustBeReadonlyRule,
                    location: fieldSymbol.Locations.FirstOrDefault(),
                    messageArgs: [fieldName]
                ));

                hasGetter = false;
            }
            else
            {
                if (hasGetter)
                {
                    diagnosticsBuilder.Add(Diagnostic.Create(
                        descriptor: GetterRedundantForDelegatedPropertyRule,
                        location: getterAttribute!.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
                        messageArgs: [fieldName]
                    ));
                }
                else
                {
                    hasGetter = true;
                }

                typeSymbol = GetPropertyTypeSymbol(typeSymbol);
                isDelegatedProperty = true;
            }
        }
        else if (fieldTypeSymbolString.StartsWith(ReadWritePropertyString))
        {
            if (!fieldSymbol.IsReadOnly)
            {
                diagnosticsBuilder.Add(Diagnostic.Create(
                    descriptor: DelegatedPropertyMustBeReadonlyRule,
                    location: fieldSymbol.Locations.FirstOrDefault(),
                    messageArgs: [fieldName]
                ));

                hasGetter = false;
                hasSetter = false;
            }
            else
            {
                if (hasGetter)
                {
                    diagnosticsBuilder.Add(Diagnostic.Create(
                        descriptor: GetterRedundantForDelegatedPropertyRule,
                        location: getterAttribute!.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
                        messageArgs: [fieldName]
                    ));
                }
                else
                {
                    hasGetter = true;
                }

                if (hasSetter)
                {
                    diagnosticsBuilder.Add(Diagnostic.Create(
                        descriptor: SetterRedundantForDelegatedPropertyRule,
                        location: setterAttribute!.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
                        messageArgs: [fieldName]
                    ));
                }
                else
                {
                    hasSetter = true;
                }

                typeSymbol = GetPropertyTypeSymbol(typeSymbol);
                isDelegatedProperty = true;
            }
        }
        else
        {
            if (fieldSymbol.IsReadOnly && hasSetter)
            {
                diagnosticsBuilder.Add(Diagnostic.Create(
                    descriptor: ReadonlyFieldWithSetterAttributeRule,
                    location: fieldSymbol.Locations.FirstOrDefault(),
                    messageArgs: [fieldName]
                ));

                hasSetter = false;
            }
        }

        if (hasGetter || hasSetter)
        {
            return (
                new PropertyContext(
                    AccessModifier: GetAccessModifier(
                        autoPropertyAttribute?.ConstructorArguments[0].Value,
                        accessModifier
                    ),
                    TypeSymbol: typeSymbol,
                    Name: propertyName,
                    FieldName: fieldName,
                    HasGetter: hasGetter,
                    HasSetter: hasSetter,
                    IsDelegated: isDelegatedProperty
                ),
                diagnosticsBuilder.ToImmutable()
            );
        }
        else
        {
            return (null, diagnosticsBuilder.ToImmutable());
        }

        #region Local Functions
        static ITypeSymbol GetPropertyTypeSymbol(ITypeSymbol fieldTypeSymbol)
        {
            return ((INamedTypeSymbol)fieldTypeSymbol).TypeArguments switch
            {
                [var propertyType] => propertyType,
                [_, var propertyType] => propertyType,
                _ => throw new InvalidOperationException($"Invalid field type: {fieldTypeSymbol}"),
            };
        }

        static string GetPropertyName(string fieldName, Regex prefix, PropertyNamingRule namingRule)
        {
            var prefixRemovedName = prefix.Replace(input: fieldName, replacement: "", count: 1);
            if (prefixRemovedName.Length < 1)
            {
                return "";
            }

            return namingRule switch
            {
                PropertyNamingRule.PascalCase => char.ToUpperInvariant(prefixRemovedName[0]) + prefixRemovedName[1..],
                PropertyNamingRule.CamelCase => char.ToLowerInvariant(prefixRemovedName[0]) + prefixRemovedName[1..],
                _ => throw new InvalidOperationException($"Invalid naming rule: {namingRule}")
            };
        }
        #endregion
    }

    private static ImmutableArray<string> GenerateAccessorCode(PropertyContext propertyContext)
    {
        var (
            accessModifier,
            typeSymbol,
            propertyName,
            fieldName,
            hasGetter,
            hasSetter,
            isDelegatedProperty
        ) = propertyContext;

        if (!hasGetter && !hasSetter)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>();

        builder.Add($"{GetAccessorModifier(accessModifier)} {typeSymbol.ToDisplayString(FullyQualifiedFormat)} {propertyName}");
        builder.Add($"{{");

        if (hasGetter)
        {
            builder.Add($"{Space}get => {fieldName}{(isDelegatedProperty ? ".Get(this)" : "")};");
        }

        if (hasSetter)
        {
            builder.Add($"{Space}set => {fieldName}{(isDelegatedProperty ? ".Set(this, value)" : " = value")};");
        }

        builder.Add($"}}");

        return builder.ToImmutable();

        #region Local Functions
        static string GetAccessorModifier(PropertyAccessModifier accessModifier)
        {
            return accessModifier switch
            {
                PropertyAccessModifier.Public => "public",
                PropertyAccessModifier.Protected => "protected",
                PropertyAccessModifier.Internal => "internal",
                PropertyAccessModifier.Private => "private",
                PropertyAccessModifier.ProtectedInternal => "protected internal",
                PropertyAccessModifier.PrivateProtected => "private protected",
                PropertyAccessModifier.File => "file",
                _ => throw new InvalidOperationException($"Invalid access modifier: {accessModifier}")
            };
        }
        #endregion
    }

    private static PropertyAccessModifier GetAccessModifier(
        object? value,
        PropertyAccessModifier defaultValue = PropertyAccessModifier.Public
    )
    {
        if (value == null)
        {
            return defaultValue;
        }

        var accessModifier = (PropertyAccessModifier)value;
        return accessModifier != PropertyAccessModifier.Default ? accessModifier : defaultValue;
    }

    private static Regex? GetPrefix(object? value, Regex? defaultValue = null)
    {
        try
        {
            return value is string stringValue && !stringValue.AsSpan().Trim().IsEmpty
                ? new Regex($"{(stringValue[0] == '^' ? "" : "^")}{stringValue}")
                : defaultValue ?? DefaultRegex;
        }
        catch
        {
            return null;
        }
    }

    private static PropertyNamingRule GetNamingRule(
        object? value,
        PropertyNamingRule defaultValue = PropertyNamingRule.PascalCase
    )
    {
        if (value == null)
        {
            return defaultValue;
        }

        var namingRule = (PropertyNamingRule)value;
        return namingRule != PropertyNamingRule.Default ? namingRule : defaultValue;
    }
    #endregion

    #region IIncrementalGenerator Interface
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var visitedTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        IncrementalValuesProvider<(TypeContext?, ImmutableArray<Diagnostic>)> valuesProvider = context
            .SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (syntaxNode, _) => syntaxNode
                    is ClassDeclarationSyntax
                    or StructDeclarationSyntax
                    or RecordDeclarationSyntax
                    {
                        AttributeLists.Count: > 0
                    },
                transform: (generatorSyntaxContext, _) =>
                {
                    var diagnosticsBuilder = ImmutableArray.CreateBuilder<Diagnostic>();

                    var semanticModel = generatorSyntaxContext.SemanticModel;
                    if (semanticModel.GetDeclaredSymbol(generatorSyntaxContext.Node) is not INamedTypeSymbol typeSymbol)
                    {
                        return ((TypeContext?)null, diagnosticsBuilder.ToImmutable());
                    }

                    var attributeSymbol = typeSymbol
                        .GetAttributes()
                        .FirstOrDefault(attributeData =>
                        {
                            return attributeData.AttributeClass?.ToDisplayString() == AutoPropertyAttributeString;
                        });
                    if (attributeSymbol == null)
                    {
                        return ((TypeContext?)null, diagnosticsBuilder.ToImmutable());
                    }

                    if (!visitedTypes.Add(typeSymbol))
                    {
                        return ((TypeContext?)null, diagnosticsBuilder.ToImmutable());
                    }

                    var prefixArgument = attributeSymbol.ConstructorArguments[1].Value;
                    var typeLevelPrefix = GetPrefix(prefixArgument);

                    if (typeLevelPrefix == null)
                    {
                        diagnosticsBuilder.Add(Diagnostic.Create(
                            descriptor: InvalidPrefixPatternRule,
                            location: attributeSymbol.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
                            messageArgs: [prefixArgument]
                        ));

                        return ((TypeContext?)null, diagnosticsBuilder.ToImmutable());
                    }

                    return (
                        new TypeContext(
                            Symbol: typeSymbol,
                            AccessModifier: GetAccessModifier(attributeSymbol.ConstructorArguments[0].Value),
                            Prefix: typeLevelPrefix,
                            NamingRule: GetNamingRule(attributeSymbol.ConstructorArguments[2].Value)
                        ),
                        diagnosticsBuilder.ToImmutable()
                    );
                }
            );

        context.RegisterSourceOutput(valuesProvider.Collect(), (sourceProductionContext, typeContexts) =>
        {
            foreach (var (typeContext, typeDiagnostics) in typeContexts)
            {
                foreach (var diagnostic in typeDiagnostics)
                {
                    sourceProductionContext.ReportDiagnostic(diagnostic);
                }

                if (typeContext == null)
                {
                    continue;
                }

                var builder = ImmutableArray.CreateBuilder<string>();

                foreach (var (fieldContext, fieldDiagnostics) in GetFieldContexts(typeContext))
                {
                    foreach (var diagnostic in fieldDiagnostics)
                    {
                        sourceProductionContext.ReportDiagnostic(diagnostic);
                    }

                    if (fieldContext == null)
                    {
                        continue;
                    }

                    var lines = GenerateAccessorCode(fieldContext);
                    if (lines.IsEmpty)
                    {
                        continue;
                    }

                    if (builder.Count > 0)
                    {
                        builder.Add("");
                    }

                    builder.AddRange(lines);
                }

                AddSource(
                    context: sourceProductionContext,
                    typeSymbol: typeContext.Symbol,
                    lines: builder.ToImmutable()
                );
            }
        });
    }
    #endregion
}
