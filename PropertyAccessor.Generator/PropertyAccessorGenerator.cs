using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using static Macaron.PropertyAccessor.AttributeMetadataNames;

namespace Macaron.PropertyAccessor;

[Generator(LanguageNames.CSharp)]
public sealed class PropertyAccessorGenerator : IIncrementalGenerator
{
    #region IIncrementalGenerator Interface
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var getAttributeContextProvider = context
            .SyntaxProvider
            .ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: GetAttributeMetadataName,
                predicate: static (syntaxNode, _) => syntaxNode is VariableDeclaratorSyntax,
                transform: static (generatorAttributeSyntaxContext, _) =>
                    AnalysisContextFactory.GetAccessorAttributeContext(
                        generatorAttributeSyntaxContext,
                        kind: PropertyAccessorKind.Get
                    )
            )
            .Where(static attributeContext => attributeContext != null)
            .Select(static (attributeContext, _) => attributeContext!);
        var getSetAttributeContextProvider = context
            .SyntaxProvider
            .ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: GetSetAttributeMetadataName,
                predicate: static (syntaxNode, _) => syntaxNode is VariableDeclaratorSyntax,
                transform: static (generatorAttributeSyntaxContext, _) =>
                    AnalysisContextFactory.GetAccessorAttributeContext(
                        generatorAttributeSyntaxContext,
                        kind: PropertyAccessorKind.GetSet
                    )
            )
            .Where(static attributeContext => attributeContext != null)
            .Select(static (attributeContext, _) => attributeContext!);
        var defaultsAttributeContextProvider = context
            .SyntaxProvider
            .ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: PropertyGenerationDefaultsAttributeMetadataName,
                predicate: static (syntaxNode, _) => syntaxNode is TypeDeclarationSyntax,
                transform: static (generatorAttributeSyntaxContext, _) =>
                    AnalysisContextFactory.GetPropertyGenerationDefaultsAttributeContext(
                        generatorAttributeSyntaxContext
                    )
            )
            .Where(static attributeContext => attributeContext != null)
            .Select(static (attributeContext, _) => attributeContext!);

        var analysisResultProvider = getAttributeContextProvider
            .Collect()
            .Combine(getSetAttributeContextProvider.Collect())
            .Combine(defaultsAttributeContextProvider.Collect())
            .Select(static (attributeContexts, cancellationToken) =>
            {
                var ((getAttributeContexts, getSetAttributeContexts), defaultsAttributeContexts) = attributeContexts;

                return AnalysisContextFactory.GetTypeContexts(
                    getAttributeContexts,
                    getSetAttributeContexts,
                    defaultsAttributeContexts,
                    cancellationToken
                );
            })
            .SelectMany(static (analysisResults, _) => analysisResults);
        var diagnosticProvider = analysisResultProvider
            .Where(static result => result is AnalysisResult<TypeContext>.Failure)
            .SelectMany(static (result, _) => ((AnalysisResult<TypeContext>.Failure)result).Diagnostics);
        var typeContextProvider = analysisResultProvider
            .Where(static result => result is AnalysisResult<TypeContext>.Success)
            .Select(static (result, _) => ((AnalysisResult<TypeContext>.Success)result).Model);

        context.RegisterSourceOutput(diagnosticProvider, static (sourceProductionContext, diagnostic) =>
        {
            sourceProductionContext.ReportDiagnostic(diagnostic);
        });
        context.RegisterSourceOutput(typeContextProvider, static (sourceProductionContext, typeContext) =>
        {
            var builder = ImmutableArray.CreateBuilder<string>();

            foreach (var propertyAnalysisResult in AnalysisContextFactory.GetPropertyContexts(typeContext))
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
                var lines = SourceGenerationHelpers.GenerateAccessorCode(propertyContext);

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

            SourceGenerationHelpers.AddSource(
                context: sourceProductionContext,
                typeSymbol: typeContext.Symbol,
                lines: builder.ToImmutable()
            );
        });
    }
    #endregion
}
