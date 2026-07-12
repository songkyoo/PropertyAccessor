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

[Generator(LanguageNames.CSharp)]
public sealed class PropertyAccessorGenerator : IIncrementalGenerator
{
    #region Constants
    private const string PropertyGenerationDefaultsAttributeMetadataName = "Macaron.PropertyAccessor.PropertyGenerationDefaultsAttribute";
    private const string GetAttributeMetadataName = "Macaron.PropertyAccessor.GetAttribute";
    private const string GetSetAttributeMetadataName = "Macaron.PropertyAccessor.GetSetAttribute";
    #endregion

    #region Static
    private static readonly Regex DefaultRegex = new(pattern: "^(_|m_)", RegexOptions.Compiled);

    private static AnalysisResult<TypeContext>? GetTypeContext(
        GeneratorSyntaxContext generatorSyntaxContext,
        CancellationToken cancellationToken
    )
    {
        var semanticModel = generatorSyntaxContext.SemanticModel;

        if (semanticModel.GetDeclaredSymbol(
                generatorSyntaxContext.Node,
                cancellationToken
            ) is not INamedTypeSymbol typeSymbol
        )
        {
            return null;
        }

        var defaultsAttribute = typeSymbol
            .GetAttributes()
            .FirstOrDefault(attributeData =>
            {
                return attributeData.AttributeClass?.ToDisplayString() == PropertyGenerationDefaultsAttributeMetadataName;
            });

        var hasAccessorField = typeSymbol
            .GetMembers()
            .OfType<IFieldSymbol>()
            .Any(fieldSymbol => fieldSymbol.GetAttributes().Any(attributeData =>
            {
                var attributeName = attributeData.AttributeClass?.ToDisplayString();

                return attributeName is GetAttributeMetadataName or GetSetAttributeMetadataName;
            }));

        if (!hasAccessorField)
        {
            return null;
        }

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
            Compilation: (CSharpCompilation)semanticModel.Compilation
        ));
    }

    private static ImmutableArray<AnalysisResult<PropertyContext>> GetPropertyContexts(
        TypeContext typeContext
    )
    {
        var (typeSymbol, accessModifier, prefixRegex, namingRule, compilation) = typeContext;

        return typeSymbol
            .GetMembers()
            .OfType<IFieldSymbol>()
            .Where(HasAccessorAttribute)
            .Select(symbol => GetGenerationContext(
                symbol,
                accessModifier,
                prefixRegex,
                namingRule,
                compilation
            ))
            .Where(static result => result != null)
            .Select(static result => result!)
            .ToImmutableArray();

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

    private static AnalysisResult<PropertyContext>? GetGenerationContext(
        IFieldSymbol fieldSymbol,
        PropertyAccessModifier accessModifier,
        Regex prefixRegex,
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
            return new AnalysisResult<PropertyContext>.Failure(Diagnostic.Create(
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
        if (accessorKind == PropertyAccessorKind.Get &&
            getAttribute is { ConstructorArguments.Length: > 0 } &&
            getAttribute.ConstructorArguments[0].Value is ITypeSymbol propertyTypeSymbol
        )
        {
            var diagnosticLocation = getAttribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();
            var getterConversion = compilation.ClassifyConversion(fieldTypeSymbol, propertyTypeSymbol);

            if (!getterConversion.Exists)
            {
                return new AnalysisResult<PropertyContext>.Failure(Diagnostic.Create(
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
            return new AnalysisResult<PropertyContext>.Failure(Diagnostic.Create(
                descriptor: Diagnostics.InvalidPropertyNameAfterPrefixRemovalRule,
                location: fieldSymbol.Locations.FirstOrDefault(),
                messageArgs: [fieldName, prefixRegex]
            ));
        }

        if (propertyName == fieldName)
        {
            return new AnalysisResult<PropertyContext>.Failure(Diagnostic.Create(
                descriptor: Diagnostics.PropertyNameSameAsFieldNameRule,
                location: fieldSymbol.Locations.FirstOrDefault(),
                messageArgs: [fieldName, prefixRegex, propertyName]
            ));
        }

        return new AnalysisResult<PropertyContext>.Success( new PropertyContext(
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
        ));

        #region Local Functions
        static string GetPropertyName(string fieldName, Regex prefixRegex, PropertyNamingRule namingRule)
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
        builder.Add("{");

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

        builder.Add("}");

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
        var analysisResult = context
            .SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (syntaxNode, _) =>
                {
                    return syntaxNode
                        is TypeDeclarationSyntax typeDeclaration
                        and (ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax)
                        && (
                            typeDeclaration.AttributeLists.Count > 0
                            || typeDeclaration
                                .Members
                                .OfType<FieldDeclarationSyntax>()
                                .Any(fieldDeclaration => fieldDeclaration.AttributeLists.Count > 0)
                        );
                },
                transform: static (generatorSyntaxContext, cancellationToken) => GetTypeContext(
                    generatorSyntaxContext,
                    cancellationToken
                )
            )
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);
        var diagnosticProvider = analysisResult
            .Where(static result => result is AnalysisResult<TypeContext>.Failure)
            .SelectMany(static (result, _) => ((AnalysisResult<TypeContext>.Failure)result).Diagnostics);
        var typeContextProvider = analysisResult
            .Where(static result => result is AnalysisResult<TypeContext>.Success)
            .Select(static (result, _) => ((AnalysisResult<TypeContext>.Success)result).Model);

        context.RegisterSourceOutput(diagnosticProvider, static (sourceProductionContext, diagnostic) =>
        {
            sourceProductionContext.ReportDiagnostic(diagnostic);
        });
        context.RegisterSourceOutput(typeContextProvider.Collect(), (sourceProductionContext, typeContexts) =>
        {
            var visitedTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            foreach (var typeContext in typeContexts)
            {
                if (!visitedTypes.Add(typeContext.Symbol))
                {
                    continue;
                }

                var builder = ImmutableArray.CreateBuilder<string>();

                foreach (var propertyAnalysisResult in GetPropertyContexts(typeContext))
                {
                    if (propertyAnalysisResult is AnalysisResult<PropertyContext>.Failure failure)
                    {
                        foreach (var diagnostic in failure.Diagnostics)
                        {
                            sourceProductionContext.ReportDiagnostic(diagnostic);
                        }

                        continue;
                    }

                    if (propertyAnalysisResult is not AnalysisResult<PropertyContext>.Success success)
                    {
                        continue;
                    }

                    var propertyContext = success.Model;
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
