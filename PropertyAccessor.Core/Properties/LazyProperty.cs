using Macaron.PropertyAccessor.Properties.Impl;

namespace Macaron.PropertyAccessor.Properties;

public static class LazyProperty
{
    public static IReadOnlyProperty<TProperty> Of<TProperty>(bool isThreadSafe)
    {
        return new LazyProperty<TProperty>(isThreadSafe);
    }

    public static IReadOnlyProperty<TProperty> Of<TProperty>(Func<TProperty> valueFactory)
    {
        return new LazyProperty<TProperty>(valueFactory);
    }

    public static IReadOnlyProperty<TProperty> Of<TProperty>(LazyThreadSafetyMode mode)
    {
        return new LazyProperty<TProperty>(mode);
    }

    public static IReadOnlyProperty<TProperty> Of<TProperty>(Func<TProperty> valueFactory, bool isThreadSafe)
    {
        return new LazyProperty<TProperty>(valueFactory, isThreadSafe);
    }

    public static IReadOnlyProperty<TProperty> Of<TProperty>(Func<TProperty> valueFactory, LazyThreadSafetyMode mode)
    {
        return new LazyProperty<TProperty>(valueFactory, mode);
    }
}
