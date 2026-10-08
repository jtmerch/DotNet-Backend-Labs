namespace PaymentDemo.Core;

/// <summary>
/// Finds (or creates) the shared data directory at the repository root, so PaymentApi
/// and TransactionWorker agree on the same SQLite file without either one hardcoding
/// an absolute path. Demo-only convenience - a real deployment would be given a
/// connection string through configuration instead of discovering one.
/// </summary>
public static class SqliteDatabasePathResolver
{
    private const string SolutionFileName = "PaymentDemo.slnx";
    private const string DatabaseFileName = "payments.db";

    public static string ResolveDefaultDatabasePath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException(
                $"Could not locate {SolutionFileName} above {AppContext.BaseDirectory}; " +
                "pass an explicit connection string instead of relying on auto-discovery.");
        }

        string dataDirectory = Path.Combine(directory.FullName, "data");
        Directory.CreateDirectory(dataDirectory);

        return Path.Combine(dataDirectory, DatabaseFileName);
    }
}
