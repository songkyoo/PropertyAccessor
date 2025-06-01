namespace Macaron.PropertyAccessor.Properties;

public static class ReadOnlyProperty
{
    public static class For<T>
    {
        public static IReadOnlyProperty<T, TProperty> Of<TProperty>(Func<T, TProperty> getter)
        {
            return new ReadOnlyProperty<T, TProperty>(getter);
        }
    }

    public static IReadOnlyProperty<TProperty> Of<TProperty>(Func<TProperty> getter)
    {
        return new ReadOnlyProperty<TProperty>(getter);
    }

    public static IReadOnlyProperty<T, TProperty> Of<T, TProperty>(Func<T, TProperty> getter)
    {
        return new ReadOnlyProperty<T, TProperty>(getter);
    }
}
