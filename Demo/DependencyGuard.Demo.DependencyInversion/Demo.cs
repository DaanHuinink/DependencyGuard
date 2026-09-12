namespace Domain
{
    public sealed class Customer
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
    }

    public interface ICustomerRepository
    {
        Customer? FindById(int id);
        void Save(Customer customer);
    }
}

namespace Application.Persistence
{
    using Domain; // Allowed: Application.* → Domain.*.

    public sealed class CustomerRepository : ICustomerRepository
    {
        private readonly Dictionary<int, Customer> _store = new();

        public Customer? FindById(int id)
        {
            return _store.GetValueOrDefault(id);
        }

        public void Save(Customer customer)
        {
            _store[customer.Id] = customer;
        }
    }
}

namespace Application
{
    using Domain; // Allowed: Application.* → Domain.*.

    public sealed class CustomerUseCase(ICustomerRepository repository)
    {
        public Customer? GetCustomer(int id)
        {
            return repository.FindById(id);
        }

        public void RegisterCustomer(int id, string name)
        {
            repository.Save(new Customer { Id = id, Name = name });
        }
    }
}

namespace Domain
{
    using Application.Persistence; // DG0001: no rule allows Domain → Application.Persistence.

    public sealed class CustomerService
    {
        private readonly CustomerRepository _repo = new();

        public Customer? GetById(int id)
        {
            return _repo.FindById(id);
        }
    }
}
