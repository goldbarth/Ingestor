using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using Ingestor.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Ingestor.Infrastructure.Persistence;

internal sealed class NpgsqlTracingCommandInterceptor : DbCommandInterceptor
{
    private readonly ConcurrentDictionary<Guid, Activity> _activities = new();

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        StartActivity(command, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        StartActivity(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        StartActivity(command, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        StartActivity(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        StartActivity(command, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        StartActivity(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        StopActivity(eventData.CommandId);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        StopActivity(eventData.CommandId);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        StopActivity(eventData.CommandId);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        StopActivity(eventData.CommandId);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        StopActivity(eventData.CommandId);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        StopActivity(eventData.CommandId);
        return ValueTask.FromResult(result);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        StopActivity(eventData.CommandId, eventData.Exception);
    }

    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        StopActivity(eventData.CommandId, eventData.Exception);
        return Task.CompletedTask;
    }

    private void StartActivity(DbCommand command, CommandEventData eventData)
    {
        var activity = IngestorDatabaseActivitySource.Database.StartActivity(
            GetSpanName(command.CommandText),
            ActivityKind.Client);

        if (activity is null)
            return;

        PopulateTags(activity, command);
        _activities[eventData.CommandId] = activity;
    }

    private void StopActivity(Guid commandId, Exception? exception = null)
    {
        if (!_activities.TryRemove(commandId, out var activity))
            return;

        if (exception is not null)
        {
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity.SetTag("error.type", exception.GetType().FullName);

            if (exception is PostgresException postgresException)
                activity.SetTag("db.response.status_code", postgresException.SqlState);
        }

        activity.Dispose();
    }

    private static void PopulateTags(Activity activity, DbCommand command)
    {
        activity.SetTag("db.system.name", "postgresql");
        activity.SetTag("db.system", "postgresql");

        var operationName = GetOperationName(command.CommandText);
        if (!string.IsNullOrWhiteSpace(operationName))
            activity.SetTag("db.operation.name", operationName);

        var summary = NormalizeCommandText(command.CommandText);
        if (!string.IsNullOrWhiteSpace(summary))
            activity.SetTag("db.query.summary", summary);

        if (command.Connection is NpgsqlConnection npgsqlConnection)
        {
            if (!string.IsNullOrWhiteSpace(npgsqlConnection.Database))
            {
                activity.SetTag("db.namespace", npgsqlConnection.Database);
                activity.SetTag("db.name", npgsqlConnection.Database);
            }

            if (!string.IsNullOrWhiteSpace(npgsqlConnection.Host))
            {
                activity.SetTag("server.address", npgsqlConnection.Host);
                activity.SetTag("network.peer.address", npgsqlConnection.Host);
            }

            if (npgsqlConnection.Port > 0)
            {
                activity.SetTag("server.port", npgsqlConnection.Port);
                activity.SetTag("network.peer.port", npgsqlConnection.Port);
            }
        }
        else if (command.Connection is not null)
        {
            if (!string.IsNullOrWhiteSpace(command.Connection.Database))
            {
                activity.SetTag("db.namespace", command.Connection.Database);
                activity.SetTag("db.name", command.Connection.Database);
            }

            if (!string.IsNullOrWhiteSpace(command.Connection.DataSource))
                activity.SetTag("server.address", command.Connection.DataSource);
        }
    }

    private static string GetSpanName(string? commandText)
    {
        var operationName = GetOperationName(commandText);
        return string.IsNullOrWhiteSpace(operationName)
            ? "postgresql query"
            : $"postgresql {operationName}";
    }

    private static string? GetOperationName(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
            return null;

        var normalized = commandText.AsSpan().TrimStart();
        if (normalized.IsEmpty)
            return null;

        var operationLength = normalized.IndexOfAny(" \r\n\t(".ToCharArray());
        var operation = operationLength <= 0
            ? normalized
            : normalized[..operationLength];

        return operation.ToString().ToUpperInvariant();
    }

    private static string? NormalizeCommandText(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
            return null;

        var parts = commandText
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
            return null;

        var summary = string.Join(' ', parts);
        return summary.Length <= 160 ? summary : summary[..160];
    }
}
