using Macaron.PropertyAccessor.Properties.Impl;

namespace Macaron.PropertyAccessor.Properties;

public static class ObservableProperty
{
    public static class For<T>
    {
        public static IReadWriteProperty<T, TProperty> Of<TProperty>(
            TProperty initialValue,
            Func<T, TProperty, TProperty, bool>? onBeforeSet = null,
            Action<T, TProperty, TProperty>? onAfterSet = null
        )
        {
            return new ObservableProperty<T, TProperty>(initialValue, onBeforeSet, onAfterSet);
        }
    }

    public static IReadWriteProperty<TProperty> Of<TProperty>(
        TProperty initialValue,
        Func<TProperty, TProperty, bool>? onBeforeSet = null,
        Action<TProperty, TProperty>? onAfterSet = null
    )
    {
        return new ObservableProperty<TProperty>(initialValue, onBeforeSet, onAfterSet);
    }

    public static IReadWriteProperty<T, TProperty> Of<T, TProperty>(
        TProperty initialValue,
        Func<T, TProperty, TProperty, bool>? onBeforeSet = null,
        Action<T, TProperty, TProperty>? onAfterSet = null
    )
    {
        return new ObservableProperty<T, TProperty>(initialValue, onBeforeSet, onAfterSet);
    }
}
