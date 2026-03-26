using Ingestor.Application.Abstractions;
using Ingestor.Application.Common;

namespace Ingestor.Application.Jobs.GetImportJobById;

public sealed class GetImportJobByIdHandler(IImportJobRepository jobRepository)
{
    public async Task<Result<ImportJobDetailDto>> HandleAsync(
        GetImportJobByIdQuery query, CancellationToken ct = default)
    {
        var job = await jobRepository.GetByIdAsync(query.Id, ct);

        if (job is null)
            return Result<ImportJobDetailDto>.NotFound(
                "job.not_found",
                $"Import job '{query.Id.Value}' was not found.");

        return Result<ImportJobDetailDto>.Success(new ImportJobDetailDto(
            job.Id.Value,
            job.SupplierCode,
            job.ImportType,
            job.Status,
            job.ReceivedAt,
            job.StartedAt,
            job.CompletedAt,
            job.CurrentAttempt,
            job.MaxAttempts,
            job.LastErrorCode,
            job.LastErrorMessage,
            job.IsBatch,
            job.TotalLines,
            job.ProcessedLines,
            job.FailedLines,
            job.ChunkSize));
    }
}
