using Microsoft.CodeAnalysis.Diagnostics;

namespace Macaron.PropertyAccessor;

internal readonly record struct ProjectGenerationOptions(
    string? AccessModifier,
    string? PrefixPattern,
    string? NamingRule
)
{
    public const string AccessModifierKey = "macaron_property_accessor.access_modifier";
    public const string PrefixPatternKey = "macaron_property_accessor.prefix_pattern";
    public const string NamingRuleKey = "macaron_property_accessor.naming_rule";

    public static ProjectGenerationOptions Read(AnalyzerConfigOptions options)
    {
        options.TryGetValue(AccessModifierKey, out var accessModifier);
        options.TryGetValue(PrefixPatternKey, out var prefixPattern);
        options.TryGetValue(NamingRuleKey, out var namingRule);

        return new ProjectGenerationOptions(accessModifier, prefixPattern, namingRule);
    }
}
