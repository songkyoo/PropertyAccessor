namespace Macaron.PropertyAccessor;

public static class MappedProperty
{
    public static class For<T>
    {
        public static IReadOnlyProperty<T, TProperty> Of<TRaw, TProperty>(
            TRaw value,
            Func<T, TRaw, TProperty> getter
        )
        {
            return new MappedProperty<T, TRaw, TProperty>(value, getter);
        }
    }

    public static IReadOnlyProperty<TProperty> Of<TRaw, TProperty>(TRaw value, Func<TRaw, TProperty> getter)
    {
        return new MappedProperty<TRaw, TProperty>(value, getter);
    }

    public static IReadOnlyProperty<T, TProperty> Of<T, TRaw, TProperty>(TRaw value, Func<T, TRaw, TProperty> getter)
    {
        return new MappedProperty<T, TRaw, TProperty>(value, getter);
    }
}
