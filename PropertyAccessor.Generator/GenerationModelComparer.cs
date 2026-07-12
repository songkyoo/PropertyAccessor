namespace Macaron.PropertyAccessor;

internal sealed class GenerationModelComparer : IEqualityComparer<GenerationModel>
{
    #region Static
    public static GenerationModelComparer Instance { get; } = new();
    #endregion

    #region IEqualityComparer<GenerationModel> Interface
    public bool Equals(GenerationModel? x, GenerationModel? y)
    {
        return ReferenceEquals(x, y)
            || x is not null
            && y is not null
            && string.Equals(x.HintName, y.HintName, StringComparison.Ordinal)
            && string.Equals(x.Type.NamespaceName, y.Type.NamespaceName, StringComparison.Ordinal)
            && x.Type.Declarations.SequenceEqual(y.Type.Declarations, StringComparer.Ordinal)
            && x.Properties.SequenceEqual(y.Properties);
    }

    public int GetHashCode(GenerationModel obj)
    {
        unchecked
        {
            var hashCode = StringComparer.Ordinal.GetHashCode(obj.HintName);
            hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(obj.Type.NamespaceName);

            foreach (var declaration in obj.Type.Declarations)
            {
                hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(declaration);
            }

            foreach (var property in obj.Properties)
            {
                hashCode = (hashCode * 397) ^ property.GetHashCode();
            }

            return hashCode;
        }
    }
    #endregion
}
