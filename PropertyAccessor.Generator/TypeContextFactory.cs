using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Macaron.PropertyAccessor;

internal static class TypeContextFactory
{
    #region Static
    public static ImmutableArray<AnalysisResult<TypeContext>> GetTypeContexts(
        ImmutableArray<AccessorAttributeContext> getAttributeContexts,
        ImmutableArray<AccessorAttributeContext> getSetAttributeContexts,
        ImmutableArray<PropertyGenerationDefaultsAttributeContext> defaultsAttributeContexts,
        ProjectGenerationOptions projectOptions,
        CancellationToken cancellationToken
    )
    {
        var compilationByType = new Dictionary<INamedTypeSymbol, CSharpCompilation>(
            SymbolEqualityComparer.Default
        );
        var defaultsAttributeByType = new Dictionary<INamedTypeSymbol, AttributeData>(
            SymbolEqualityComparer.Default
        );
        var reportedNonPartialTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

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

            if (!compilationByType.ContainsKey(defaultsAttributeContext.Symbol))
            {
                compilationByType.Add(defaultsAttributeContext.Symbol, defaultsAttributeContext.Compilation);
            }
        }

        if (compilationByType.Count == 0)
        {
            return ImmutableArray<AnalysisResult<TypeContext>>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<AnalysisResult<TypeContext>>(compilationByType.Count);
        var projectDiagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var projectDefaults = PropertyGenerationDefaultsResolver.ResolveProjectDefaults(
            projectOptions,
            projectDiagnostics
        );

        foreach (var entry in compilationByType)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var typeSymbol = entry.Key;
            var compilation = entry.Value;

            var partialDiagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
            var hasNonPartialType = false;

            for (var currentType = typeSymbol; currentType != null; currentType = currentType.ContainingType)
            {
                foreach (var syntaxReference in currentType.DeclaringSyntaxReferences)
                {
                    if (syntaxReference.GetSyntax(cancellationToken) is not TypeDeclarationSyntax declaration
                        || declaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)))
                    {
                        continue;
                    }

                    hasNonPartialType = true;

                    if (reportedNonPartialTypes.Add(currentType))
                    {
                        partialDiagnostics.Add(Diagnostic.Create(
                            descriptor: Diagnostics.TypeMustBePartialRule,
                            location: declaration.Identifier.GetLocation(),
                            messageArgs: [currentType.Name]
                        ));
                    }

                    break;
                }
            }

            if (hasNonPartialType)
            {
                if (partialDiagnostics.Count > 0)
                {
                    builder.Add(new AnalysisResult<TypeContext>.Failure(partialDiagnostics.ToImmutable()));
                }

                continue;
            }

            if (!PropertyGenerationDefaultsResolver.TryResolveTypeDefaults(
                typeSymbol,
                defaultsAttributeByType,
                projectDefaults,
                cancellationToken,
                out var defaults,
                out var diagnostic
            ))
            {
                builder.Add(new AnalysisResult<TypeContext>.Failure(diagnostic));
                continue;
            }

            builder.Add(new AnalysisResult<TypeContext>.Success(new TypeContext(
                Symbol: typeSymbol,
                AccessModifier: defaults.AccessModifier,
                PrefixRegex: defaults.PrefixRegex,
                NamingRule: defaults.NamingRule,
                Compilation: compilation
            )));
        }

        if (projectDiagnostics.Count > 0)
        {
            // 기존 타입 분석 결과의 순서를 유지해 설정 진단만 바뀔 때 생성 모델 캐시를 재사용한다.
            builder.Add(new AnalysisResult<TypeContext>.Failure(projectDiagnostics.ToImmutable()));
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
    #endregion
}
