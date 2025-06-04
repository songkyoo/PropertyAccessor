using Macaron.PropertyAccessor.Properties.Impl;

namespace Macaron.PropertyAccessor.Properties;

public static class ReadWriteProperty
{
    public static class For<T>
    {
        public static IReadWriteProperty<T, TProperty> Of<TProperty>(
            Func<T, TProperty> getter,
            Action<T, TProperty> setter
        )
        {
            return new ReadWriteProperty<T, TProperty>(getter, setter);
        }

        public static IReadWriteProperty<T, TProperty> Of<TValue, TProperty>(
            TValue initialValue,
            Func<T, TValue, TProperty> getter,
            Func<T, TValue, TProperty, TValue> setter
        )
        {
            return new ReadWritePropertyWithValue<T, TValue, TProperty>(initialValue, getter, setter);
        }
    }

    public static IReadWriteProperty<TProperty> Of<TProperty>(Func<TProperty> getter, Action<TProperty> setter)
    {
        return new ReadWriteProperty<TProperty>(getter, setter);
    }

    public static IReadWriteProperty<TProperty> Of<TValue, TProperty>(
        TValue initialValue,
        Func<TValue, TProperty> getter,
        Func<TValue, TProperty, TValue> setter
    )
    {
        return new ReadWritePropertyWithValue<TValue, TProperty>(initialValue, getter, setter);
    }

    public static IReadWriteProperty<T, TProperty> Of<T, TProperty>(
        Func<T, TProperty> getter,
        Action<T, TProperty> setter
    )
    {
        return new ReadWriteProperty<T, TProperty>(getter, setter);
    }

    public static IReadWriteProperty<T, TProperty> Of<T, TValue, TProperty>(
        TValue initialValue,
        Func<T, TValue, TProperty> getter,
        Func<T, TValue, TProperty, TValue> setter
    )
    {
        return new ReadWritePropertyWithValue<T, TValue, TProperty>(initialValue, getter, setter);
    }
}
