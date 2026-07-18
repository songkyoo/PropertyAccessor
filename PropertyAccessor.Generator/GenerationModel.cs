using System.Collections.Immutable;

namespace Macaron.PropertyAccessor;

internal sealed record GenerationModel(
    string HintName,
    TypeGenerationModel Type,
    ImmutableArray<PropertyModel> Properties
)
{
    #region IEquatable<GenerationModel> Interface
    public bool Equals(GenerationModel? other)
    {
        var stringComparer = StringComparer.Ordinal;

        return ReferenceEquals(this, other)
            || other is not null
            && stringComparer.Equals(HintName, other.HintName)
            && stringComparer.Equals(Type.NamespaceName, other.Type.NamespaceName)
            && Type.Declarations.SequenceEqual(other.Type.Declarations, stringComparer)
            && Properties.SequenceEqual(other.Properties);
    }
    #endregion

    #region Object Overrides
    public override int GetHashCode()
    {
        var stringComparer = StringComparer.Ordinal;

        unchecked
        {
            var hashCode = stringComparer.GetHashCode(HintName);

            hashCode = (hashCode * 397) ^ stringComparer.GetHashCode(Type.NamespaceName);

            foreach (var declaration in Type.Declarations)
            {
                hashCode = (hashCode * 397) ^ stringComparer.GetHashCode(declaration);
            }

            foreach (var property in Properties)
            {
                hashCode = (hashCode * 397) ^ property.GetHashCode();
            }

            return hashCode;
        }
    }
    #endregion
}
