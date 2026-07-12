using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.PropertyAccessor.AttributeMetadataNames;
using static Microsoft.CodeAnalysis.SymbolDisplayFormat;
using static Microsoft.CodeAnalysis.SymbolDisplayMiscellaneousOptions;

namespace Macaron.PropertyAccessor;

internal static class AnalysisContextFactory
{
    #region Static
    private static readonly Regex DefaultRegex = new(pattern: "^(_|m_)", RegexOptions.Compiled);

    public static AccessorAttributeContext? GetAccessorAttributeContext(
        GeneratorAttributeSyntaxContext generatorAttributeSyntaxContext,
        PropertyAccessorKind kind
    )
    {
        if (generatorAttributeSyntaxContext.Attributes.Length != 1)
        {
            return null;
        }

        return generatorAttributeSyntaxContext is
            {
                TargetSymbol: IFieldSymbol fieldSymbol,
                SemanticModel.Compilation: CSharpCompilation compilation,
            }
            ? new AccessorAttributeContext(fieldSymbol, kind, compilation)
            : null;
    }

    public static PropertyGenerationDefaultsAttributeContext? GetPropertyGenerationDefaultsAttributeContext(
        GeneratorAttributeSyntaxContext generatorAttributeSyntaxContext
    )
    {
        if (generatorAttributeSyntaxContext.Attributes.Length != 1)
        {
            return null;
        }

        return generatorAttributeSyntaxContext.TargetSymbol is INamedTypeSymbol typeSymbol
            ? new PropertyGenerationDefaultsAttributeContext(
                typeSymbol,
                generatorAttributeSyntaxContext.Attributes
            )
            : null;
    }

    public static ImmutableArray<AnalysisResult<TypeContext>> GetTypeContexts(
        ImmutableArray<AccessorAttributeContext> getAttributeContexts,
        ImmutableArray<AccessorAttributeContext> getSetAttributeContexts,
        ImmutableArray<PropertyGenerationDefaultsAttributeContext> defaultsAttributeContexts,
        CancellationToken cancellationToken
    )
    {
        var compilationByType = new Dictionary<INamedTypeSymbol, CSharpCompilation>(
            SymbolEqualityComparer.Default
        );
        var defaultsAttributeByType = new Dictionary<INamedTypeSymbol, AttributeData>(
            SymbolEqualityComparer.Default
        );

        AddAccessorAttributeContexts(getAttributeContexts);
        AddAccessorAttributeContexts(getSetAttributeContexts);

        foreach (var defaultsAttributeContext in defaultsAttributeContexts)
        {
            if (defaultsAttributeByType.ContainsKey(defaultsAttributeContext.Symbol)
                || defaultsAttributeContext.Attributes.IsDefaultOrEmpty
            )
            {
                continue;
            }

            defaultsAttributeByType.Add(
                defaultsAttributeContext.Symbol,
                defaultsAttributeContext.Attributes[0]
            );
        }

        var builder = ImmutableArray.CreateBuilder<AnalysisResult<TypeContext>>(compilationByType.Count);

        foreach (var entry in compilationByType)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var typeSymbol = entry.Key;
            var compilation = entry.Value;

            defaultsAttributeByType.TryGetValue(typeSymbol, out var defaultsAttribute);
            builder.Add(GetTypeContext(
                typeSymbol,
                compilation,
                defaultsAttribute,
                cancellationToken
            ));
        }

        return builder.ToImmutable();

        #region Local Functions
        void AddAccessorAttributeContexts(ImmutableArray<AccessorAttributeContext> accessorAttributeContexts)
        {
            foreach (var accessorAttributeContext in accessorAttributeContexts)
            {
                var typeSymbol = accessorAttributeContext.Symbol.ContainingType;

                if (!compilationByType.ContainsKey(typeSymbol))
                {
                    compilationByType.Add(typeSymbol, accessorAttributeContext.Compilation);
                }
            }
        }
        #endregion
    }

    public static ImmutableArray<AnalysisResult<PropertyModel>> GetPropertyModels(
        TypeContext typeContext,
        CancellationToken cancellationToken
    )
    {
        var (typeSymbol, accessModifier, prefixRegex, namingRule, compilation) = typeContext;
        var builder = ImmutableArray.CreateBuilder<AnalysisResult<PropertyModel>>();

        foreach (var fieldSymbol in typeSymbol.GetMembers().OfType<IFieldSymbol>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!HasAccessorAttribute(fieldSymbol))
            {
                continue;
            }

            var result = GetPropertyModel(
                fieldSymbol,
                accessModifier,
                prefixRegex,
                namingRule,
                compilation,
                cancellationToken
            );

            if (result != null)
            {
                builder.Add(result);
            }
        }

        return builder.ToImmutable();

        #region Local Functions
        static bool HasAccessorAttribute(IFieldSymbol fieldSymbol)
        {
            return fieldSymbol.GetAttributes().Any(attributeData =>
            {
                var attributeName = attributeData.AttributeClass?.ToDisplayString();

                return attributeName is GetAttributeMetadataName or GetSetAttributeMetadataName;
            });
        }
        #endregion
    }

    private static AnalysisResult<TypeContext> GetTypeContext(
        INamedTypeSymbol typeSymbol,
        CSharpCompilation compilation,
        AttributeData? defaultsAttribute,
        CancellationToken cancellationToken
    )
    {
        var prefixPatternArgument = defaultsAttribute?.ConstructorArguments[1].Value;
        var typeLevelPrefixRegex = GetPrefixRegex(prefixPatternArgument);

        if (typeLevelPrefixRegex == null)
        {
            return new AnalysisResult<TypeContext>.Failure(Diagnostic.Create(
                descriptor: Diagnostics.InvalidPrefixPatternRule,
                location: defaultsAttribute?
                    .ApplicationSyntaxReference?
                    .GetSyntax(cancellationToken)
                    .GetLocation(),
                messageArgs: [prefixPatternArgument]
            ));
        }

        return new AnalysisResult<TypeContext>.Success(new TypeContext(
            Symbol: typeSymbol,
            AccessModifier: GetAccessModifier(defaultsAttribute?.ConstructorArguments[0].Value),
            PrefixRegex: typeLevelPrefixRegex,
            NamingRule: GetNamingRule(defaultsAttribute?.ConstructorArguments[2].Value),
            Compilation: compilation
        ));
    }

    private static AnalysisResult<PropertyModel>? GetPropertyModel(
        IFieldSymbol fieldSymbol,
        PropertyAccessModifier accessModifier,
        Regex prefixRegex,
        PropertyNamingRule namingRule,
        CSharpCompilation compilation,
        CancellationToken cancellationToken
    )
    {
        var fieldName = fieldSymbol.Name;
        var fieldTypeSymbol = fieldSymbol.Type;

        var getAttribute = (AttributeData?)null;
        var getSetAttribute = (AttributeData?)null;
        var accessorKind = PropertyAccessorKind.None;
        var getterRequiresExplicitConversion = false;

        foreach (var attributeData in fieldSymbol.GetAttributes())
        {
            switch (attributeData.AttributeClass?.ToDisplayString())
            {
                case GetAttributeMetadataName:
                {
                    getAttribute = attributeData;
                    if (accessorKind == PropertyAccessorKind.None)
                    {
                        accessorKind = PropertyAccessorKind.Get;
                    }

                    break;
                }
                case GetSetAttributeMetadataName:
                {
                    getSetAttribute = attributeData;
                    accessorKind = PropertyAccessorKind.GetSet;

                    break;
                }
            }
        }

        if (fieldSymbol.IsStatic && accessorKind != PropertyAccessorKind.None)
        {
            return new AnalysisResult<PropertyModel>.Failure(Diagnostic.Create(
                descriptor: Diagnostics.StaticFieldNotSupportedRule,
                location: fieldSymbol.Locations.FirstOrDefault(),
                messageArgs: [fieldName]
            ));
        }

        var typeSymbol = fieldTypeSymbol;

        if (accessorKind == PropertyAccessorKind.None)
        {
            return null;
        }

        var shapeAttribute = getSetAttribute ?? getAttribute;
        if (accessorKind == PropertyAccessorKind.Get
            && getAttribute is { ConstructorArguments.Length: > 0 }
            && getAttribute.ConstructorArguments[0].Value is ITypeSymbol propertyTypeSymbol
        )
        {
            var diagnosticLocation = getAttribute
                .ApplicationSyntaxReference?
                .GetSyntax(cancellationToken)
                .GetLocation();
            var getterConversion = compilation.ClassifyConversion(fieldTypeSymbol, propertyTypeSymbol);

            if (!getterConversion.Exists)
            {
                return new AnalysisResult<PropertyModel>.Failure(Diagnostic.Create(
                    descriptor: Diagnostics.InvalidGetterConversionRule,
                    location: diagnosticLocation,
                    messageArgs:
                    [
                        fieldName,
                        fieldTypeSymbol.ToDisplayString(MinimallyQualifiedFormat),
                        propertyTypeSymbol.ToDisplayString(MinimallyQualifiedFormat)
                    ]
                ));
            }

            typeSymbol = propertyTypeSymbol;
            getterRequiresExplicitConversion = !getterConversion.IsImplicit;
        }

        var usesGetAttribute = ReferenceEquals(shapeAttribute, getAttribute);
        var explicitPropertyName = GetConstructorArgumentValue(shapeAttribute, usesGetAttribute ? 2 : 1) as string;
        var propertyName = !string.IsNullOrWhiteSpace(explicitPropertyName)
            ? explicitPropertyName!
            : GetPropertyName(fieldName, prefixRegex, namingRule);

        if (propertyName.Length < 1)
        {
            return new AnalysisResult<PropertyModel>.Failure(Diagnostic.Create(
                descriptor: Diagnostics.InvalidPropertyNameAfterPrefixRemovalRule,
                location: fieldSymbol.Locations.FirstOrDefault(),
                messageArgs: [fieldName, prefixRegex]
            ));
        }

        if (propertyName == fieldName)
        {
            return new AnalysisResult<PropertyModel>.Failure(Diagnostic.Create(
                descriptor: Diagnostics.PropertyNameSameAsFieldNameRule,
                location: fieldSymbol.Locations.FirstOrDefault(),
                messageArgs: [fieldName, prefixRegex, propertyName]
            ));
        }

        var propertyTypeName = typeSymbol.ToDisplayString(FullyQualifiedFormat.WithMiscellaneousOptions(
            IncludeNullableReferenceTypeModifier
            | UseSpecialTypes
        ));

        return new AnalysisResult<PropertyModel>.Success(new PropertyModel(
            AccessModifier: GetAccessModifier(
                GetConstructorArgumentValue(shapeAttribute, usesGetAttribute ? 1 : 0),
                accessModifier
            ),
            TypeName: propertyTypeName,
            Name: propertyName,
            FieldName: fieldName,
            AccessorKind: accessorKind,
            IsInitAccessor: fieldSymbol.IsReadOnly,
            GetterRequiresExplicitConversion: getterRequiresExplicitConversion
        ));
    }

    private static string GetPropertyName(
        string fieldName,
        Regex prefixRegex,
        PropertyNamingRule namingRule
    )
    {
        var prefixRemovedName = prefixRegex.Replace(input: fieldName, replacement: "", count: 1);

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

    private static object? GetConstructorArgumentValue(AttributeData? attributeData, int index)
    {
        var constructorArguments = attributeData?.ConstructorArguments;

        return constructorArguments is { Length: > 0 and var length } && index < length
            ? constructorArguments.Value[index].Value
            : null;
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

    private static Regex? GetPrefixRegex(object? value, Regex? defaultValue = null)
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
    #endregion
}
