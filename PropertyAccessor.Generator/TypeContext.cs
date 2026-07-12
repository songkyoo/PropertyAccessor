using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Macaron.PropertyAccessor;

public sealed record TypeContext(
    INamedTypeSymbol Symbol,
    PropertyAccessModifier AccessModifier,
    Regex PrefixRegex,
    PropertyNamingRule NamingRule,
    CSharpCompilation Compilation
);
