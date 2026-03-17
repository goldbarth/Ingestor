using Ingestor.Application.Abstractions;
using Ingestor.Domain.Common;
using Ingestor.Domain.DeliveryItems;
using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;
using Ingestor.Domain.Parsing;

namespace Ingestor.Application.Processing;

public sealed class ProcessDeliveryItemsHandler(
    IImportJobRepository jobRepository,
    IDeliveryItemRepository deliveryItemRepository,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<ProcessDeliveryItemsResult> HandleAsync(
        JobId jobId,
        IReadOnlyList<DeliveryAdviceLine> lines,
        CancellationToken ct = default)
    {
        var job = await jobRepository.GetByIdAsync(jobId, ct);

        if (job is null)
            return ProcessDeliveryItemsResult.JobNotFound(jobId);

        var processedAt = clock.UtcNow;

        var items = lines.Select(line => new DeliveryItem(
            DeliveryItemId.New(),
            job.Id,
            line.ArticleNumber,
            line.ProductName,
            line.Quantity,
            line.ExpectedDate,
            line.SupplierRef,
            processedAt)).ToList();

        await deliveryItemRepository.AddRangeAsync(items, ct);

        job.TransitionTo(JobStatus.Succeeded, processedAt, items.Count);

        await unitOfWork.SaveChangesAsync(ct);

        return ProcessDeliveryItemsResult.Success(items.Count);
    }
}
