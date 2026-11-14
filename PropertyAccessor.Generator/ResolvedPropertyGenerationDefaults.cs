using System.Text.RegularExpressions;

namespace Macaron.PropertyAccessor;

internal readonly record struct ResolvedPropertyGenerationDefaults(
    PropertyAccessModifier AccessModifier,
    Regex? PrefixRegex,
    PropertyNamingRule NamingRule
);
