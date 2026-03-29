// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

namespace Canton.Ledger.Pqs.Client;

/// <summary>
/// Configuration options for the PQS client.
/// </summary>
public class PqsClientOptions
{
    /// <summary>
    /// PostgreSQL connection string for the PQS database.
    /// </summary>
    public required string ConnectionString { get; set; }
}
