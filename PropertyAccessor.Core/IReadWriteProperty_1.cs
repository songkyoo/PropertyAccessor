namespace Macaron.PropertyAccessor;

public interface IReadWriteProperty<TProperty> : IReadOnlyProperty<TProperty>
{
    void Set<T>(T instance, TProperty value);
}
