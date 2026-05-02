using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using static Macaron.PropertyAccessor.SourceGenerationHelpers;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFacts;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;
using static Microsoft.CodeAnalysis.SymbolDisplayMiscellaneousOptions;

namespace Macaron.PropertyAccessor;

[Generator]
public sealed class PropertyAccessorGenerator : IIncrementalGenerator
{
    #region Constants
    private const string AutoPropertyAttributeString = "Macaron.PropertyAccessor.AutoPropertyAttribute";
    private const string GetAttributeString = "Macaron.PropertyAccessor.GetAttribute";
    private const string GetSetAttributeString = "Macaron.PropertyAccessor.GetSetAttribute";
    #endregion

    #region Enums
    private enum PropertyAccessorKind
    {
        None,

        Get,
        GetSet
    }

    private enum DelegatedPropertyKind
    {
        None,

        ReadOnly,
        ReadWrite
    }
    #endregion

    #region Types
    private sealed record DelegatedPropertyTypes(
        INamedTypeSymbol? ReadOnlyProperty1,
        INamedTypeSymbol? ReadOnlyProperty2,
        INamedTypeSymbol? ReadWriteProperty1,
        INamedTypeSymbol? ReadWriteProperty2
    );

    private sealed record TypeContext(
        INamedTypeSymbol Symbol,
        PropertyAccessModifier AccessModifier,
        Regex Prefix,
        PropertyNamingRule NamingRule,
        DelegatedPropertyTypes DelegatedPropertyTypes
    );

    private sealed record PropertyContext(
        PropertyAccessModifier AccessModifier,
        ITypeSymbol TypeSymbol,
        string Name,
        string FieldName,
        PropertyAccessorKind AccessorKind,
        bool IsInitAccessor,
        bool IsDelegated
    );
    #endregion

    #region Static
    private static readonly DiagnosticDescriptor DelegatedPropertyMustBeReadonlyRule = new(
        id: "MPROP0001",
        title: "Delegated property fields must be readonly",
        messageFormat: "Field '{0}' must be marked readonly",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
    private static readonly DiagnosticDescriptor GetOrGetSetNotAllowedForDelegatedPropertyRule = new(
        id: "MPROP0002",
        title: "Get and GetSet are not allowed for delegated properties",
        messageFormat: "Field '{0}' must not use Get or GetSet because delegated properties are configured by interface type",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
    private static readonly DiagnosticDescriptor InvalidPropertyNameAfterPrefixRemovalRule = new(
        id: "MPROP0004",
        title: "Cannot generate property name after prefix removal",
        messageFormat: "Field '{0}' with prefix pattern '{1}' results in an empty property name",
        category: "Naming",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
    private static readonly DiagnosticDescriptor PropertyNameSameAsFieldNameRule = new(
        id: "MPROP0005",
        title: "Generated property name is same as field name",
        messageFormat: "Field '{0}' with prefix pattern '{1}' results in property name '{2}', which is the same as the field name",
        category: "Naming",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );
    private static readonly DiagnosticDescriptor InvalidPrefixPatternRule = new(
        id: "MPROP0006",
        title: "Invalid prefix pattern",
        messageFormat: "Prefix pattern '{0}' is not a valid regular expression",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
    private static readonly DiagnosticDescriptor StaticFieldNotSupportedRule = new(
        id: "MPROP0007",
        title: "Static fields are not supported",
        messageFormat: "Field '{0}' must not be static",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly Regex DefaultRegex = new(pattern: "^(_|m_)", RegexOptions.Compiled);

    private static ImmutableArray<(PropertyContext?, ImmutableArray<Diagnostic>)> GetPropertyContexts(
        TypeContext typeContext
    )
    {
        var (typeSymbol, accessModifier, prefix, namingRule, _) = typeContext;

        return typeSymbol
            .GetMembers()
            .OfType<IFieldSymbol>()
            .Select(symbol => GetGenerationContext(
                symbol,
                accessModifier,
                prefix,
                namingRule,
                typeContext.DelegatedPropertyTypes
            ))
            .ToImmutableArray();
    }

    private static (PropertyContext?, ImmutableArray<Diagnostic>) GetGenerationContext(
        IFieldSymbol fieldSymbol,
        PropertyAccessModifier accessModifier,
        Regex prefix,
        PropertyNamingRule namingRule,
        DelegatedPropertyTypes delegatedPropertyTypes
    )
    {
        var fieldName = fieldSymbol.Name;
        var typeSymbol = fieldSymbol.Type;
        var getAttribute = (AttributeData?)null;
        var getSetAttribute = (AttributeData?)null;
        var accessorKind = PropertyAccessorKind.None;
        var isDelegatedProperty = false;
        var diagnosticsBuilder = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (var attributeData in fieldSymbol.GetAttributes())
        {
            switch (attributeData.AttributeClass?.ToDisplayString())
            {
                case GetAttributeString:
                {
                    getAttribute = attributeData;
                    if (accessorKind == PropertyAccessorKind.None)
                    {
                        accessorKind = PropertyAccessorKind.Get;
                    }

                    break;
                }
                case GetSetAttributeString:
                {
                    getSetAttribute = attributeData;
                    accessorKind = PropertyAccessorKind.GetSet;
                    break;
                }
            }
        }

        var delegatedPropertyKind = GetDelegatedPropertyKind(typeSymbol, delegatedPropertyTypes);
        if (fieldSymbol.IsStatic && (accessorKind != PropertyAccessorKind.None || delegatedPropertyKind != DelegatedPropertyKind.None))
        {
            diagnosticsBuilder.Add(Diagnostic.Create(
                descriptor: StaticFieldNotSupportedRule,
                location: fieldSymbol.Locations.FirstOrDefault(),
                messageArgs: [fieldName]
            ));

            return (null, diagnosticsBuilder.ToImmutable());
        }

        if (delegatedPropertyKind == DelegatedPropertyKind.ReadOnly)
        {
            if (!fieldSymbol.IsReadOnly)
            {
                diagnosticsBuilder.Add(Diagnostic.Create(
                    descriptor: DelegatedPropertyMustBeReadonlyRule,
                    location: fieldSymbol.Locations.FirstOrDefault(),
                    messageArgs: [fieldName]
                ));

            }
            else
            {
                if (accessorKind != PropertyAccessorKind.None)
                {
                    diagnosticsBuilder.Add(Diagnostic.Create(
                        descriptor: GetOrGetSetNotAllowedForDelegatedPropertyRule,
                        location: (getSetAttribute ?? getAttribute)?.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
                        messageArgs: [fieldName]
                    ));
                }

                accessorKind = PropertyAccessorKind.Get;
                typeSymbol = GetPropertyTypeSymbol(typeSymbol);
                isDelegatedProperty = true;
            }
        }
        else if (delegatedPropertyKind == DelegatedPropertyKind.ReadWrite)
        {
            if (!fieldSymbol.IsReadOnly)
            {
                diagnosticsBuilder.Add(Diagnostic.Create(
                    descriptor: DelegatedPropertyMustBeReadonlyRule,
                    location: fieldSymbol.Locations.FirstOrDefault(),
                    messageArgs: [fieldName]
                ));
            }
            else
            {
                if (accessorKind != PropertyAccessorKind.None)
                {
                    diagnosticsBuilder.Add(Diagnostic.Create(
                        descriptor: GetOrGetSetNotAllowedForDelegatedPropertyRule,
                        location: (getSetAttribute ?? getAttribute)?.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
                        messageArgs: [fieldName]
                    ));
                }

                accessorKind = PropertyAccessorKind.GetSet;
                typeSymbol = GetPropertyTypeSymbol(typeSymbol);
                isDelegatedProperty = true;
            }
        }

        if (accessorKind == PropertyAccessorKind.None)
        {
            return (null, diagnosticsBuilder.ToImmutable());
        }

        var shapeAttribute = getSetAttribute ?? getAttribute;
        var explicitPropertyName = shapeAttribute?.ConstructorArguments[1].Value as string;

        var propertyName = !string.IsNullOrWhiteSpace(explicitPropertyName)
            ? explicitPropertyName!
            : GetPropertyName(fieldName, prefix, namingRule);

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

        return (
            new PropertyContext(
                AccessModifier: GetAccessModifier(
                    isDelegatedProperty ? null : shapeAttribute?.ConstructorArguments[0].Value,
                    accessModifier
                ),
                TypeSymbol: typeSymbol,
                Name: propertyName,
                FieldName: fieldName,
                AccessorKind: accessorKind,
                IsInitAccessor: !isDelegatedProperty && fieldSymbol.IsReadOnly,
                IsDelegated: isDelegatedProperty
            ),
            diagnosticsBuilder.ToImmutable()
        );

        #region Local Functions
        static DelegatedPropertyKind GetDelegatedPropertyKind(
            ITypeSymbol fieldTypeSymbol,
            DelegatedPropertyTypes delegatedPropertyTypes
        )
        {
            if (fieldTypeSymbol is not INamedTypeSymbol namedTypeSymbol)
            {
                return DelegatedPropertyKind.None;
            }

            var originalDefinition = namedTypeSymbol.OriginalDefinition;
            var comparer = SymbolEqualityComparer.Default;

            if (comparer.Equals(originalDefinition, delegatedPropertyTypes.ReadOnlyProperty1) ||
                comparer.Equals(originalDefinition, delegatedPropertyTypes.ReadOnlyProperty2)
            )
            {
                return DelegatedPropertyKind.ReadOnly;
            }

            if (comparer.Equals(originalDefinition, delegatedPropertyTypes.ReadWriteProperty1) ||
                comparer.Equals(originalDefinition, delegatedPropertyTypes.ReadWriteProperty2)
            )
            {
                return DelegatedPropertyKind.ReadWrite;
            }

            return DelegatedPropertyKind.None;
        }

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
            accessorKind,
            isInitAccessor,
            isDelegatedProperty
        ) = propertyContext;

        if (accessorKind == PropertyAccessorKind.None)
        {
            return ImmutableArray<string>.Empty;
        }

        var escapedFieldName = GetEscapedKeyword(fieldName);
        var escapedPropertyName = GetEscapedKeyword(propertyName);

        var builder = ImmutableArray.CreateBuilder<string>();

        builder.Add($"{GetAccessorModifier(accessModifier)} {typeSymbol.ToDisplayString(FullyQualifiedFormat.WithMiscellaneousOptions(IncludeNullableReferenceTypeModifier | UseSpecialTypes))} {escapedPropertyName}");
        builder.Add($"{{");

        if (accessorKind is PropertyAccessorKind.Get or PropertyAccessorKind.GetSet)
        {
            builder.Add($"{Indent}get => {escapedFieldName}{(isDelegatedProperty ? ".Get(this)" : "")};");
        }

        if (accessorKind == PropertyAccessorKind.GetSet)
        {
            builder.Add($"{Indent}{(isInitAccessor ? "init" : "set")} => {escapedFieldName}{(isDelegatedProperty ? ".Set(this, value)" : " = value")};");
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
        var isDefined = Enum.IsDefined(typeof(PropertyAccessModifier), accessModifier);

        return isDefined && accessModifier != PropertyAccessModifier.Default ? accessModifier : defaultValue;
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
        var isDefined = Enum.IsDefined(typeof(PropertyNamingRule), namingRule);

        return isDefined && namingRule != PropertyNamingRule.Default ? namingRule : defaultValue;
    }

    private static string GetEscapedKeyword(string keyword)
    {
        return GetKeywordKind(keyword) != SyntaxKind.None || GetContextualKeywordKind(keyword) != SyntaxKind.None
            ? "@" + keyword
            : keyword;
    }
    #endregion

    #region IIncrementalGenerator Interface
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<(TypeContext?, ImmutableArray<Diagnostic>)> valuesProvider = context
            .SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (syntaxNode, _) =>
                {
                    return syntaxNode
                        is TypeDeclarationSyntax { AttributeLists.Count: > 0 }
                        and (ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax);
                },
                transform: static (generatorSyntaxContext, _) =>
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
                            NamingRule: GetNamingRule(attributeSymbol.ConstructorArguments[2].Value),
                            DelegatedPropertyTypes: new DelegatedPropertyTypes(
                                ReadOnlyProperty1: semanticModel.Compilation.GetTypeByMetadataName("Macaron.PropertyAccessor.IReadOnlyProperty`1"),
                                ReadOnlyProperty2: semanticModel.Compilation.GetTypeByMetadataName("Macaron.PropertyAccessor.IReadOnlyProperty`2"),
                                ReadWriteProperty1: semanticModel.Compilation.GetTypeByMetadataName("Macaron.PropertyAccessor.IReadWriteProperty`1"),
                                ReadWriteProperty2: semanticModel.Compilation.GetTypeByMetadataName("Macaron.PropertyAccessor.IReadWriteProperty`2")
                            )
                        ),
                        diagnosticsBuilder.ToImmutable()
                    );
                }
            );

        context.RegisterSourceOutput(valuesProvider.Collect(), (sourceProductionContext, typeContexts) =>
        {
            var visitedTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

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

                if (!visitedTypes.Add(typeContext.Symbol))
                {
                    continue;
                }

                var builder = ImmutableArray.CreateBuilder<string>();

                foreach (var (propertyContext, fieldDiagnostics) in GetPropertyContexts(typeContext))
                {
                    foreach (var diagnostic in fieldDiagnostics)
                    {
                        sourceProductionContext.ReportDiagnostic(diagnostic);
                    }

                    if (propertyContext == null)
                    {
                        continue;
                    }

                    var lines = GenerateAccessorCode(propertyContext);
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
