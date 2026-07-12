using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Microsoft.CodeAnalysis.CSharp.SyntaxFacts;

namespace Macaron.PropertyAccessor;

internal static class GenerationModelFactory
{
    #region Static
    public static ImmutableArray<AnalysisResult<GenerationModel>> GetGenerationModels(
        ImmutableArray<AnalysisResult<TypeContext>> typeAnalysisResults,
        CancellationToken cancellationToken
    )
    {
        var builder = ImmutableArray.CreateBuilder<AnalysisResult<GenerationModel>>();

        foreach (var typeAnalysisResult in typeAnalysisResults)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (typeAnalysisResult is AnalysisResult<TypeContext>.Failure failure)
            {
                builder.Add(new AnalysisResult<GenerationModel>.Failure(failure.Diagnostics));

                continue;
            }

            if (typeAnalysisResult is AnalysisResult<TypeContext>.Success success)
            {
                builder.AddRange(GetGenerationModels(success.Model, cancellationToken));
            }
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<AnalysisResult<GenerationModel>> GetGenerationModels(
        TypeContext typeContext,
        CancellationToken cancellationToken
    )
    {
        var propertyAnalysisResults = AnalysisContextFactory.GetPropertyModels(
            typeContext,
            cancellationToken
        );
        var propertyModels = ImmutableArray.CreateBuilder<PropertyModel>();
        var builder = ImmutableArray.CreateBuilder<AnalysisResult<GenerationModel>>();

        foreach (var propertyAnalysisResult in propertyAnalysisResults)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (propertyAnalysisResult is AnalysisResult<PropertyModel>.Failure failure)
            {
                builder.Add(new AnalysisResult<GenerationModel>.Failure(failure.Diagnostics));

                continue;
            }

            if (propertyAnalysisResult is AnalysisResult<PropertyModel>.Success success)
            {
                propertyModels.Add(success.Model);
            }
        }

        if (propertyModels.Count > 0)
        {
            builder.Add(new AnalysisResult<GenerationModel>.Success(GetGenerationModel(
                typeContext.Symbol,
                propertyModels.ToImmutable()
            )));
        }

        return builder.ToImmutable();
    }

    private static GenerationModel GetGenerationModel(
        INamedTypeSymbol typeSymbol,
        ImmutableArray<PropertyModel> propertyModels
    )
    {
        var containingTypes = new Stack<INamedTypeSymbol>();
        var containingType = typeSymbol.ContainingType;

        while (containingType != null)
        {
            containingTypes.Push(containingType);
            containingType = containingType.ContainingType;
        }

        var declarations = ImmutableArray.CreateBuilder<string>(containingTypes.Count + 1);

        foreach (var currentType in containingTypes)
        {
            declarations.Add(GetPartialTypeDeclarationString(currentType));
        }

        declarations.Add(GetPartialTypeDeclarationString(typeSymbol));

        return new GenerationModel(
            HintName: GetHintName(typeSymbol),
            Type: new TypeGenerationModel(
                NamespaceName: typeSymbol.ContainingNamespace.IsGlobalNamespace
                    ? ""
                    : typeSymbol.ContainingNamespace.ToDisplayString(),
                Declarations: declarations.ToImmutable()
            ),
            Properties: propertyModels
        );
    }

    private static string GetPartialTypeDeclarationString(INamedTypeSymbol typeSymbol)
    {
        var typeModifier = typeSymbol.IsReadOnly ? "readonly " : "";
        var typeKind = GetTypeKindString(typeSymbol);
        var typeName = GetTypeNameString(typeSymbol);

        return $"{typeModifier}partial {typeKind} {typeName}";
    }

    private static string GetTypeNameString(INamedTypeSymbol typeSymbol)
    {
        var typeName = GetEscapedIdentifier(typeSymbol.Name);
        if (typeSymbol.TypeParameters.Length == 0)
        {
            return typeName;
        }

        var typeParameters = typeSymbol
            .TypeParameters
            .Select(typeParameter => GetEscapedIdentifier(typeParameter.Name));

        return $"{typeName}<{string.Join(", ", typeParameters)}>";
    }

    private static string GetHintName(INamedTypeSymbol typeSymbol)
    {
        var qualifiedName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        const uint fnvPrime = 16777619;
        const uint offsetBasis = 2166136261;

        var bytes = Encoding.UTF8.GetBytes(qualifiedName);
        var hash = offsetBasis;

        foreach (var b in bytes)
        {
            hash ^= b;
            hash *= fnvPrime;
        }

        return $"{typeSymbol.Name}_{typeSymbol.Arity}.{hash:x8}.g.cs";
    }

    private static string GetTypeKindString(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.IsRecord)
        {
            return typeSymbol.TypeKind is TypeKind.Struct ? "record struct" : "record";
        }

        return typeSymbol.TypeKind switch
        {
            TypeKind.Class => "class",
            TypeKind.Struct => "struct",
            TypeKind.Interface => "interface",
            _ => throw new InvalidOperationException($"Invalid type kind: {typeSymbol.TypeKind}"),
        };
    }

    private static string GetEscapedIdentifier(string identifier)
    {
        return GetKeywordKind(identifier) != SyntaxKind.None
            || GetContextualKeywordKind(identifier) != SyntaxKind.None
            ? "@" + identifier
            : identifier;
    }
    #endregion
}
