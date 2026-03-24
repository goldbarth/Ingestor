using Ingestor.Application.Abstractions;
using Ingestor.Application.Common;
using Ingestor.Application.Jobs.CreateImportJob;
using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;

namespace Ingestor.Tests.Unit.Jobs;

public sealed class CreateImportJobHandlerTests
{
    private readonly FakeImportJobRepository _jobRepository = new();
    private readonly FakeDatabaseJobDispatcher _jobDispatcher = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly CreateImportJobHandler _sut;

    public CreateImportJobHandlerTests()
    {
        _sut = new CreateImportJobHandler(_jobRepository, _jobDispatcher, _unitOfWork);
    }

    [Fact]
    public async Task HandleAsync_PayloadExceedsMaxSize_ReturnsValidationError()
    {
        var command = new CreateImportJobCommand(
            "SUP-01",
            ImportType.CsvDeliveryAdvice,
            "text/csv",
            new byte[11 * 1024 * 1024]);

        var result = await _sut.HandleAsync(command);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("job.payload_too_large");
    }

    private sealed class FakeImportJobRepository : IImportJobRepository
    {
        public Task AddAsync(ImportJob job, ImportPayload payload, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<ImportJob?> GetByIdAsync(JobId id, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<ImportPayload?> GetPayloadByJobIdAsync(JobId jobId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<ImportJob?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<ImportJob>> SearchAsync(JobStatus? status, JobId? cursor, int pageSize, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyDictionary<JobStatus, int>> GetStatusCountsAsync(CancellationToken ct = default)
            => throw new NotImplementedException();
    }
    
    private sealed class FakeDatabaseJobDispatcher : IJobDispatcher
    {
        public Task DispatchAsync(ImportJob job, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task AcknowledgeAsync(ImportJob job, CancellationToken ct = default) 
            => Task.CompletedTask;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken ct = default)
            => throw new NotImplementedException();
    }
}
