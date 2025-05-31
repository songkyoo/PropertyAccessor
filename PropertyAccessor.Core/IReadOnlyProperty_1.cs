namespace Macaron.PropertyAccessor;

public interface IReadOnlyProperty<out TProperty>
{
    TProperty Get<T>(T instance);
}
