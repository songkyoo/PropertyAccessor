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
    #endregion

    #region Types
    private sealed record TypeContext(
        INamedTypeSymbol DeclaredTypeSymbol,
        PropertyAccessModifier AccessModifier,
        string Prefix,
        PropertyNamingRule NamingRule
    );

    private sealed record FieldContext(
        IFieldSymbol DeclaredFieldSymbol,
        bool HasGetterAttribute,
        bool HasSetterAttribute,
        PropertyAccessModifier AccessModifier,
        string Prefix,
        PropertyNamingRule NamingRule
    );
    #endregion

    #region Static
    private static readonly DiagnosticDescriptor ReadonlyFieldWithSetterAttributeRule =
        new(
            id: "PA0001",
            title: "SetterAttribute cannot be applied to readonly fields",
            messageFormat: "Field '{0}' is marked readonly but has Setter attribute",
            category: "Usage",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );
    #endregion

    #region Static
    private static ImmutableArray<(FieldContext?, ImmutableArray<Diagnostic>)> GetFieldContexts(
        TypeContext typeContext
    )
    {
        var (typeSymbol, accessModifier, prefix, namingRule) = typeContext;

        return typeSymbol
            .GetMembers()
            .OfType<IFieldSymbol>()
            .Select(symbol => GetGenerationContext(symbol, accessModifier, prefix, namingRule))
            .ToImmutableArray()!;
    }

    private static (FieldContext?, ImmutableArray<Diagnostic>) GetGenerationContext(
        IFieldSymbol fieldSymbol,
        PropertyAccessModifier accessModifier,
        string prefix,
        PropertyNamingRule namingRule
    )
    {
        var hasGetterAttribute = false;
        var hasSetterAttribute = false;
        var autoProperty = (AttributeData?)null;
        var diagnosticsBuilder = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (var attributeData in fieldSymbol.GetAttributes())
        {
            var attributeClassString = attributeData.AttributeClass?.ToDisplayString();

            switch (attributeClassString)
            {
                case GetterAttributeString:
                {
                    hasGetterAttribute = true;
                    break;
                }
                case SetterAttributeString when fieldSymbol.IsReadOnly:
                {
                    diagnosticsBuilder.Add(Diagnostic.Create(
                        descriptor: ReadonlyFieldWithSetterAttributeRule,
                        location: fieldSymbol.Locations.FirstOrDefault(),
                        messageArgs: fieldSymbol.Name
                    ));
                    break;
                }
                case SetterAttributeString:
                {
                    hasSetterAttribute = true;
                    break;
                }
                case AutoPropertyAttributeString:
                {
                    autoProperty = attributeData;
                    break;
                }
            }
        }

        if (hasGetterAttribute || hasSetterAttribute)
        {
            return (
                new FieldContext(
                    DeclaredFieldSymbol: fieldSymbol,
                    AccessModifier: GetAccessModifier(autoProperty?.ConstructorArguments[0].Value, accessModifier),
                    Prefix: GetPrefix(autoProperty?.ConstructorArguments[1].Value, prefix),
                    NamingRule: GetNamingRule(autoProperty?.ConstructorArguments[2].Value, namingRule),
                    HasGetterAttribute: hasGetterAttribute,
                    HasSetterAttribute: hasSetterAttribute
                ),
                diagnosticsBuilder.ToImmutable()
            );
        }
        else
        {
            return (null, diagnosticsBuilder.ToImmutable());
        }
    }

    private static ImmutableArray<string> GenerateAccessorCode(FieldContext fieldContext)
    {
        var (
            fieldSymbol,
            hasGetterAttribute,
            hasSetterAttribute,
            accessModifier,
            prefix,
            namingRule
        ) = fieldContext;

        if (!hasGetterAttribute && !hasSetterAttribute)
        {
            return ImmutableArray<string>.Empty;
        }

        var fieldName = fieldSymbol.Name;
        var propertyName = GetPropertyName(fieldName, prefix, namingRule);

        if (propertyName.Length < 1)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>();

        builder.Add($"{GetAccessorModifier(accessModifier)} {fieldSymbol.Type.ToDisplayString(FullyQualifiedFormat)} {propertyName}");
        builder.Add($"{{");

        if (hasGetterAttribute)
        {
            builder.Add($"{Space}get => {fieldName};");
        }

        if (hasSetterAttribute)
        {
            builder.Add($"{Space}set => {fieldName} = value;");
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

        static string GetPropertyName(string name, string prefix, PropertyNamingRule namingRule)
        {
            var prefixRemovedName = Regex.Replace(input: name, pattern: $"^{prefix}", replacement: "");

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

    private static string GetPrefix(object? value, string defaultValue = "(_|m_)")
    {
        return value is string stringValue && !string.IsNullOrWhiteSpace(stringValue)
            ? stringValue
            : defaultValue;
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
        var uniqueTypeSymbols = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        IncrementalValuesProvider<TypeContext> valuesProvider = context
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
                    var semanticModel = generatorSyntaxContext.SemanticModel;
                    if (semanticModel.GetDeclaredSymbol(generatorSyntaxContext.Node) is not INamedTypeSymbol typeSymbol)
                    {
                        return null;
                    }

                    var attributeSymbol = typeSymbol
                        .GetAttributes()
                        .FirstOrDefault(attributeData =>
                        {
                            return attributeData.AttributeClass?.ToDisplayString() == AutoPropertyAttributeString;
                        });
                    if (attributeSymbol == null)
                    {
                        return null;
                    }

                    if (!uniqueTypeSymbols.Add(typeSymbol))
                    {
                        return null;
                    }

                    return new TypeContext(
                        DeclaredTypeSymbol: typeSymbol,
                        AccessModifier: GetAccessModifier(attributeSymbol.ConstructorArguments[0].Value),
                        Prefix: GetPrefix(attributeSymbol.ConstructorArguments[1].Value),
                        NamingRule: GetNamingRule(attributeSymbol.ConstructorArguments[2].Value)
                    );
                }
            )
            .Where(typeContext => typeContext != null)!;

        context.RegisterSourceOutput(valuesProvider.Collect(), (sourceProductionContext, typeContexts) =>
        {
            foreach (var typeContext in typeContexts)
            {
                var builder = ImmutableArray.CreateBuilder<string>();

                foreach (var (fieldContext, diagnostics) in GetFieldContexts(typeContext))
                {
                    foreach (var diagnostic in diagnostics)
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
                    typeSymbol: typeContext.DeclaredTypeSymbol,
                    lines: builder.ToImmutable()
                );
            }
        });
    }
    #endregion
}
