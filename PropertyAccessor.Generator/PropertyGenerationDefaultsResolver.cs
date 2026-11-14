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
            diagnostics.Add(Diagnostics.InvalidProjectOption(
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
            diagnostics.Add(Diagnostics.InvalidPrefixPattern(prefixPattern, Location.None));
            prefixRegex = DefaultRegex;
        }

        return new ResolvedPropertyGenerationDefaults(accessModifier, prefixRegex, namingRule);
    }

    public static ResolvedPropertyGenerationDefaults ResolveAssemblyDefaults(
        AttributeData? attribute,
        ResolvedPropertyGenerationDefaults projectDefaults,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        CancellationToken cancellationToken
    )
    {
        if (attribute == null)
        {
            return projectDefaults;
        }

        var hasPrefixPattern = TryGetNamedArgumentValue(
            attribute,
            name: nameof(PropertyGenerationDefaultsAttribute.PrefixPattern),
            out var prefixPattern
        );
        var prefixRegex = projectDefaults.PrefixRegex;

        if (hasPrefixPattern && !TryGetPrefixRegex(prefixPattern, out prefixRegex))
        {
            diagnostics.Add(CreateInvalidPrefixPatternDiagnostic(attribute, prefixPattern, cancellationToken));
            prefixRegex = projectDefaults.PrefixRegex;
        }

        TryGetNamedArgumentValue(
            attribute,
            name: nameof(PropertyGenerationDefaultsAttribute.AccessModifier),
            out var accessModifier
        );
        TryGetNamedArgumentValue(
            attribute,
            name: nameof(PropertyGenerationDefaultsAttribute.NamingRule),
            out var namingRule
        );

        return ApplyAttributeDefaults(projectDefaults, accessModifier, prefixRegex, namingRule);
    }

    public static bool TryResolveTypeDefaults(
        INamedTypeSymbol typeSymbol,
        IReadOnlyDictionary<INamedTypeSymbol, AttributeData> defaultsAttributeByType,
        ResolvedPropertyGenerationDefaults assemblyDefaults,
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
        var prefixRegex = assemblyDefaults.PrefixRegex;

        if (prefixDefaultsAttribute != null && !TryGetPrefixRegex(prefixPatternArgument, out prefixRegex))
        {
            defaults = default;
            diagnostic = CreateInvalidPrefixPatternDiagnostic(
                prefixDefaultsAttribute,
                prefixPatternArgument,
                cancellationToken
            );

            return false;
        }

        var accessModifier = GetInheritedNamedArgumentValue(
            typeSymbol,
            defaultsAttributeByType,
            name: nameof(PropertyGenerationDefaultsAttribute.AccessModifier)
        );
        var namingRule = GetInheritedNamedArgumentValue(
            typeSymbol,
            defaultsAttributeByType,
            name: nameof(PropertyGenerationDefaultsAttribute.NamingRule)
        );

        defaults = ApplyAttributeDefaults(assemblyDefaults, accessModifier, prefixRegex, namingRule);
        diagnostic = null;

        return true;
    }

    private static ResolvedPropertyGenerationDefaults ApplyAttributeDefaults(
        ResolvedPropertyGenerationDefaults fallback,
        object? accessModifier,
        Regex? prefixRegex,
        object? namingRule
    )
    {
        return new ResolvedPropertyGenerationDefaults(
            AccessModifier: PropertyAccessModifierResolver.Resolve(accessModifier, fallback.AccessModifier),
            PrefixRegex: prefixRegex,
            NamingRule: GetNamingRule(namingRule, fallback.NamingRule)
        );
    }

    private static Diagnostic CreateInvalidPrefixPatternDiagnostic(
        AttributeData attribute,
        object? prefixPattern,
        CancellationToken cancellationToken
    )
    {
        return Diagnostics.InvalidPrefixPattern(
            prefixPattern,
            location: attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()
        );
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
            if (!defaultsAttributeByType.TryGetValue(currentType, out var attribute)
                || !TryGetNamedArgumentValue(attribute, name, out var value)
                || (!includeNull && value == null)
            )
            {
                continue;
            }

            definingAttribute = attribute;

            return value;
        }

        definingAttribute = null;

        return null;
    }

    private static bool TryGetNamedArgumentValue(AttributeData attribute, string name, out object? value)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key == name)
            {
                value = namedArgument.Value.Value;

                return true;
            }
        }

        value = null;

        return false;
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

        diagnostics.Add(Diagnostics.InvalidProjectOption(key, value));

        return defaultValue;
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
