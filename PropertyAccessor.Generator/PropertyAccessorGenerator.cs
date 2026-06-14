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
    #endregion

    #region Types
    private sealed record TypeContext(
        INamedTypeSymbol Symbol,
        PropertyAccessModifier AccessModifier,
        Regex Prefix,
        PropertyNamingRule NamingRule,
        CSharpCompilation Compilation
    );

    private sealed record PropertyContext(
        PropertyAccessModifier AccessModifier,
        ITypeSymbol TypeSymbol,
        string Name,
        string FieldName,
        PropertyAccessorKind AccessorKind,
        bool IsInitAccessor,
        bool GetterRequiresExplicitConversion
    );
    #endregion

    #region Static
    private static readonly DiagnosticDescriptor InvalidPropertyNameAfterPrefixRemovalRule = new(
        id: "MPROP0001",
        title: "Cannot generate property name after prefix removal",
        messageFormat: "Field '{0}' with prefix pattern '{1}' results in an empty property name",
        category: "Naming",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
    private static readonly DiagnosticDescriptor PropertyNameSameAsFieldNameRule = new(
        id: "MPROP0002",
        title: "Generated property name is same as field name",
        messageFormat: "Field '{0}' with prefix pattern '{1}' results in property name '{2}', which is the same as the field name",
        category: "Naming",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );
    private static readonly DiagnosticDescriptor InvalidPrefixPatternRule = new(
        id: "MPROP0003",
        title: "Invalid prefix pattern",
        messageFormat: "Prefix pattern '{0}' is not a valid regular expression",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
    private static readonly DiagnosticDescriptor StaticFieldNotSupportedRule = new(
        id: "MPROP0004",
        title: "Static fields are not supported",
        messageFormat: "Field '{0}' must not be static",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
    private static readonly DiagnosticDescriptor InvalidGetterConversionRule = new(
        id: "MPROP0005",
        title: "Field type cannot be converted to property type",
        messageFormat: "Field '{0}' of type '{1}' cannot be cast to property type '{2}'",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly Regex DefaultRegex = new(pattern: "^(_|m_)", RegexOptions.Compiled);

    private static ImmutableArray<(PropertyContext?, ImmutableArray<Diagnostic>)> GetPropertyContexts(
        TypeContext typeContext
    )
    {
        var (typeSymbol, accessModifier, prefix, namingRule, compilation) = typeContext;

        return typeSymbol
            .GetMembers()
            .OfType<IFieldSymbol>()
            .Where(HasAccessorAttribute)
            .Select(symbol => GetGenerationContext(
                symbol,
                accessModifier,
                prefix,
                namingRule,
                compilation
            ))
            .ToImmutableArray();

        #region Local Functions
        static bool HasAccessorAttribute(IFieldSymbol fieldSymbol)
        {
            return fieldSymbol.GetAttributes().Any(attributeData =>
            {
                var attributeName = attributeData.AttributeClass?.ToDisplayString();

                return attributeName is GetAttributeString or GetSetAttributeString;
            });
        }
        #endregion
    }

    private static (PropertyContext?, ImmutableArray<Diagnostic>) GetGenerationContext(
        IFieldSymbol fieldSymbol,
        PropertyAccessModifier accessModifier,
        Regex prefix,
        PropertyNamingRule namingRule,
        CSharpCompilation compilation
    )
    {
        var fieldName = fieldSymbol.Name;
        var fieldTypeSymbol = fieldSymbol.Type;

        var getAttribute = (AttributeData?)null;
        var getSetAttribute = (AttributeData?)null;
        var accessorKind = PropertyAccessorKind.None;
        var getterRequiresExplicitConversion = false;

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

        if (fieldSymbol.IsStatic && accessorKind != PropertyAccessorKind.None)
        {
            diagnosticsBuilder.Add(Diagnostic.Create(
                descriptor: StaticFieldNotSupportedRule,
                location: fieldSymbol.Locations.FirstOrDefault(),
                messageArgs: [fieldName]
            ));

            return (null, diagnosticsBuilder.ToImmutable());
        }

        var typeSymbol = fieldTypeSymbol;

        if (accessorKind == PropertyAccessorKind.None)
        {
            return (null, diagnosticsBuilder.ToImmutable());
        }

        var shapeAttribute = getSetAttribute ?? getAttribute;
        if (accessorKind == PropertyAccessorKind.Get &&
            getAttribute is { ConstructorArguments.Length: > 0 } &&
            getAttribute.ConstructorArguments[0].Value is ITypeSymbol propertyTypeSymbol
        )
        {
            var diagnosticLocation = getAttribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();
            var getterConversion = compilation.ClassifyConversion(fieldTypeSymbol, propertyTypeSymbol);

            if (!getterConversion.Exists)
            {
                diagnosticsBuilder.Add(Diagnostic.Create(
                    descriptor: InvalidGetterConversionRule,
                    location: diagnosticLocation,
                    messageArgs:
                    [
                        fieldName,
                        fieldTypeSymbol.ToDisplayString(MinimallyQualifiedFormat),
                        propertyTypeSymbol.ToDisplayString(MinimallyQualifiedFormat)
                    ]
                ));

                return (null, diagnosticsBuilder.ToImmutable());
            }

            typeSymbol = propertyTypeSymbol;
            getterRequiresExplicitConversion = !getterConversion.IsImplicit;
        }

        var usesGetAttribute = ReferenceEquals(shapeAttribute, getAttribute);
        var explicitPropertyName = GetConstructorArgumentValue(shapeAttribute, usesGetAttribute ? 2 : 1) as string;
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
                    GetConstructorArgumentValue(shapeAttribute, usesGetAttribute ? 1 : 0),
                    accessModifier
                ),
                TypeSymbol: typeSymbol,
                Name: propertyName,
                FieldName: fieldName,
                AccessorKind: accessorKind,
                IsInitAccessor: fieldSymbol.IsReadOnly,
                GetterRequiresExplicitConversion: getterRequiresExplicitConversion
            ),
            diagnosticsBuilder.ToImmutable()
        );

        #region Local Functions
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
                _ => throw new InvalidOperationException($"Invalid naming rule: {namingRule}"),
            };
        }

        static object? GetConstructorArgumentValue(AttributeData? attributeData, int index)
        {
            var constructorArguments = attributeData?.ConstructorArguments;

            return constructorArguments is { Length: > 0 and var length } && index < length
                ? constructorArguments.Value[index].Value
                : null;
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
            getterRequiresExplicitConversion
        ) = propertyContext;

        if (accessorKind == PropertyAccessorKind.None)
        {
            return ImmutableArray<string>.Empty;
        }

        var escapedFieldName = GetEscapedKeyword(fieldName);
        var escapedPropertyName = GetEscapedKeyword(propertyName);
        var propertyTypeName = typeSymbol.ToDisplayString(FullyQualifiedFormat.WithMiscellaneousOptions(
            IncludeNullableReferenceTypeModifier | UseSpecialTypes
        ));

        var builder = ImmutableArray.CreateBuilder<string>();

        builder.Add($"{GetAccessorModifier(accessModifier)} {propertyTypeName} {escapedPropertyName}");
        builder.Add($"{{");

        if (accessorKind is PropertyAccessorKind.Get or PropertyAccessorKind.GetSet)
        {
            var getterExpression = escapedFieldName;

            if (getterRequiresExplicitConversion)
            {
                getterExpression = $"({propertyTypeName}){getterExpression}";
            }

            builder.Add($"{Indent}get => {getterExpression};");
        }

        if (accessorKind == PropertyAccessorKind.GetSet)
        {
            builder.Add($"{Indent}{(isInitAccessor ? "init" : "set")} => {escapedFieldName} = value;");
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
                _ => throw new InvalidOperationException($"Invalid access modifier: {accessModifier}"),
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
                           is TypeDeclarationSyntax typeDeclaration
                           and (ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax)
                           && (
                               typeDeclaration.AttributeLists.Count > 0 ||
                               typeDeclaration
                                   .Members
                                   .OfType<FieldDeclarationSyntax>()
                                   .Any(fieldDeclaration => fieldDeclaration.AttributeLists.Count > 0)
                           );
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

                    var hasAccessorField = typeSymbol
                        .GetMembers()
                        .OfType<IFieldSymbol>()
                        .Any(fieldSymbol => fieldSymbol.GetAttributes().Any(attributeData =>
                        {
                            var attributeName = attributeData.AttributeClass?.ToDisplayString();

                            return attributeName is GetAttributeString or GetSetAttributeString;
                        }));

                    if (!hasAccessorField)
                    {
                        return ((TypeContext?)null, diagnosticsBuilder.ToImmutable());
                    }

                    var prefixArgument = attributeSymbol?.ConstructorArguments[1].Value;
                    var typeLevelPrefix = GetPrefix(prefixArgument);

                    if (typeLevelPrefix == null)
                    {
                        diagnosticsBuilder.Add(Diagnostic.Create(
                            descriptor: InvalidPrefixPatternRule,
                            location: attributeSymbol?.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
                            messageArgs: [prefixArgument]
                        ));

                        return ((TypeContext?)null, diagnosticsBuilder.ToImmutable());
                    }

                    return (
                        new TypeContext(
                            Symbol: typeSymbol,
                            AccessModifier: GetAccessModifier(attributeSymbol?.ConstructorArguments[0].Value),
                            Prefix: typeLevelPrefix,
                            NamingRule: GetNamingRule(attributeSymbol?.ConstructorArguments[2].Value),
                            Compilation: (CSharpCompilation)semanticModel.Compilation
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
