namespace Macaron.PropertyAccessor.Properties;

public static class MappedProperty
{
    public static class For<T>
    {
        public static IReadOnlyProperty<T, TProperty> Of<TValue, TProperty>(
            TValue value,
            Func<T, TValue, TProperty> getter
        )
        {
            return new MappedProperty<T, TValue, TProperty>(value, getter);
        }
    }

    public static IReadOnlyProperty<TProperty> Of<TValue, TProperty>(TValue value, Func<TValue, TProperty> getter)
    {
        return new MappedProperty<TValue, TProperty>(value, getter);
    }

    public static IReadOnlyProperty<T, TProperty> Of<T, TValue, TProperty>(
        TValue value,
        Func<T, TValue, TProperty> getter
    )
    {
        return new MappedProperty<T, TValue, TProperty>(value, getter);
    }
}
