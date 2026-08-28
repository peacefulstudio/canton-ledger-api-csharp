// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Telemetry;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Canton.Ledger.Pqs.Client;

/// <summary>
/// Implementation of <see cref="IPqsClient"/> using Npgsql for PostgreSQL queries.
/// </summary>
public sealed partial class PqsClient : IPqsClient
{
    /// <summary>
    /// The <see cref="ActivitySource"/> name used for OpenTelemetry tracing.
    /// Register with <c>tracing.AddSource(PqsClient.ActivitySourceName)</c>.
    /// </summary>
    public static string ActivitySourceName => LedgerActivitySourceNames.PqsClient;

    private static readonly ActivitySource ActivitySource = LedgerActivitySource.Create<PqsClient>();

    /// <summary>
    /// Default <see cref="JsonSerializerOptions"/> used for deserializing PQS contract payloads.
    /// PQS payloads use camelCase keys while generated C# records use PascalCase properties,
    /// so <see cref="JsonSerializerOptions.PropertyNameCaseInsensitive"/> is enabled to handle the case mismatch.
    /// Daml Numeric values are stored as JSON strings ("1.0000000000") requiring AllowReadingFromString.
    /// Daml enum values are stored as plain strings ("Active", "Sell") requiring JsonStringEnumConverter.
    /// This instance is read-only and cannot be modified.
    /// </summary>
    public static readonly JsonSerializerOptions DefaultJsonSerializerOptions = CreateDefaultJsonOptions();

    private static JsonSerializerOptions CreateDefaultJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private const string SelectActiveSql = "SELECT contract_id, payload FROM active(@typeId)";
    private const string PageLimitParameter = "@pageLimit";
    private const string PageOffsetParameter = "@pageOffset";

    private readonly PqsClientOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<PqsClient> _logger;
    private readonly Func<CancellationToken, ValueTask<NpgsqlConnection>> _openConnectionAsync;
    private readonly Func<NpgsqlCommand, CancellationToken, Task<DbDataReader>> _executeReaderAsync;

    /// <summary>
    /// Creates a new PqsClient from explicit options.
    /// Logs are discarded unless a <paramref name="logger"/> is supplied.
    /// </summary>
    public PqsClient(PqsClientOptions options, ILogger<PqsClient>? logger = null)
        : this(options, openConnectionAsync: null, logger)
    {
    }

    /// <summary>
    /// Creates a new PqsClient using options from dependency injection.
    /// Logs are discarded unless a <paramref name="logger"/> is supplied.
    /// </summary>
    public PqsClient(IOptions<PqsClientOptions> options, ILogger<PqsClient>? logger = null)
        : this((options ?? throw new ArgumentNullException(nameof(options))).Value, logger)
    {
    }

    internal PqsClient(
        PqsClientOptions options,
        Func<CancellationToken, ValueTask<NpgsqlConnection>>? openConnectionAsync,
        ILogger<PqsClient>? logger,
        Func<NpgsqlCommand, CancellationToken, Task<DbDataReader>>? executeReaderAsync = null)
    {
        _options = ValidateOptions(options);
        _jsonOptions = options.JsonSerializerOptions ?? DefaultJsonSerializerOptions;
        _logger = logger ?? NullLogger<PqsClient>.Instance;
        _openConnectionAsync = openConnectionAsync ?? OpenConnectionFromOptionsAsync;
        _executeReaderAsync = executeReaderAsync ?? ExecuteReaderDirectlyAsync;
    }

    private static async Task<DbDataReader> ExecuteReaderDirectlyAsync(
        NpgsqlCommand command, CancellationToken cancellationToken) =>
        await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

    private async ValueTask<NpgsqlConnection> OpenConnectionFromOptionsAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static PqsClientOptions ValidateOptions(PqsClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);
        return options;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Contract<T>>> QueryAsync<T>(
        CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        return ExecuteQueryManyAsync<T>(
            SelectActiveSql,
            configureParams: null,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Contract<T>>> QueryAsync<T>(
        PqsPage page,
        CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        ArgumentNullException.ThrowIfNull(page);

        return ExecuteQueryManyAsync<T>(
            WithPageClause(SelectActiveSql),
            cmd => AddPageParameters(cmd, page),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InterfaceContract<TInterface, TView>>> QueryAsync<TInterface, TView>(
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord =>
        ExecuteProjectingQueryManyAsync(
            SelectActiveSql,
            GetDamlTypeId<TInterface>(),
            configureParams: null,
            (contractId, payloadJson) =>
                DeserializeInterfaceContract<TInterface, TView>(contractId, payloadJson, _jsonOptions),
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<InterfaceContract<TInterface, TView>>> QueryAsync<TInterface, TView>(
        PqsPage page,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord
    {
        ArgumentNullException.ThrowIfNull(page);

        return ExecuteProjectingQueryManyAsync(
            WithPageClause(SelectActiveSql),
            GetDamlTypeId<TInterface>(),
            cmd => AddPageParameters(cmd, page),
            (contractId, payloadJson) =>
                DeserializeInterfaceContract<TInterface, TView>(contractId, payloadJson, _jsonOptions),
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
    public Task<IReadOnlyList<Contract<T>>> QueryAsync<T>(
        PqsFilter filter,
        PqsPage page,
        CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);

        var (sql, parameters) = BuildFilteredQuery(filter);
        return ExecuteQueryManyAsync<T>(
            WithPageClause(sql),
            cmd =>
            {
                ApplyParameters(cmd, parameters);
                AddPageParameters(cmd, page);
            },
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
            "SELECT contract_id, payload FROM active(@typeId) WHERE contract_id = @contractId LIMIT 1",
            cmd => cmd.Parameters.AddWithValue("@contractId", contractId.Value),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync<T>(
        ContractId<T> contractId,
        CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        var templateId = TemplateExtensions.GetTemplateId<T>();

        return ExecuteWithDiagnosticsAsync(
            "PqsExists",
            "SELECT 1 FROM active(@typeId) WHERE contract_id = @contractId LIMIT 1",
            templateId,
            cmd => cmd.Parameters.AddWithValue("@contractId", contractId.Value),
            notFoundResult: false,
            async (command, _) =>
            {
                var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                var exists = result is not null;

                LogQueryOneResult(_logger, exists ? "found" : "not found", templateId);
                return exists;
            },
            cancellationToken);
    }

    internal static (string Sql, IReadOnlyList<(string Name, string Value)> Parameters) BuildFilteredQuery(
        PqsFilter filter)
    {
        var parameters = new List<(string Name, string Value)>();
        var paramIndex = 0;
        var whereClause = filter.ToSqlClause(parameters, ref paramIndex);

        return ($"{SelectActiveSql} WHERE {whereClause}", parameters);
    }

    private static void ApplyParameters(NpgsqlCommand cmd, IReadOnlyList<(string Name, string Value)> parameters)
    {
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
    }

    internal static string WithPageClause(string sql) =>
        $"{sql} ORDER BY contract_id LIMIT {PageLimitParameter} OFFSET {PageOffsetParameter}";

    private static void AddPageParameters(NpgsqlCommand cmd, PqsPage page)
    {
        cmd.Parameters.AddWithValue(PageLimitParameter, page.Limit);
        cmd.Parameters.AddWithValue(PageOffsetParameter, page.Offset);
    }

    private Task<IReadOnlyList<Contract<T>>> ExecuteQueryManyAsync<T>(
        string sql,
        Action<NpgsqlCommand>? configureParams,
        CancellationToken cancellationToken)
        where T : ITemplate =>
        ExecuteProjectingQueryManyAsync(
            sql,
            TemplateExtensions.GetTemplateId<T>(),
            configureParams,
            (contractId, payloadJson) => DeserializeContract<T>(contractId, payloadJson, _jsonOptions),
            cancellationToken);

    private Task<IReadOnlyList<TItem>> ExecuteProjectingQueryManyAsync<TItem>(
        string sql,
        string identifier,
        Action<NpgsqlCommand>? configureParams,
        Func<string, string, TItem> project,
        CancellationToken cancellationToken)
    {
        return ExecuteWithDiagnosticsAsync<IReadOnlyList<TItem>>(
            "PqsQuery",
            sql,
            identifier,
            configureParams,
            notFoundResult: [],
            async (command, activity) =>
            {
                var items = new List<TItem>();
                var reader = await _executeReaderAsync(command, cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        items.Add(project(reader.GetString(0), reader.GetString(1)));
                    }

                    LogQueryResult(_logger, items.Count, identifier);
                    activity?.SetTag(PqsClientActivityTags.CantonPqsResultCount, items.Count);
                    return items;
                }
            },
            cancellationToken);
    }

    private Task<Contract<T>?> ExecuteQueryOneAsync<T>(
        string sql,
        Action<NpgsqlCommand>? configureParams,
        CancellationToken cancellationToken)
        where T : ITemplate
    {
        var templateId = TemplateExtensions.GetTemplateId<T>();

        return ExecuteWithDiagnosticsAsync<Contract<T>?>(
            "PqsQueryOne",
            sql,
            templateId,
            configureParams,
            notFoundResult: null,
            async (command, _) =>
            {
                var reader = await _executeReaderAsync(command, cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var contract = DeserializeContract<T>(reader.GetString(0), reader.GetString(1), _jsonOptions);
                        LogQueryOneResult(_logger, "found", templateId);
                        return contract;
                    }

                    LogQueryOneResult(_logger, "not found", templateId);
                    return null;
                }
            },
            cancellationToken);
    }

    private async Task<TResult> ExecuteWithDiagnosticsAsync<TResult>(
        string activityName,
        string sql,
        string typeId,
        Action<NpgsqlCommand>? configureParams,
        TResult notFoundResult,
        Func<NpgsqlCommand, Activity?, Task<TResult>> runQuery,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity(activityName);
        activity?.SetTag(PqsClientActivityTags.DamlTemplateId, typeId);

        LogQueryStart(_logger, typeId);

        try
        {
            var connection = await _openConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                var command = new NpgsqlCommand(sql, connection);
                await using (command.ConfigureAwait(false))
                {
                    command.Parameters.AddWithValue("@typeId", typeId);
                    configureParams?.Invoke(command);

                    return await runQuery(command, activity).ConfigureAwait(false);
                }
            }
        }
        catch (PostgresException ex) when (IsTypeNotFoundError(ex))
        {
            LogTypeNotFound(_logger, typeId);
            return notFoundResult;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogQueryError(_logger, ex, typeId);
            activity.RecordException(ex);
            throw;
        }
    }

    // Workaround for PQS's active() function: it raises P0001 "Identifier not found" when no
    // contracts of a given type have ever been created, which is semantically "no
    // results" rather than an error.
    internal static bool IsTypeNotFoundError(PostgresException ex) =>
        ex.SqlState == "P0001" && ex.MessageText.StartsWith("Identifier not found:", StringComparison.Ordinal);

    internal static Contract<T> DeserializeContract<T>(
        string contractId,
        string payloadJson,
        JsonSerializerOptions jsonOptions) where T : ITemplate
    {
        var payload = JsonSerializer.Deserialize<T>(payloadJson, jsonOptions)
            ?? throw new InvalidOperationException(
                $"Failed to deserialize PQS payload for contract '{contractId}' " +
                $"as template '{typeof(T).FullName ?? typeof(T).Name}'.");

        return new Contract<T>(new ContractId<T>(contractId), payload);
    }

    internal static InterfaceContract<TInterface, TView> DeserializeInterfaceContract<TInterface, TView>(
        string contractId,
        string payloadJson,
        JsonSerializerOptions jsonOptions)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord
    {
        var view = JsonSerializer.Deserialize<TView>(payloadJson, jsonOptions)
            ?? throw new InvalidOperationException(
                $"Failed to deserialize PQS interface view for contract '{contractId}' " +
                $"as view '{typeof(TView).FullName ?? typeof(TView).Name}'.");

        return new InterfaceContract<TInterface, TView>(new ContractId<TInterface>(contractId), view);
    }

    /// <summary>
    /// Builds the package-name-qualified identifier PQS <c>active()</c> expects
    /// (<c>{packageName}:{moduleName}:{entityName}</c>) from a Daml type's compile-time
    /// <see cref="DamlTypeDescriptor"/>, resolving a template id or an interface id uniformly.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the descriptor carries an empty package name — a silent fall-back would produce
    /// an identifier that matches nothing in PQS.
    /// </exception>
    internal static string GetDamlTypeId<T>() where T : IDamlType
    {
        var descriptor = T.DamlTypeId;
        if (string.IsNullOrEmpty(descriptor.PackageName))
        {
            throw new InvalidOperationException(
                $"Daml type '{typeof(T).FullName}' has an empty static PackageName; "
                + "cannot build the package-name identifier required by PQS active().");
        }

        return $"{descriptor.PackageName}:{descriptor.Identifier.ModuleName}:{descriptor.Identifier.EntityName}";
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Querying active contracts for type {DamlTypeId}")]
    private static partial void LogQueryStart(ILogger logger, string damlTypeId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} active contracts for {DamlTypeId}")]
    private static partial void LogQueryResult(ILogger logger, int count, string damlTypeId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Query for {DamlTypeId}: {Result}")]
    private static partial void LogQueryOneResult(ILogger logger, string result, string damlTypeId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Type {DamlTypeId} not registered in PQS — returning empty result. " +
                  "This may indicate the type has never been instantiated or PQS has not indexed it yet.")]
    private static partial void LogTypeNotFound(ILogger logger, string damlTypeId);

    [LoggerMessage(Level = LogLevel.Error, Message = "PQS query failed for type {DamlTypeId}")]
    private static partial void LogQueryError(ILogger logger, Exception ex, string damlTypeId);
}
