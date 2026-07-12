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
        Assert.That(reasons.Count(reason => reason == IncrementalStepRunReason.Unchanged), Is.EqualTo(1));
    }
    #endregion
}
