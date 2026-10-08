using PaymentDemo.Core;

namespace PaymentDemo.Core.Tests;

public sealed class SqliteTransactionRepositoryTests : TransactionRepositoryContractTests, IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"payments_test_{Guid.NewGuid():N}.db");

    protected override ITransactionRepository CreateRepository() => new SqliteTransactionRepository(_databasePath);

    public void Dispose()
    {
        // Best-effort cleanup of the temp file; a lingering handle on a slow CI runner
        // isn't worth failing the test over.
        try
        {
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
        catch (IOException)
        {
        }
    }
}
