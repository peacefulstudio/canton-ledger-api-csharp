// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using System.Diagnostics;
using System.Text.Json;
using Daml.Codegen.CSharp.Runtime.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Canton.Ledger.Pqs.Client;

/// <summary>
/// Implementation of <see cref="IPqsClient"/> using Npgsql for PostgreSQL queries.
/// </summary>
public sealed partial class PqsClient : IPqsClient
{
    private static readonly ActivitySource ActivitySource = new("Canton.Ledger.Pqs.Client");

    // PQS payloads use camelCase keys while generated C# records use PascalCase properties.
    private static readonly JsonSerializerOptions CaseInsensitiveOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly PqsClientOptions _options;
    private readonly ILogger<PqsClient> _logger;

    public PqsClient(IOptions<PqsClientOptions> options, ILogger<PqsClient> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.ConnectionString);
        _logger = logger;
    }

    public PqsClient(PqsClientOptions options, ILogger<PqsClient> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Contract<T>>> QueryAsync<T>(
        CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        return ExecuteQueryManyAsync<T>(
            "SELECT contract_id, payload FROM active(@templateId)",
            configureParams: null,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Contract<T>>> QueryAsync<T>(
        PqsFilter filter,
        CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        ArgumentNullException.ThrowIfNull(filter);

        var (sql, parameters) = BuildFilteredQuery(filter);
        return ExecuteQueryManyAsync<T>(
            sql,
            cmd => ApplyParameters(cmd, parameters),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Contract<T>?> QueryOneAsync<T>(
        PqsFilter filter,
        CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        ArgumentNullException.ThrowIfNull(filter);

        var (sql, parameters) = BuildFilteredQuery(filter);
        return ExecuteQueryOneAsync<T>(
            sql + " LIMIT 1",
            cmd => ApplyParameters(cmd, parameters),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Contract<T>?> FetchByIdAsync<T>(
        ContractId<T> contractId,
        CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        return ExecuteQueryOneAsync<T>(
            "SELECT contract_id, payload FROM active(@templateId) WHERE contract_id = @contractId LIMIT 1",
            cmd => cmd.Parameters.AddWithValue("@contractId", contractId.Value),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync<T>(
        ContractId<T> contractId,
        CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        var templateId = TemplateExtensions.GetTemplateId<T>();

        using var activity = ActivitySource.StartActivity("PqsExists");
        activity?.SetTag("pqs.template", templateId);

        LogQueryStart(templateId);

        try
        {
            await using var connection = new NpgsqlConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(
                "SELECT 1 FROM active(@templateId) WHERE contract_id = @contractId LIMIT 1",
                connection);
            command.Parameters.AddWithValue("@templateId", templateId);
            command.Parameters.AddWithValue("@contractId", contractId.Value);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            var exists = result is not null;

            LogQueryOneResult(exists ? "found" : "not found", templateId);
            return exists;
        }
        catch (PostgresException ex) when (IsTemplateNotFoundError(ex))
        {
            LogTemplateNotFound(templateId);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogQueryError(ex, templateId);
            throw;
        }
    }

    // ──────────────────────────────────────────────────────────────
    // SQL generation
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the full SQL query and captures filter parameters in a single pass.
    /// </summary>
    internal static (string Sql, IReadOnlyList<(string Name, string Value)> Parameters) BuildFilteredQuery(
        PqsFilter filter)
    {
        using var tempCmd = new NpgsqlCommand();
        var paramIndex = 0;
        var whereClause = filter.ToSqlClause(tempCmd, ref paramIndex);
        var sql = $"SELECT contract_id, payload FROM active(@templateId) WHERE {whereClause}";

        var parameters = new List<(string Name, string Value)>(tempCmd.Parameters.Count);
        foreach (NpgsqlParameter p in tempCmd.Parameters)
            parameters.Add((p.ParameterName, (string)p.Value!));

        return (sql, parameters);
    }

    private static void ApplyParameters(NpgsqlCommand cmd, IReadOnlyList<(string Name, string Value)> parameters)
    {
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
    }

    // ──────────────────────────────────────────────────────────────
    // Query execution
    // ──────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<Contract<T>>> ExecuteQueryManyAsync<T>(
        string sql,
        Action<NpgsqlCommand>? configureParams,
        CancellationToken cancellationToken)
        where T : ITemplate
    {
        var templateId = TemplateExtensions.GetTemplateId<T>();

        using var activity = ActivitySource.StartActivity("PqsQuery");
        activity?.SetTag("pqs.template", templateId);

        LogQueryStart(templateId);

        try
        {
            await using var connection = new NpgsqlConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("@templateId", templateId);
            configureParams?.Invoke(command);

            var contracts = new List<Contract<T>>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                contracts.Add(DeserializeContract<T>(reader.GetString(0), reader.GetString(1)));
            }

            LogQueryResult(contracts.Count, templateId);
            activity?.SetTag("pqs.result.count", contracts.Count);
            return contracts;
        }
        catch (PostgresException ex) when (IsTemplateNotFoundError(ex))
        {
            LogTemplateNotFound(templateId);
            return [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogQueryError(ex, templateId);
            throw;
        }
    }

    private async Task<Contract<T>?> ExecuteQueryOneAsync<T>(
        string sql,
        Action<NpgsqlCommand>? configureParams,
        CancellationToken cancellationToken)
        where T : ITemplate
    {
        var templateId = TemplateExtensions.GetTemplateId<T>();

        using var activity = ActivitySource.StartActivity("PqsQueryOne");
        activity?.SetTag("pqs.template", templateId);

        LogQueryStart(templateId);

        try
        {
            await using var connection = new NpgsqlConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("@templateId", templateId);
            configureParams?.Invoke(command);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var contract = DeserializeContract<T>(reader.GetString(0), reader.GetString(1));
                LogQueryOneResult("found", templateId);
                return contract;
            }

            LogQueryOneResult("not found", templateId);
            return null;
        }
        catch (PostgresException ex) when (IsTemplateNotFoundError(ex))
        {
            LogTemplateNotFound(templateId);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogQueryError(ex, templateId);
            throw;
        }
    }

    /// <summary>
    /// PQS's <c>active()</c> function raises P0001 "Identifier not found" when no contracts
    /// of a given template type have ever been created. This is semantically equivalent
    /// to "no results" rather than an error. This commonly occurs when querying a template
    /// type before any contracts of that type have been created on the ledger.
    /// </summary>
    internal static bool IsTemplateNotFoundError(PostgresException ex) =>
        ex.SqlState == "P0001" && ex.MessageText.StartsWith("Identifier not found:", StringComparison.Ordinal);

    private static Contract<T> DeserializeContract<T>(string contractId, string payloadJson) where T : ITemplate
    {
        var payload = JsonSerializer.Deserialize<T>(payloadJson, CaseInsensitiveOptions)
            ?? throw new InvalidOperationException(
                $"Failed to deserialize PQS payload for contract '{contractId}' " +
                $"as template '{typeof(T).FullName ?? typeof(T).Name}'.");

        return new Contract<T>(new ContractId<T>(contractId), payload);
    }

    // ──────────────────────────────────────────────────────────────
    // Source-generated logging
    // ──────────────────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug, Message = "Querying active contracts for template {TemplateId}")]
    private partial void LogQueryStart(string templateId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} active contracts for {TemplateId}")]
    private partial void LogQueryResult(int count, string templateId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Query for {TemplateId}: {Result}")]
    private partial void LogQueryOneResult(string result, string templateId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Template {TemplateId} not registered in PQS — returning empty result. " +
                  "This may indicate the template has never been instantiated or PQS has not indexed it yet.")]
    private partial void LogTemplateNotFound(string templateId);

    [LoggerMessage(Level = LogLevel.Error, Message = "PQS query failed for template {TemplateId}")]
    private partial void LogQueryError(Exception ex, string templateId);
}
