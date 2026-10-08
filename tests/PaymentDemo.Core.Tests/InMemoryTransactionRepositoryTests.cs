using PaymentDemo.Core;

namespace PaymentDemo.Core.Tests;

public sealed class InMemoryTransactionRepositoryTests : TransactionRepositoryContractTests
{
    protected override ITransactionRepository CreateRepository() => new InMemoryTransactionRepository();
}
