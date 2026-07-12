using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Macaron.PropertyAccessor.Tests;

internal static class Helper
{
    #region Static
    public static void AssertGeneratedCode(
        string sourceCode,
        string expected,
        out ImmutableArray<Diagnostic> diagnostics
    )
    {
        (diagnostics, var generatedCode) = CompileAndGetResults(sourceCode);

        Assert.That(generatedCode.ReplaceLineEndings(), Is.EqualTo(expected.ReplaceLineEndings()));
    }

    public static void AssertGeneratedCode(
        string sourceCode,
        string expected
    )
    {
        var (_, generatedCode) = CompileAndGetResults(sourceCode);

        Assert.That(generatedCode.ReplaceLineEndings(), Is.EqualTo(expected.ReplaceLineEndings()));
    }

    public static CSharpCompilation CreateCompilation(string sourceCode)
    {
        var attributeAssembly = typeof(GetSetAttribute).Assembly;
        var references = AppDomain
            .CurrentDomain
            .GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Append(attributeAssembly)
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Cast<MetadataReference>()
            .ToImmutableArray();

        return CSharpCompilation.Create(
            assemblyName: "Macaron.PropertyAccessor.Tests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(sourceCode)],
            references: references,
            options: new CSharpCompilationOptions(
                outputKind: OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );
    }

    public static GeneratorDriver CreateGeneratorDriver(bool trackIncrementalGeneratorSteps = false)
    {
        if (!trackIncrementalGeneratorSteps)
        {
            return CSharpGeneratorDriver.Create(new PropertyAccessorGenerator());
        }

        return CSharpGeneratorDriver.Create(
            generators: [new PropertyAccessorGenerator().AsSourceGenerator()],
            additionalTexts: [],
            parseOptions: CSharpParseOptions.Default,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true
            )
        );
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, string GeneratedCode) CompileAndGetResults(
        string sourceCode
    )
    {
        var compilation = CreateCompilation(sourceCode);
        var driver = CreateGeneratorDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics
        );

        var result = driver.GetRunResult().Results.Single();
        var generatedSources = result.GeneratedSources;
        var generatedCode = generatedSources.Length > 0 ? generatedSources[0].SourceText.ToString() : "";

        var allDiagnostics = outputCompilation.GetDiagnostics()
            .Concat(generatorDiagnostics)
            .ToImmutableArray();

        return (allDiagnostics, generatedCode);
    }
    #endregion
}
