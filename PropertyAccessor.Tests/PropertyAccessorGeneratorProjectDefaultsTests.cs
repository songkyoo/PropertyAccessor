using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using static Macaron.PropertyAccessor.Tests.Helper;

namespace Macaron.PropertyAccessor.Tests;

[TestFixture]
public class PropertyAccessorGeneratorProjectDefaultsTests
{
    #region Static
    private static AnalyzerConfigOptionsProvider GetGlobalOptions(Project project, string options)
    {
        const string fileName = "Default.globalconfig";
        var config = SourceText.From($"is_global = true\n\n{options}");

        var document = project.AddAnalyzerConfigDocument(
            fileName,
            config,
            filePath: Path.Combine(Path.GetTempPath(), fileName)
        );

        return document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider;
    }

    private static AnalyzerConfigOptionsProvider GetGlobalOptions(AdhocWorkspace workspace, string options)
    {
        return GetGlobalOptions(workspace.AddProject("ProjectDefaults", LanguageNames.CSharp), options);
    }
    #endregion

    #region Tests
    [Test]
    public void Should_MergeProjectDefaultsWithTypeAndFieldOptions()
    {
        var compilation = CreateCompilation(
            """
            using Macaron.PropertyAccessor;

            public partial class Standalone
            {
                [GetSet]
                private int s_Speed;
            }

            [PropertyGenerationDefaults(AccessModifier = PropertyAccessModifier.Public)]
            public partial class Outer
            {
                [PropertyGenerationDefaults(NamingRule = PropertyNamingRule.PascalCase)]
                public partial class Inner
                {
                    [Get]
                    private int s_Size;

                    [GetSet("Weight", AccessModifier = PropertyAccessModifier.Private)]
                    private int s_Alias;
                }
            }
            """
        );
        using var workspace = new AdhocWorkspace();
        var options = GetGlobalOptions(
            workspace,
            """
            macaron_property_accessor.access_modifier = internal
            macaron_property_accessor.prefix_pattern = ^s_
            macaron_property_accessor.naming_rule = camelcase
            """
        );
        var driver = CreateGeneratorDriver().WithUpdatedAnalyzerConfigOptions(options);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var standalone = output.GetTypeByMetadataName("Standalone")!;
        var inner = output.GetTypeByMetadataName("Outer+Inner")!;
        var speed = (IPropertySymbol)standalone.GetMembers("speed").Single();
        var size = (IPropertySymbol)inner.GetMembers("Size").Single();
        var weight = (IPropertySymbol)inner.GetMembers("Weight").Single();
        var errors = output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(errors, Is.Empty);
        Assert.That(speed.DeclaredAccessibility, Is.EqualTo(Accessibility.Internal));
        Assert.That(size.DeclaredAccessibility, Is.EqualTo(Accessibility.Public));
        Assert.That(weight.DeclaredAccessibility, Is.EqualTo(Accessibility.Private));
    }

    [TestCase("null", "Value")]
    [TestCase("\"\"", "M_Value")]
    public void Should_OverrideProjectPrefixPattern_When_TypeSpecifiesNullOrEmpty(string pattern, string propertyName)
    {
        var compilation = CreateCompilation(
            $$"""
            using Macaron.PropertyAccessor;

            [PropertyGenerationDefaults(PrefixPattern = {{pattern}})]
            public partial class Foo
            {
                [Get]
                private int m_Value;
            }
            """
        );
        using var workspace = new AdhocWorkspace();
        var options = GetGlobalOptions(workspace, "macaron_property_accessor.prefix_pattern = ^m_V");
        var driver = CreateGeneratorDriver().WithUpdatedAnalyzerConfigOptions(options);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var type = output.GetTypeByMetadataName("Foo")!;
        var errors = output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(errors, Is.Empty);
        Assert.That(type.GetMembers(propertyName), Has.One.InstanceOf<IPropertySymbol>());
    }

    [Test]
    public void Should_SkipPrefixRemoval_When_ProjectPrefixPatternIsEmpty()
    {
        var compilation = CreateCompilation(
            """
            using Macaron.PropertyAccessor;

            public partial class Foo
            {
                [Get]
                private int m_value;
            }
            """
        );
        using var workspace = new AdhocWorkspace();
        var options = GetGlobalOptions(workspace, "macaron_property_accessor.prefix_pattern =");
        var driver = CreateGeneratorDriver().WithUpdatedAnalyzerConfigOptions(options);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var type = output.GetTypeByMetadataName("Foo")!;
        var errors = output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(errors, Is.Empty);
        Assert.That(type.GetMembers("M_value"), Has.One.InstanceOf<IPropertySymbol>());
    }

    [TestCase("access_modifier", "2", "MPROP0012")]
    [TestCase("access_modifier", "File", "MPROP0012")]
    [TestCase("naming_rule", "Unknown", "MPROP0012")]
    [TestCase("prefix_pattern", "[invalid", "MPROP0004")]
    public void Should_ReportInvalidProjectOptionOnceAndUseBuiltInDefault(string key, string value, string diagnosticId)
    {
        var compilation = CreateCompilation(
            """
            using Macaron.PropertyAccessor;

            public partial class Foo
            {
                [Get]
                private int _value;
            }

            public partial class Bar
            {
                [GetSet]
                private int _value;
            }
            """
        );
        using var workspace = new AdhocWorkspace();
        var options = GetGlobalOptions(workspace, $"macaron_property_accessor.{key} = {value}");
        var driver = CreateGeneratorDriver().WithUpdatedAnalyzerConfigOptions(options);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var diagnostic = diagnostics.Single();
        var fooValue = (IPropertySymbol)output.GetTypeByMetadataName("Foo")!.GetMembers("Value").Single();
        var barValue = (IPropertySymbol)output.GetTypeByMetadataName("Bar")!.GetMembers("Value").Single();
        var errors = output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        Assert.That(diagnostic.Id, Is.EqualTo(diagnosticId));
        Assert.That(diagnostic.GetMessage(), Does.Contain(value));
        Assert.That(errors, Is.Empty);
        Assert.That(fooValue.DeclaredAccessibility, Is.EqualTo(Accessibility.Public));
        Assert.That(barValue.DeclaredAccessibility, Is.EqualTo(Accessibility.Public));
    }

    [Test]
    public void Should_UpdateAndCacheGenerationModels_When_OnlyProjectOptionsChange()
    {
        var compilation = CreateCompilation(
            """
            using Macaron.PropertyAccessor;

            public partial class Foo
            {
                [Get]
                private int _value;
            }

            [PropertyGenerationDefaults(AccessModifier = PropertyAccessModifier.Public)]
            public partial class Bar
            {
                [Get]
                private int _value;
            }
            """
        );
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("ProjectDefaults", LanguageNames.CSharp);
        var initialOptions = GetGlobalOptions(project, "macaron_property_accessor.access_modifier = Internal");
        var updatedOptions = GetGlobalOptions(project, "macaron_property_accessor.access_modifier = Private");
        var driver = CreateGeneratorDriver(trackIncrementalGeneratorSteps: true);
        driver = driver.WithUpdatedAnalyzerConfigOptions(initialOptions);
        driver = driver.RunGenerators(compilation);

        driver = driver.WithUpdatedAnalyzerConfigOptions(updatedOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var result = driver.GetRunResult().Results.Single();
        var reasons = result.TrackedSteps["GenerationModel"]
            .SelectMany(step => step.Outputs)
            .Select(generation => generation.Reason)
            .ToArray();
        var fooValue = (IPropertySymbol)output.GetTypeByMetadataName("Foo")!.GetMembers("Value").Single();
        var barValue = (IPropertySymbol)output.GetTypeByMetadataName("Bar")!.GetMembers("Value").Single();

        Assert.That(diagnostics, Is.Empty);
        Assert.That(reasons, Is.EquivalentTo(new[] { IncrementalStepRunReason.Modified, IncrementalStepRunReason.Cached }));
        Assert.That(fooValue.DeclaredAccessibility, Is.EqualTo(Accessibility.Private));
        Assert.That(barValue.DeclaredAccessibility, Is.EqualTo(Accessibility.Public));
    }

    [Test]
    public void Should_ClearProjectDiagnosticAndCacheGenerationModel_When_InvalidOptionIsCorrected()
    {
        var compilation = CreateCompilation(
            """
            using Macaron.PropertyAccessor;

            public partial class Foo
            {
                [Get]
                private int _value;
            }
            """
        );
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("ProjectDefaults", LanguageNames.CSharp);
        var validOptions = GetGlobalOptions(project, "macaron_property_accessor.access_modifier = Public");
        var invalidOptions = GetGlobalOptions(project, "macaron_property_accessor.access_modifier = Invalid");
        var driver = CreateGeneratorDriver(trackIncrementalGeneratorSteps: true);
        driver = driver.WithUpdatedAnalyzerConfigOptions(validOptions).RunGenerators(compilation);

        driver = driver.WithUpdatedAnalyzerConfigOptions(invalidOptions).RunGenerators(compilation);
        var invalidResult = driver.GetRunResult().Results.Single();
        var invalidGeneration = invalidResult.TrackedSteps["GenerationModel"]
            .SelectMany(step => step.Outputs)
            .Single(output => output.Value != null);

        driver = driver.WithUpdatedAnalyzerConfigOptions(validOptions).RunGenerators(compilation);
        var correctedResult = driver.GetRunResult().Results.Single();
        var correctedGeneration = correctedResult.TrackedSteps["GenerationModel"]
            .SelectMany(step => step.Outputs)
            .Single(output => output.Value != null);

        Assert.That(invalidResult.Diagnostics.Single().Id, Is.EqualTo("MPROP0012"));
        Assert.That(invalidGeneration.Reason, Is.EqualTo(IncrementalStepRunReason.Cached));
        Assert.That(correctedResult.Diagnostics, Is.Empty);
        Assert.That(correctedResult.GeneratedSources, Has.Length.EqualTo(1));
        Assert.That(correctedGeneration.Reason, Is.EqualTo(IncrementalStepRunReason.Cached));
    }
    #endregion
}
