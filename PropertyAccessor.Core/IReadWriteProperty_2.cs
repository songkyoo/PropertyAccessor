namespace Macaron.PropertyAccessor;

public interface IReadWriteProperty<in T, TProperty> : IReadOnlyProperty<T, TProperty>
{
    void Set(T instance, TProperty value);
}
