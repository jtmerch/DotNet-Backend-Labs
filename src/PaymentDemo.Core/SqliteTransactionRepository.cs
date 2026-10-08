using Microsoft.Data.Sqlite;

namespace PaymentDemo.Core;

/// <summary>
/// Shared-file store for Transaction records. Opens a short-lived connection per
/// operation rather than holding one open, since SQLite handles that cheaply and it
/// avoids any question of thread-safety on a single shared connection.
/// </summary>
public sealed class SqliteTransactionRepository : ITransactionRepository
{
    private readonly string _connectionString;

    public SqliteTransactionRepository(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();

        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS transactions (
                transaction_id TEXT PRIMARY KEY,
                idempotency_key TEXT NOT NULL,
                demo_account_id TEXT NOT NULL,
                amount_minor_units INTEGER NOT NULL,
                currency TEXT NOT NULL,
                requested_outcome TEXT NULL,
                state TEXT NOT NULL,
                attempts INTEGER NOT NULL,
                processor_reference TEXT NULL,
                failure_explanation TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS idx_transactions_idempotency
                ON transactions (demo_account_id, idempotency_key);
            """;
        command.ExecuteNonQuery();
    }

    public Transaction Add(Transaction transaction)
    {
        using SqliteConnection connection = Open();
        using SqliteTransaction sqlTransaction = connection.BeginTransaction();

        try
        {
            using (SqliteCommand insert = connection.CreateCommand())
            {
                insert.Transaction = sqlTransaction;
                insert.CommandText = """
                    INSERT INTO transactions
                        (transaction_id, idempotency_key, demo_account_id, amount_minor_units,
                         currency, requested_outcome, state, attempts, processor_reference,
                         failure_explanation, created_at, updated_at)
                    VALUES
                        ($transactionId, $idempotencyKey, $demoAccountId, $amountMinorUnits,
                         $currency, $requestedOutcome, $state, $attempts, $processorReference,
                         $failureExplanation, $createdAt, $updatedAt);
                    """;
                BindTransactionParameters(insert, transaction);
                insert.ExecuteNonQuery();
            }

            sqlTransaction.Commit();
            return transaction;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT
        {
            sqlTransaction.Rollback();

            Transaction? existing = FindByIdempotencyKeyOnConnection(connection, transaction.DemoAccountId, transaction.IdempotencyKey);
            return existing
                ?? throw new InvalidOperationException(
                    "Idempotency key conflicted on insert but no matching row could be found.");
        }
    }

    public Transaction? FindById(string transactionId)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM transactions WHERE transaction_id = $transactionId;";
        command.Parameters.AddWithValue("$transactionId", transactionId);

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadTransaction(reader) : null;
    }

    public Transaction? FindByIdempotencyKey(string demoAccountId, string idempotencyKey)
    {
        using SqliteConnection connection = Open();
        return FindByIdempotencyKeyOnConnection(connection, demoAccountId, idempotencyKey);
    }

    public Transaction? ClaimNextPending(TimeSpan staleSubmittedThreshold)
    {
        string staleCutoff = (DateTimeOffset.UtcNow - staleSubmittedThreshold).ToString("O");

        using SqliteConnection connection = Open();
        using SqliteTransaction sqlTransaction = connection.BeginTransaction();

        using SqliteCommand select = connection.CreateCommand();
        select.Transaction = sqlTransaction;
        select.CommandText = """
            SELECT * FROM transactions
            WHERE state = 'Pending'
               OR (state = 'Submitted' AND updated_at < $staleCutoff)
            ORDER BY CASE WHEN state = 'Pending' THEN created_at ELSE updated_at END
            LIMIT 1;
            """;
        select.Parameters.AddWithValue("$staleCutoff", staleCutoff);

        Transaction? candidate;
        using (SqliteDataReader reader = select.ExecuteReader())
        {
            candidate = reader.Read() ? ReadTransaction(reader) : null;
        }

        if (candidate is null)
        {
            sqlTransaction.Commit();
            return null;
        }

        candidate.State = TransactionState.Submitted;
        candidate.UpdatedAt = DateTimeOffset.UtcNow;

        using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = sqlTransaction;
            update.CommandText = """
                UPDATE transactions SET state = $state, updated_at = $updatedAt
                WHERE transaction_id = $transactionId
                  AND (state = 'Pending' OR (state = 'Submitted' AND updated_at < $staleCutoff));
                """;
            update.Parameters.AddWithValue("$state", candidate.State.ToString());
            update.Parameters.AddWithValue("$updatedAt", candidate.UpdatedAt.ToString("O"));
            update.Parameters.AddWithValue("$transactionId", candidate.TransactionId);
            update.Parameters.AddWithValue("$staleCutoff", staleCutoff);
            update.ExecuteNonQuery();
        }

        sqlTransaction.Commit();
        return candidate;
    }

    public void Save(Transaction transaction)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE transactions
            SET state = $state, attempts = $attempts, processor_reference = $processorReference,
                failure_explanation = $failureExplanation, updated_at = $updatedAt
            WHERE transaction_id = $transactionId;
            """;
        command.Parameters.AddWithValue("$state", transaction.State.ToString());
        command.Parameters.AddWithValue("$attempts", transaction.Attempts);
        command.Parameters.AddWithValue("$processorReference", transaction.ProcessorReference ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$failureExplanation", transaction.FailureExplanation ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", transaction.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$transactionId", transaction.TransactionId);
        command.ExecuteNonQuery();
    }

    private static Transaction? FindByIdempotencyKeyOnConnection(SqliteConnection connection, string demoAccountId, string idempotencyKey)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM transactions
            WHERE demo_account_id = $demoAccountId AND idempotency_key = $idempotencyKey;
            """;
        command.Parameters.AddWithValue("$demoAccountId", demoAccountId);
        command.Parameters.AddWithValue("$idempotencyKey", idempotencyKey);

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadTransaction(reader) : null;
    }

    private SqliteConnection Open()
    {
        SqliteConnection connection = new(_connectionString);
        connection.Open();
        using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout = 5000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static void BindTransactionParameters(SqliteCommand command, Transaction transaction)
    {
        command.Parameters.AddWithValue("$transactionId", transaction.TransactionId);
        command.Parameters.AddWithValue("$idempotencyKey", transaction.IdempotencyKey);
        command.Parameters.AddWithValue("$demoAccountId", transaction.DemoAccountId);
        command.Parameters.AddWithValue("$amountMinorUnits", transaction.AmountMinorUnits);
        command.Parameters.AddWithValue("$currency", transaction.Currency);
        command.Parameters.AddWithValue("$requestedOutcome", transaction.RequestedOutcome ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$state", transaction.State.ToString());
        command.Parameters.AddWithValue("$attempts", transaction.Attempts);
        command.Parameters.AddWithValue("$processorReference", transaction.ProcessorReference ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$failureExplanation", transaction.FailureExplanation ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", transaction.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", transaction.UpdatedAt.ToString("O"));
    }

    private static Transaction ReadTransaction(SqliteDataReader reader)
    {
        return new Transaction
        {
            TransactionId = reader.GetString(reader.GetOrdinal("transaction_id")),
            IdempotencyKey = reader.GetString(reader.GetOrdinal("idempotency_key")),
            DemoAccountId = reader.GetString(reader.GetOrdinal("demo_account_id")),
            AmountMinorUnits = reader.GetInt64(reader.GetOrdinal("amount_minor_units")),
            Currency = reader.GetString(reader.GetOrdinal("currency")),
            RequestedOutcome = reader.IsDBNull(reader.GetOrdinal("requested_outcome"))
                ? null
                : reader.GetString(reader.GetOrdinal("requested_outcome")),
            State = Enum.Parse<TransactionState>(reader.GetString(reader.GetOrdinal("state"))),
            Attempts = reader.GetInt32(reader.GetOrdinal("attempts")),
            ProcessorReference = reader.IsDBNull(reader.GetOrdinal("processor_reference"))
                ? null
                : reader.GetString(reader.GetOrdinal("processor_reference")),
            FailureExplanation = reader.IsDBNull(reader.GetOrdinal("failure_explanation"))
                ? null
                : reader.GetString(reader.GetOrdinal("failure_explanation")),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_at")))
        };
    }
}
