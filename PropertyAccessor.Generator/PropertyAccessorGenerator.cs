using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using static System.Text.Encoding;
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
                var typeAnalysisResults = AnalysisContextFactory.GetTypeContexts(
                    getAttributeContexts,
                    getSetAttributeContexts,
                    defaultsAttributeContexts,
                    cancellationToken
                );

                return GenerationModelFactory.GetGenerationModels(
                    typeAnalysisResults,
                    cancellationToken
                );
            });

        var diagnosticProvider = analysisResultProvider
            .SelectMany(static (analysisResults, _) =>
            {
                return analysisResults
                    .OfType<AnalysisResult<GenerationModel>.Failure>()
                    .SelectMany(failure => failure.Diagnostics);
            });
        var generationModelProvider = analysisResultProvider
            .SelectMany(static (analysisResults, _) => analysisResults)
            // 실패한 항목도 null로 유지해야 Roslyn 4.3에서 제거·복구 시 추적 예외 없이 캐시를 재사용한다.
            .Select(static (result, _) => result is AnalysisResult<GenerationModel>.Success success
                ? success.Model
                : null
            )
            .WithTrackingName("GenerationModel");

        context.RegisterSourceOutput(diagnosticProvider, static (sourceProductionContext, diagnostic) =>
        {
            sourceProductionContext.ReportDiagnostic(diagnostic);
        });
        context.RegisterSourceOutput(generationModelProvider, static (sourceProductionContext, generationModel) =>
        {
            if (generationModel == null)
            {
                return;
            }

            var sourceText = SourceGenerationHelpers.GetSource(generationModel);

            sourceProductionContext.AddSource(
                hintName: generationModel.HintName,
                sourceText: SourceText.From(sourceText, encoding: UTF8)
            );
        });
    }
    #endregion
}
