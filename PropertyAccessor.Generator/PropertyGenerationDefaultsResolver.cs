using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Macaron.PropertyAccessor;

internal static class PropertyGenerationDefaultsResolver
{
    #region Static
    private static readonly Regex DefaultRegex = new(pattern: "^(_|m_)", RegexOptions.Compiled);

    public static ResolvedPropertyGenerationDefaults ResolveProjectDefaults(
        ProjectGenerationOptions options,
        ImmutableArray<Diagnostic>.Builder diagnostics
    )
    {
        var accessModifier = GetProjectEnumOption(
            key: ProjectGenerationOptions.AccessModifierKey,
            value: options.AccessModifier,
            defaultValue: PropertyAccessModifier.Public,
            diagnostics
        );

        if (accessModifier == PropertyAccessModifier.File)
        {
            diagnostics.Add(CreateInvalidProjectOptionDiagnostic(
                key: ProjectGenerationOptions.AccessModifierKey,
                value: options.AccessModifier!
            ));
            accessModifier = PropertyAccessModifier.Public;
        }

        var namingRule = GetProjectEnumOption(
            key: ProjectGenerationOptions.NamingRuleKey,
            value: options.NamingRule,
            defaultValue: PropertyNamingRule.PascalCase,
            diagnostics
        );
        var prefixPattern = IsUnsetProjectOption(options.PrefixPattern)
            ? null
            : options.PrefixPattern;

        if (!TryGetPrefixRegex(prefixPattern, out var prefixRegex))
        {
            diagnostics.Add(Diagnostic.Create(
                descriptor: Diagnostics.InvalidPrefixPatternRule,
                location: Location.None,
                messageArgs: [prefixPattern]
            ));
            prefixRegex = DefaultRegex;
        }

        return new ResolvedPropertyGenerationDefaults(accessModifier, prefixRegex, namingRule);
    }

    public static bool TryResolveTypeDefaults(
        INamedTypeSymbol typeSymbol,
        IReadOnlyDictionary<INamedTypeSymbol, AttributeData> defaultsAttributeByType,
        ResolvedPropertyGenerationDefaults projectDefaults,
        CancellationToken cancellationToken,
        out ResolvedPropertyGenerationDefaults defaults,
        [NotNullWhen(false)] out Diagnostic? diagnostic
    )
    {
        var prefixPatternArgument = GetInheritedNamedArgumentValue(
            typeSymbol,
            defaultsAttributeByType,
            name: nameof(PropertyGenerationDefaultsAttribute.PrefixPattern),
            includeNull: true,
            out var prefixDefaultsAttribute
        );
        var prefixRegex = projectDefaults.PrefixRegex;

        if (prefixDefaultsAttribute != null && !TryGetPrefixRegex(prefixPatternArgument, out prefixRegex))
        {
            defaults = default;
            diagnostic = Diagnostic.Create(
                descriptor: Diagnostics.InvalidPrefixPatternRule,
                location: prefixDefaultsAttribute
                    .ApplicationSyntaxReference?
                    .GetSyntax(cancellationToken)
                    .GetLocation(),
                messageArgs: [prefixPatternArgument]
            );

            return false;
        }

        defaults = new ResolvedPropertyGenerationDefaults(
            AccessModifier: PropertyAccessModifierResolver.Resolve(
                GetInheritedNamedArgumentValue(
                    typeSymbol,
                    defaultsAttributeByType,
                    name: nameof(PropertyGenerationDefaultsAttribute.AccessModifier)
                ),
                projectDefaults.AccessModifier
            ),
            PrefixRegex: prefixRegex,
            NamingRule: GetNamingRule(
                GetInheritedNamedArgumentValue(
                    typeSymbol,
                    defaultsAttributeByType,
                    name: nameof(PropertyGenerationDefaultsAttribute.NamingRule)
                ),
                projectDefaults.NamingRule
            )
        );
        diagnostic = null;

        return true;
    }

    private static object? GetInheritedNamedArgumentValue(
        INamedTypeSymbol typeSymbol,
        IReadOnlyDictionary<INamedTypeSymbol, AttributeData> defaultsAttributeByType,
        string name
    )
    {
        return GetInheritedNamedArgumentValue(typeSymbol, defaultsAttributeByType, name, includeNull: false, out _);
    }

    private static object? GetInheritedNamedArgumentValue(
        INamedTypeSymbol typeSymbol,
        IReadOnlyDictionary<INamedTypeSymbol, AttributeData> defaultsAttributeByType,
        string name,
        bool includeNull,
        out AttributeData? definingAttribute
    )
    {
        for (var currentType = typeSymbol; currentType != null; currentType = currentType.ContainingType)
        {
            if (!defaultsAttributeByType.TryGetValue(currentType, out var attribute))
            {
                continue;
            }

            foreach (var namedArgument in attribute.NamedArguments)
            {
                if (namedArgument.Key == name && (includeNull || namedArgument.Value.Value != null))
                {
                    definingAttribute = attribute;

                    return namedArgument.Value.Value;
                }
            }
        }

        definingAttribute = null;

        return null;
    }

    private static TEnum GetProjectEnumOption<TEnum>(
        string key,
        string? value,
        TEnum defaultValue,
        ImmutableArray<Diagnostic>.Builder diagnostics
    )
        where TEnum : struct, Enum
    {
        if (value == null || IsUnsetProjectOption(value) || value.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            return defaultValue;
        }

        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var option)
            && value.Equals(Enum.GetName(typeof(TEnum), option), StringComparison.OrdinalIgnoreCase)
        )
        {
            return option;
        }

        diagnostics.Add(CreateInvalidProjectOptionDiagnostic(key, value));

        return defaultValue;
    }

    private static Diagnostic CreateInvalidProjectOptionDiagnostic(string key, string value)
    {
        return Diagnostic.Create(
            descriptor: Diagnostics.InvalidProjectOptionRule,
            location: Location.None,
            messageArgs: [key, value]
        );
    }

    private static bool IsUnsetProjectOption(string? value)
    {
        return string.Equals(value, "unset", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetPrefixRegex(object? value, out Regex? prefixRegex)
    {
        if (value == null)
        {
            prefixRegex = DefaultRegex;

            return true;
        }

        if (value is "")
        {
            prefixRegex = null;

            return true;
        }

        try
        {
            var stringValue = (string)value;

            prefixRegex = new Regex($"{(stringValue[0] == '^' ? "" : "^")}{stringValue}");

            return true;
        }
        catch
        {
            prefixRegex = null;

            return false;
        }
    }

    private static PropertyNamingRule GetNamingRule(object? value, PropertyNamingRule defaultValue)
    {
        if (value == null)
        {
            return defaultValue;
        }

        var namingRule = (PropertyNamingRule)value;
        var isDefined = Enum.IsDefined(typeof(PropertyNamingRule), namingRule);

        return isDefined && namingRule != PropertyNamingRule.Default ? namingRule : defaultValue;
    }
    #endregion
}
