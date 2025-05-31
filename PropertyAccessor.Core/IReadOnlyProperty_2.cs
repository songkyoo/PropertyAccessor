namespace Macaron.PropertyAccessor;

public interface IReadOnlyProperty<in T, out TProperty>
{
    TProperty Get(T instance);
}
