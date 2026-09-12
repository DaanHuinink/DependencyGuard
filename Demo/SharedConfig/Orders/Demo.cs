namespace Domain
{
    public sealed record Order(int Id, string Description, decimal Amount);

    public interface IOrderRepository
    {
        Order? FindById(int id);
        void Save(Order order);
    }
}

namespace Infrastructure
{
    using Domain; // Allowed: Infrastructure → Domain.

    public sealed class OrderRepository : IOrderRepository
    {
        private readonly List<Order> _store = [];

        public Order? FindById(int id)
        {
            return _store.FirstOrDefault(o => o.Id == id);
        }

        public void Save(Order order)
        {
            _store.Add(order);
        }
    }
}

namespace Application
{
    using Domain;         // Allowed: Application → Domain.
    using Infrastructure; // DG0001: no rule allows Application → Infrastructure.

    public sealed class OrderService(OrderRepository repository)
    {
        public Order? GetOrder(int id)
        {
            return repository.FindById(id);
        }
    }
}
