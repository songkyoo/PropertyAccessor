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
            })
            .SelectMany(static (analysisResults, _) => analysisResults);
        var diagnosticProvider = analysisResultProvider
            .Where(static result => result is AnalysisResult<GenerationModel>.Failure)
            .SelectMany(static (result, _) => ((AnalysisResult<GenerationModel>.Failure)result).Diagnostics);
        var generationModelProvider = analysisResultProvider
            .Where(static result => result is AnalysisResult<GenerationModel>.Success)
            .Select(static (result, _) => ((AnalysisResult<GenerationModel>.Success)result).Model)
            .WithTrackingName("GenerationModel");

        context.RegisterSourceOutput(diagnosticProvider, static (sourceProductionContext, diagnostic) =>
        {
            sourceProductionContext.ReportDiagnostic(diagnostic);
        });
        context.RegisterSourceOutput(generationModelProvider, static (sourceProductionContext, generationModel) =>
        {
            var sourceText = SourceGenerationHelpers.GetSource(generationModel);

            sourceProductionContext.AddSource(
                hintName: generationModel.HintName,
                sourceText: SourceText.From(sourceText, encoding: UTF8)
            );
        });
    }
    #endregion
}
