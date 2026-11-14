using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Macaron.PropertyAccessor;

internal static class PropertyOverrideAnalysis
{
    #region Static
    public static AnalysisResult<PropertyModel> ApplyOverride(
        IFieldSymbol field,
        ITypeSymbol propertyType,
        PropertyModel model,
        CSharpCompilation compilation,
        CancellationToken cancellationToken
    )
    {
        var containingType = field.ContainingType;
        var baseProperty = FindBaseProperty(containingType, model.Name, compilation, cancellationToken);

        if (baseProperty == null
            || baseProperty.IsStatic
            || baseProperty.IsSealed
            || !baseProperty.IsAbstract
            || !SymbolEqualityComparer.Default.Equals(baseProperty.Type, propertyType)
        )
        {
            return new AnalysisResult<PropertyModel>.Success(model);
        }

        var mismatchReason = baseProperty.DeclaringSyntaxReferences.IsEmpty
            ? GetMetadataPropertyMismatchReason(containingType, model, compilation, cancellationToken)
            : GetMismatchReason(baseProperty, containingType, model, compilation);

        if (mismatchReason != null)
        {
            return new AnalysisResult<PropertyModel>.Failure(Diagnostics.IncompatiblePropertyOverride(
                field,
                propertyName: model.Name,
                baseProperty,
                mismatchReason
            ));
        }

        return new AnalysisResult<PropertyModel>.Success(model with { IsOverride = true });
    }

    private static IPropertySymbol? FindBaseProperty(
        INamedTypeSymbol containingType,
        string propertyName,
        Compilation compilation,
        CancellationToken cancellationToken
    )
    {
        var memberName = propertyName.StartsWith("@", StringComparison.Ordinal)
            ? propertyName[1..]
            : propertyName;

        for (var baseType = containingType.BaseType; baseType != null; baseType = baseType.BaseType)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var members = baseType.GetMembers(memberName)
                .Where(member => compilation.IsSymbolAccessibleWithin(member, containingType))
                .ToArray();

            if (members.Length == 0)
            {
                continue;
            }

            return members.OfType<IPropertySymbol>().FirstOrDefault(property => !property.IsIndexer);
        }

        return null;
    }

    private static string? GetMismatchReason(
        IPropertySymbol baseProperty,
        INamedTypeSymbol containingType,
        PropertyModel model,
        Compilation compilation
    )
    {
        if (baseProperty.RefKind != RefKind.None)
        {
            return "ref-returning properties are not supported";
        }

        if (baseProperty.IsRequired)
        {
            return "required properties are not supported";
        }

        var expectedAccessibility = GetOverrideAccessibility(baseProperty, containingType);
        var generatedAccessibility = GetAccessibility(model.AccessModifier);

        if (generatedAccessibility != expectedAccessibility)
        {
            return $"property accessibility must be '{GetAccessibilityName(expectedAccessibility)}', "
                + $"but generated accessibility is '{GetAccessibilityName(generatedAccessibility)}'";
        }

        var hasSetter = model.AccessorKind == PropertyAccessorKind.GetSet && model.SetterMethodName == null;
        var baseSetter = baseProperty.SetMethod;
        var generatedAccessors = GetAccessorList(hasGetter: true, hasSetter, model.IsInitAccessor);
        var expectedAccessors = GetAccessorList(
            hasGetter: baseProperty.GetMethod != null,
            hasSetter: baseSetter != null,
            isInitAccessor: baseSetter?.IsInitOnly == true
        );

        if (generatedAccessors != expectedAccessors)
        {
            return $"accessors must be '{expectedAccessors}', but generated accessors are '{generatedAccessors}'";
        }

        var getterMismatch = GetAccessorMismatchReason(
            baseProperty.GetMethod!,
            containingType,
            generatedAccessibility,
            compilation,
            accessorName: "get"
        );

        if (getterMismatch != null)
        {
            return getterMismatch;
        }

        return baseSetter == null
            ? null
            : GetAccessorMismatchReason(
                baseSetter,
                containingType,
                generatedAccessibility,
                compilation,
                accessorName: baseSetter.IsInitOnly ? "init" : "set"
            );
    }

    private static string? GetMetadataPropertyMismatchReason(
        INamedTypeSymbol containingType,
        PropertyModel model,
        CSharpCompilation compilation,
        CancellationToken cancellationToken
    )
    {
        // 기본 메타데이터 조회는 private 접근자를 생략할 수 있어 선언 전체를 비교할 수 없다.
        // 전체 멤버를 가져온 컴파일에서 파생 타입도 다시 바인딩해 접근 가능 여부를 함께 검증한다.
        var fullCompilation = compilation.WithOptions(
            compilation.Options.WithMetadataImportOptions(MetadataImportOptions.All)
        );
        var declaration = containingType.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken);
        var semanticModel = fullCompilation.GetSemanticModel(declaration.SyntaxTree);
        var fullContainingType = (INamedTypeSymbol)semanticModel.GetDeclaredSymbol(declaration, cancellationToken)!;
        var fullBaseProperty = FindBaseProperty(
            fullContainingType,
            model.Name,
            fullCompilation,
            cancellationToken
        )!;

        return GetMismatchReason(fullBaseProperty, fullContainingType, model, fullCompilation);
    }

    private static string? GetAccessorMismatchReason(
        IMethodSymbol accessor,
        INamedTypeSymbol containingType,
        Accessibility generatedAccessibility,
        Compilation compilation,
        string accessorName
    )
    {
        if (!compilation.IsSymbolAccessibleWithin(accessor, containingType))
        {
            return $"the inherited '{accessorName}' accessor is not accessible to the generated property";
        }

        var expectedAccessibility = GetOverrideAccessibility(accessor, containingType);

        return generatedAccessibility == expectedAccessibility
            ? null
            : $"'{accessorName}' accessibility must be '{GetAccessibilityName(expectedAccessibility)}', "
                + $"but generated accessibility is '{GetAccessibilityName(generatedAccessibility)}'";
    }

    private static Accessibility GetOverrideAccessibility(ISymbol member, INamedTypeSymbol containingType)
    {
        // 내부 접근 권한을 공유하지 않는 다른 어셈블리의 protected internal 멤버는 protected로 재정의한다.
        return member.DeclaredAccessibility == Accessibility.ProtectedOrInternal
            && !SymbolEqualityComparer.Default.Equals(member.ContainingAssembly, containingType.ContainingAssembly)
            && !member.ContainingAssembly.GivesAccessTo(containingType.ContainingAssembly)
            ? Accessibility.Protected
            : member.DeclaredAccessibility;
    }

    private static string GetAccessorList(bool hasGetter, bool hasSetter, bool isInitAccessor)
    {
        var getter = hasGetter ? "get;" : "";
        var setter = hasSetter ? (isInitAccessor ? "init;" : "set;") : "";

        return hasGetter && hasSetter ? $"{getter} {setter}" : getter + setter;
    }

    private static Accessibility GetAccessibility(PropertyAccessModifier modifier)
    {
        return modifier switch
        {
            PropertyAccessModifier.Public => Accessibility.Public,
            PropertyAccessModifier.Protected => Accessibility.Protected,
            PropertyAccessModifier.Private => Accessibility.Private,
            PropertyAccessModifier.Internal => Accessibility.Internal,
            PropertyAccessModifier.ProtectedInternal => Accessibility.ProtectedOrInternal,
            PropertyAccessModifier.PrivateProtected => Accessibility.ProtectedAndInternal,
            _ => Accessibility.NotApplicable,
        };
    }

    private static string GetAccessibilityName(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Protected => "protected",
            Accessibility.Private => "private",
            Accessibility.Internal => "internal",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "file",
        };
    }
    #endregion
}
