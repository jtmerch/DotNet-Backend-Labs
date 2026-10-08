# Distributed Payment Processing Simulator

A small simulator demonstrating service boundaries, async work handoff, idempotency,
bounded retry, and crash recovery across independently runnable .NET services. It's a
**portfolio project, not a real payment product** - no real card network, bank, or
processor involved, and no real cardholder or customer data anywhere. Everything is
synthetic.

## Architecture

```
PaymentApi  --writes-->  SQLite (data/payments.db)  <--polls--  TransactionWorker
    |                                                                  |
    | cache-aside                                                     | HTTP
    v                                                                  v
  Redis (optional)                                          ProcessorSimulator
```

- **PaymentApi** - accepts a synthetic payment, persists it `Pending`, returns
  immediately. Requires an `Idempotency-Key` header; a repeat key returns the original
  transaction instead of creating a new one.
- **TransactionWorker** - polls for `Pending` work, calls the simulator with bounded
  retries (3 attempts, 2s backoff), resolves each transaction to
  `Approved`/`Declined`/`Failed`.
- **ProcessorSimulator** - returns a configurable, deterministic outcome
  (approved/declined/timeout/error) - never a real processor call.
- **SQLite** is the shared store between the two processes - a local file rather than
  a server, so they can agree on state without needing a database service or any
  credentials.
- **Redis** is optional and only ever a speed optimization for idempotency-key
  lookups (cache-aside). If it's not running, `PaymentApi` falls back to hitting SQLite
  directly - correct, just slightly slower. Deliberately not an eager
  connect-or-crash dependency.
- A transaction that exhausts its retries gets an optional AI-generated
  plain-English explanation of the likely cause (`TransactionWorker`'s
  `IFailureExplainer`), via the Claude API, if `ANTHROPIC_API_KEY` is set in the
  environment. Without a key, a deterministic canned explainer is used instead -
  same correctness, just no AI commentary. Never required for anything to work.

Transaction states: `Pending -> Submitted -> Approved | Declined | Failed`. A
transaction stuck `Submitted` for more than 30 seconds (its worker likely crashed) is
automatically reclaimed and retried by the next poll.

OpenTelemetry traces are wired through all three services and exported to a local
Jaeger instance when it's running (`docker compose up -d`, UI at
http://localhost:16686) - no cloud exporter, no account. Watching real traces during
development caught a real bug: the first version of the retry-backoff wait was nested
inside the same span as the simulator call, making attempt durations look 2 seconds
longer than they actually were. Fixed by giving the backoff its own span
(`TransactionProcessingWorker.cs`).

## Local setup

Requires the .NET 10 SDK. Nothing else is required for the main build and test suite.

```
dotnet build PaymentDemo.slnx
dotnet test PaymentDemo.slnx
```

To run all three services together:

```
dotnet run --project src/ProcessorSimulator --urls http://localhost:5099
dotnet run --project src/PaymentApi --urls http://localhost:5172
dotnet run --project src/TransactionWorker
```

Submit a synthetic payment:

```
curl -X POST http://localhost:5172/payments \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: demo-001" \
  -d '{"demoAccountId":"acct_demo","amountMinorUnits":2599,"currency":"USD","simulatedOutcome":"approved"}'
```

Then `GET http://localhost:5172/payments/{transactionId}` to watch it move from
`Pending` to a terminal state.

Optional Redis + Jaeger: `docker compose up -d` / `docker compose down`. Neither is
required for anything above to work.

## Tests

```
dotnet test PaymentDemo.slnx
```

50 tests across 4 test projects: repository contract tests run against both storage
implementations (in-memory and SQLite), a 35-way concurrent-claim test, bounded-retry
and crash-recovery tests using fake delays and a scripted HTTP handler (no real
sleeps), and integration tests for both HTTP services. Two Redis-specific tests
self-detect whether Redis is reachable and no-op if it isn't, so the suite never
depends on Docker being up. CI (`.github/workflows/ci.yml`) runs the same commands
with no external credentials.

## Challenges

`challenges/` has three small, standalone bug-fix exercises, each pulled out of a real
hazard this project had to actually solve - deliberately excluded from the main
build/CI since the starter code is supposed to fail its own tests until fixed:

- **01-duplicate-submission-race** - a check-then-act idempotency guard that lets two
  concurrent callers both create a result for the same key.
- **02-timeout-retry-race** - a retry loop that "gives up" on a slow attempt without
  actually stopping it, so an abandoned attempt can complete late and double-fire its
  completion handler.
- **03-stale-claim-recovery** - a crash-recovery check that compares against the wrong
  timestamp, so a job can be claimed a second time moments after it was first claimed.

Each has a `Starter/` (bug intact) and `Starter.Tests/` (fails against it) -
`dotnet test challenges/<name>/Starter.Tests`.

## Known limitations

- SQLite state resets if `data/payments.db` is deleted - fine for a demo, not a
  durability story.
- `TransactionWorker` processes one transaction (including its retries) at a time; a
  transaction stuck retrying delays whatever's behind it.
- A transaction recovered after a crash gets a fresh retry budget rather than resuming
  a partial one.
- No metrics, only traces - didn't add instrumentation without a concrete use for it.

## Next improvements

- A proper lease (worker id + expiry) instead of a timestamp-only staleness check, if
  this ever needed more than one worker instance.
- Metrics, if a concrete dashboard need showed up.
- Idempotency cache eviction policy beyond a flat TTL, if key volume got large.
