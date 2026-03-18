using Ingestor.Domain.Jobs.Enums;

namespace Ingestor.Application.Abstractions;

public interface IExceptionClassifier
{
    ErrorCategory Classify(Exception exception);
}