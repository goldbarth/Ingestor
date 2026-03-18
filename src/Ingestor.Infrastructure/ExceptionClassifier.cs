using Ingestor.Application.Abstractions;
using Ingestor.Domain.Jobs.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ingestor.Infrastructure;

public sealed class ExceptionClassifier : IExceptionClassifier
{
    public ErrorCategory Classify(Exception exception) => exception switch
    {
        NpgsqlException { IsTransient: true } => ErrorCategory.Transient,
        DbUpdateException { InnerException: NpgsqlException { IsTransient: true } } => ErrorCategory.Transient,
        TimeoutException => ErrorCategory.Transient,
        _ => ErrorCategory.Permanent
    };
}