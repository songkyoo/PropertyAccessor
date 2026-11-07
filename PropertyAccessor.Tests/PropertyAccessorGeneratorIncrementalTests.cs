using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Macaron.PropertyAccessor.Tests.Helper;

namespace Macaron.PropertyAccessor.Tests;

[TestFixture]
public class PropertyAccessorGeneratorIncrementalTests
{
    #region Static
    private static IReadOnlyList<IncrementalStepRunReason> GetGenerationModelRunReasons(GeneratorDriver driver)
    {
        var result = driver.GetRunResult().Results.Single();

        return result
            .TrackedSteps["GenerationModel"]
            .SelectMany(step => step.Outputs)
            .Select(output => output.Reason)
            .ToArray();
    }
    #endregion

    #region Tests
    [Test]
    public void Should_CacheGenerationModels_When_CompilationIsUnchanged()
    {
        var compilation = CreateCompilation(
            """
            namespace Macaron.PropertyAccessor.Tests;

            public partial class Foo
            {
                [Get]
                private int _answer = 42;
            }

            public partial class Bar
            {
                [GetSet]
                private string _name = "";
            }
            """
        );
        var driver = CreateGeneratorDriver(trackIncrementalGeneratorSteps: true);

        driver = driver.RunGenerators(compilation);
        driver = driver.RunGenerators(compilation);

        var reasons = GetGenerationModelRunReasons(driver);

        Assert.That(reasons, Has.Count.EqualTo(2));
        Assert.That(reasons, Has.All.EqualTo(IncrementalStepRunReason.Cached));
    }

    [Test]
    public void Should_OnlyModifyChangedGenerationModel_When_OneTypeChanges()
    {
        var compilation = CreateCompilation(
            """
            namespace Macaron.PropertyAccessor.Tests;

            public partial class Foo
            {
                [Get]
                private int _answer = 42;
            }

            public partial class Bar
            {
                [GetSet]
                private string _name = "";
            }
            """
        );
        var driver = CreateGeneratorDriver(trackIncrementalGeneratorSteps: true);

        driver = driver.RunGenerators(compilation);

        var originalSyntaxTree = compilation.SyntaxTrees.Single();
        var updatedSyntaxTree = CSharpSyntaxTree.ParseText(
            """
            namespace Macaron.PropertyAccessor.Tests;

            public partial class Foo
            {
                [Get]
                private int _value = 42;
            }

            public partial class Bar
            {
                [GetSet]
                private string _name = "";
            }
            """
        );
        var updatedCompilation = compilation.ReplaceSyntaxTree(originalSyntaxTree, updatedSyntaxTree);

        driver = driver.RunGenerators(updatedCompilation);

        var reasons = GetGenerationModelRunReasons(driver);

        Assert.That(reasons, Has.Count.EqualTo(2));
        Assert.That(reasons.Count(reason => reason == IncrementalStepRunReason.Modified), Is.EqualTo(1));
        Assert.That(reasons.Count(reason => reason == IncrementalStepRunReason.Cached), Is.EqualTo(1));
    }

    [Test]
    public void Should_OnlyModifyChangedGenerationModel_When_SetterMethodNameChanges()
    {
        var compilation = CreateCompilation(
            """
            namespace Macaron.PropertyAccessor.Tests;

            public partial class Foo
            {
                [GetSet(setterName: "SetOriginal")]
                private int _value;
            }

            public partial class Bar
            {
                [Get]
                private int _value;
            }
            """
        );
        var driver = CreateGeneratorDriver(trackIncrementalGeneratorSteps: true);

        driver = driver.RunGenerators(compilation);
        var updatedSyntaxTree = CSharpSyntaxTree.ParseText(
            """
            namespace Macaron.PropertyAccessor.Tests;

            public partial class Foo
            {
                [GetSet(setterName: "SetUpdated")]
                private int _value;
            }

            public partial class Bar
            {
                [Get]
                private int _value;
            }
            """
        );
        var updatedCompilation = compilation.ReplaceSyntaxTree(compilation.SyntaxTrees.Single(), updatedSyntaxTree);

        driver = driver.RunGenerators(updatedCompilation);

        var reasons = GetGenerationModelRunReasons(driver);
        var generatedSources = driver.GetRunResult().Results.Single().GeneratedSources;

        Assert.That(reasons.Count(reason => reason == IncrementalStepRunReason.Modified), Is.EqualTo(1));
        Assert.That(reasons.Count(reason => reason == IncrementalStepRunReason.Cached), Is.EqualTo(1));
        Assert.That(generatedSources.Any(source => source.SourceText.ToString().Contains("SetUpdated")), Is.True);
        Assert.That(generatedSources.Any(source => source.SourceText.ToString().Contains("SetOriginal")), Is.False);
    }

    [Test]
    public void Should_OnlyModifyNestedGenerationModel_When_ContainingDefaultsChange()
    {
        var compilation = CreateCompilation(
            """
            namespace Macaron.PropertyAccessor.Tests;

            [PropertyGenerationDefaults(AccessModifier = PropertyAccessModifier.Protected)]
            public partial class Outer
            {
                public partial class Inner
                {
                    [Get]
                    private int _value;
                }
            }

            public partial class Other
            {
                [Get]
                private int _value;
            }
            """
        );
        var driver = CreateGeneratorDriver(trackIncrementalGeneratorSteps: true);

        driver = driver.RunGenerators(compilation);

        var updatedSyntaxTree = CSharpSyntaxTree.ParseText(
            """
            namespace Macaron.PropertyAccessor.Tests;

            [PropertyGenerationDefaults(AccessModifier = PropertyAccessModifier.Internal)]
            public partial class Outer
            {
                public partial class Inner
                {
                    [Get]
                    private int _value;
                }
            }

            public partial class Other
            {
                [Get]
                private int _value;
            }
            """
        );
        var updatedCompilation = compilation.ReplaceSyntaxTree(compilation.SyntaxTrees.Single(), updatedSyntaxTree);

        driver = driver.RunGenerators(updatedCompilation);

        var reasons = GetGenerationModelRunReasons(driver);
        var generatedSources = driver.GetRunResult().Results.Single().GeneratedSources;

        Assert.That(reasons.Count(reason => reason == IncrementalStepRunReason.Modified), Is.EqualTo(1));
        Assert.That(reasons.Count(reason => reason == IncrementalStepRunReason.Cached), Is.EqualTo(1));
        Assert.That(generatedSources.Any(source => source.SourceText.ToString().Contains("internal int Value")), Is.True);
    }
    [Test]
    public void Should_OnlyModifyOverrideModel_When_BasePropertyBecomesAbstractInAnotherFile()
    {
        const string sourceCode =
            """
            namespace Macaron.PropertyAccessor.Tests;

            public partial class Derived : Base
            {
                [Get]
                private int _value;
            }

            public partial class Unrelated
            {
                [Get]
                private int _other;
            }
            """;
        var baseTree = CSharpSyntaxTree.ParseText(
            """
            namespace Macaron.PropertyAccessor.Tests;

            public class Base
            {
                public virtual int Value => 0;
            }
            """
        );
        var compilation = CreateCompilation(sourceCode).AddSyntaxTrees(baseTree);
        var driver = CreateGeneratorDriver(trackIncrementalGeneratorSteps: true);

        driver = driver.RunGenerators(compilation);

        var updatedBaseTree = CSharpSyntaxTree.ParseText(
            """
            namespace Macaron.PropertyAccessor.Tests;

            public abstract class Base
            {
                public abstract int Value { get; }
            }
            """
        );
        var updatedCompilation = compilation.ReplaceSyntaxTree(baseTree, updatedBaseTree);

        driver = driver.RunGeneratorsAndUpdateCompilation(updatedCompilation, out var output, out _);

        var reasons = GetGenerationModelRunReasons(driver);
        var result = driver.GetRunResult().Results.Single();
        var derivedSource = result.GeneratedSources
            .Single(source => source.HintName.StartsWith("Derived_", StringComparison.Ordinal))
            .SourceText.ToString();
        var errors = output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(errors, Is.Empty);
        Assert.That(reasons.Count(reason => reason == IncrementalStepRunReason.Modified), Is.EqualTo(1));
        Assert.That(reasons.Count(reason => reason == IncrementalStepRunReason.Cached), Is.EqualTo(1));
        Assert.That(derivedSource, Does.Contain("public override int Value"));
    }

    [Test]
    public void Should_UpdateOverrideDiagnostics_When_BaseAccessorsChangeInAnotherFile()
    {
        const string sourceCode =
            """
            namespace Macaron.PropertyAccessor.Tests;

            public partial class Derived : Base
            {
                [Get]
                private int _value;
            }

            public partial class Unrelated
            {
                [Get]
                private int _other;
            }
            """;
        var baseTree = CSharpSyntaxTree.ParseText(
            """
            namespace Macaron.PropertyAccessor.Tests;

            public abstract class Base
            {
                public abstract int Value { get; }
            }
            """
        );
        var compilation = CreateCompilation(sourceCode).AddSyntaxTrees(baseTree);
        var driver = CreateGeneratorDriver(trackIncrementalGeneratorSteps: true);

        driver = driver.RunGenerators(compilation);

        var updatedBaseTree = CSharpSyntaxTree.ParseText(
            """
            namespace Macaron.PropertyAccessor.Tests;

            public abstract class Base
            {
                public abstract int Value { get; set; }
            }
            """
        );
        var updatedCompilation = compilation.ReplaceSyntaxTree(baseTree, updatedBaseTree);

        driver = driver.RunGenerators(updatedCompilation);

        var mismatchResult = driver.GetRunResult().Results.Single();
        var diagnostic = mismatchResult.Diagnostics.Single();
        var remainingSource = mismatchResult.GeneratedSources.Single().SourceText.ToString();
        var reasons = GetGenerationModelRunReasons(driver);

        Assert.That(diagnostic.Id, Is.EqualTo("MPROP0011"));
        Assert.That(remainingSource, Does.Contain("partial class Unrelated"));
        Assert.That(reasons, Does.Contain(IncrementalStepRunReason.Cached));

        var restoredBaseTree = CSharpSyntaxTree.ParseText(baseTree.GetText().ToString());
        var restoredCompilation = updatedCompilation.ReplaceSyntaxTree(updatedBaseTree, restoredBaseTree);

        driver = driver.RunGenerators(restoredCompilation);

        var restoredResult = driver.GetRunResult().Results.Single();

        Assert.That(restoredResult.Diagnostics, Is.Empty);
        Assert.That(restoredResult.GeneratedSources, Has.Length.EqualTo(2));
    }
    [Test]
    public void Should_ClearContractDiagnostic_When_AbstractPropertyBecomesVirtual()
    {
        const string sourceCode =
            """
            namespace Macaron.PropertyAccessor.Tests;

            public partial class Derived : Base
            {
                [Get]
                private int _value;
            }
            """;
        var baseTree = CSharpSyntaxTree.ParseText(
            """
            namespace Macaron.PropertyAccessor.Tests;

            public abstract class Base
            {
                public abstract int Value { get; set; }
            }
            """
        );
        var compilation = CreateCompilation(sourceCode).AddSyntaxTrees(baseTree);
        var driver = CreateGeneratorDriver(trackIncrementalGeneratorSteps: true);

        driver = driver.RunGenerators(compilation);

        var initialResult = driver.GetRunResult().Results.Single();

        Assert.That(initialResult.Diagnostics.Single().Id, Is.EqualTo("MPROP0011"));
        Assert.That(initialResult.GeneratedSources, Is.Empty);

        var updatedBaseTree = CSharpSyntaxTree.ParseText(
            """
            namespace Macaron.PropertyAccessor.Tests;

            public class Base
            {
                public virtual int Value { get; set; }
            }
            """
        );
        var updatedCompilation = compilation.ReplaceSyntaxTree(baseTree, updatedBaseTree);

        driver = driver.RunGeneratorsAndUpdateCompilation(updatedCompilation, out var output, out _);

        var result = driver.GetRunResult().Results.Single();
        var generatedSource = result.GeneratedSources.Single().SourceText.ToString();
        var compilerDiagnostics = output.GetDiagnostics();
        var errors = compilerDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(errors, Is.Empty);
        Assert.That(generatedSource, Does.Not.Contain("override"));
        Assert.That(compilerDiagnostics, Has.Some.Matches<Diagnostic>(diagnostic => diagnostic.Id == "CS0114"));
    }
    #endregion
}
