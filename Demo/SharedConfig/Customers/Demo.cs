namespace Domain
{
    public sealed record Customer(int Id, string Name, string Email);

    public interface ICustomerRepository
    {
        Customer? FindById(int id);
        void Save(Customer customer);
    }
}

namespace Infrastructure
{
    using Domain; // Allowed: Infrastructure → Domain.

    public sealed class CustomerRepository : ICustomerRepository
    {
        private readonly List<Customer> _store = [];

        public Customer? FindById(int id)
        {
            return _store.FirstOrDefault(c => c.Id == id);
        }

        public void Save(Customer customer)
        {
            _store.Add(customer);
        }
    }
}

namespace Application
{
    using Domain;         // Allowed: Application → Domain.
    using Infrastructure; // DG0001: no rule allows Application → Infrastructure.

    public sealed class CustomerService(CustomerRepository repository)
    {
        public Customer? GetCustomer(int id)
        {
            return repository.FindById(id);
        }
    }
}
