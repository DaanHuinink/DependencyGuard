namespace Customers
{
    public sealed record Customer(int Id, string Name);
}

namespace Orders
{
    using System.Text; // Allowed: .* → .*.
    using Customers;   // Allowed: .* → .*.

    public sealed class OrderSummary
    {
        public string Describe(Customer customer, decimal amount)
        {
            StringBuilder builder = new();
            builder.Append(customer.Name).Append(": ").Append(amount);
            return builder.ToString();
        }
    }
}

namespace Plugins
{
    using System.Reflection; // DG0001: System.Reflection is denied for every namespace.

    public sealed class PluginLoader
    {
        public object? Create(string typeName)
        {
            Type? type = Type.GetType(typeName);
            ConstructorInfo? constructor = type?.GetConstructor(Type.EmptyTypes);
            return constructor?.Invoke(null);
        }
    }
}
