using Macaron.PropertyAccessor.Properties.Impl;

namespace Macaron.PropertyAccessor.Properties;

public static class ReadOnlyProperty
{
    public static class For<T>
    {
        public static IReadOnlyProperty<T, TProperty> Of<TProperty>(Func<T, TProperty> getter)
        {
            return new ReadOnlyProperty<T, TProperty>(getter);
        }

        public static IReadOnlyProperty<T, TProperty> Of<TContext, TProperty>(
            TContext context,
            Func<T, TContext, TProperty> getter
        )
        {
            return new ReadOnlyPropertyWithContext<T, TContext, TProperty>(context, getter);
        }
    }

    public static IReadOnlyProperty<TProperty> Of<TProperty>(Func<TProperty> getter)
    {
        return new ReadOnlyProperty<TProperty>(getter);
    }

    public static IReadOnlyProperty<TProperty> Of<TContext, TProperty>(
        TContext context,
        Func<TContext, TProperty> getter
    )
    {
        return new ReadOnlyPropertyWithContext<TContext, TProperty>(context, getter);
    }

    public static IReadOnlyProperty<T, TProperty> Of<T, TProperty>(Func<T, TProperty> getter)
    {
        return new ReadOnlyProperty<T, TProperty>(getter);
    }

    public static IReadOnlyProperty<T, TProperty> Of<T, TContext, TProperty>(
        TContext context,
        Func<T, TContext, TProperty> getter
    )
    {
        return new ReadOnlyPropertyWithContext<T, TContext, TProperty>(context, getter);
    }
}
