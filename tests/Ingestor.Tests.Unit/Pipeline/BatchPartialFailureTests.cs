using FluentAssertions;
using Ingestor.Application.Abstractions;
using Ingestor.Application.Pipeline;
using Ingestor.Domain.Common;
using Ingestor.Domain.DeliveryItems;
using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;
using Ingestor.Domain.Parsing;
using Ingestor.Domain.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ingestor.Tests.Unit.Pipeline;

public sealed class BatchPartialFailureTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 28, 10, 0, 0, TimeSpan.Zero);

    // 5 lines → ChunkSize 2 → [[L1,L2], [L3,L4], [L5]] (3 chunks, isBatch = true)
    private static readonly IReadOnlyList<DeliveryAdviceLine> FiveLines =
    [
        new DeliveryAdviceLine(1, "ART-001", "Product A", 1, Now.AddDays(1), "REF-1"),
        new DeliveryAdviceLine(2, "ART-002", "Product B", 1, Now.AddDays(1), "REF-2"),
        new DeliveryAdviceLine(3, "ART-003", "Product C", 1, Now.AddDays(1), "REF-3"),
        new DeliveryAdviceLine(4, "ART-004", "Product D", 1, Now.AddDays(1), "REF-4"),
        new DeliveryAdviceLine(5, "ART-005", "Product E", 1, Now.AddDays(1), "REF-5"),
    ];

    private readonly FakeImportJobRepository _jobRepository = new();
    private readonly FaultInjectableDeliveryItemRepository _deliveryItemRepository = new();
    private readonly FakeAuditEventRepository _auditEventRepository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly ImportPipelineHandler _sut;

    public BatchPartialFailureTests()
    {
        _sut = new ImportPipelineHandler(
            _jobRepository,
            _deliveryItemRepository,
            _auditEventRepository,
            _unitOfWork,
            new DeliveryAdviceValidator(new FakeClock(Now)),
            new FakeClock(Now),
            Options.Create(new BatchOptions { ChunkSize = 2 }),
            NullLogger<ImportPipelineHandler>.Instance,
            new FakeParser(FiveLines),
            new FakeParser(FiveLines));
    }

    [Fact]
    public async Task HandleAsync_BatchWithFailingChunk_TransitionsToPartiallySucceeded()
    {
        // Arrange: second chunk (AddRangeAsync call #2) throws
        var jobId = SetupJob();
        _deliveryItemRepository.FailOnCallNumber = 2;

        // Act
        await _sut.HandleAsync(jobId);

        // Assert
        _jobRepository.Jobs[jobId].Status.Should().Be(JobStatus.PartiallySucceeded);
    }

    [Fact]
    public async Task HandleAsync_BatchWithFailingChunk_RecordsFailedLines()
    {
        // Arrange: second chunk (2 lines) fails
        var jobId = SetupJob();
        _deliveryItemRepository.FailOnCallNumber = 2;

        // Act
        await _sut.HandleAsync(jobId);

        // Assert: chunk 2 had 2 lines
        _jobRepository.Jobs[jobId].FailedLines.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_BatchWithFailingChunk_PersistsItemsFromSuccessfulChunks()
    {
        // Arrange: second chunk fails, chunks 1 (2 lines) and 3 (1 line) succeed
        var jobId = SetupJob();
        _deliveryItemRepository.FailOnCallNumber = 2;

        // Act
        await _sut.HandleAsync(jobId);

        // Assert: 3 items from chunks 1 and 3 are persisted
        _deliveryItemRepository.Items.Should().HaveCount(3);
        _deliveryItemRepository.Items.Select(i => i.ArticleNumber)
            .Should().BeEquivalentTo(["ART-001", "ART-002", "ART-005"]);
    }

    private JobId SetupJob()
    {
        var jobId = JobId.New();
        var payload = new ImportPayload(PayloadId.New(), jobId, "text/csv", Array.Empty<byte>(), Now);
        var job = new ImportJob(jobId, "SUP-01", ImportType.CsvDeliveryAdvice,
            "idempotency-key", payload.Id.Value.ToString(), Now, maxAttempts: 3);
        _jobRepository.Add(job, payload);
        return jobId;
    }

    private sealed class FakeImportJobRepository : IImportJobRepository
    {
        public Dictionary<JobId, ImportJob> Jobs { get; } = new();
        private readonly Dictionary<JobId, ImportPayload> _payloads = new();

        public void Add(ImportJob job, ImportPayload payload)
        {
            Jobs[job.Id] = job;
            _payloads[job.Id] = payload;
        }

        public Task AddAsync(ImportJob job, ImportPayload payload, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<ImportJob?> GetByIdAsync(JobId id, CancellationToken ct = default)
            => Task.FromResult(Jobs.GetValueOrDefault(id));

        public Task<ImportPayload?> GetPayloadByJobIdAsync(JobId jobId, CancellationToken ct = default)
            => Task.FromResult(_payloads.GetValueOrDefault(jobId));

        public Task<ImportJob?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<ImportJob>> SearchAsync(JobStatus? status, JobId? cursor, int pageSize, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyDictionary<JobStatus, int>> GetStatusCountsAsync(CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class FaultInjectableDeliveryItemRepository : IDeliveryItemRepository
    {
        private int _callCount;
        public int FailOnCallNumber { get; set; } = -1;
        public List<DeliveryItem> Items { get; } = [];

        public Task AddRangeAsync(IReadOnlyList<DeliveryItem> items, CancellationToken ct = default)
        {
            _callCount++;
            if (_callCount == FailOnCallNumber)
                throw new InvalidOperationException("Simulated chunk failure.");
            Items.AddRange(items);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuditEventRepository : IAuditEventRepository
    {
        public Task AddAsync(AuditEvent entry, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<AuditEvent>> GetByJobIdAsync(JobId jobId, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class FakeParser(IReadOnlyList<DeliveryAdviceLine> lines) : IDeliveryAdviceParser
    {
        public ParseResult<DeliveryAdviceLine> Parse(Stream input)
            => ParseResult<DeliveryAdviceLine>.Success(lines);
    }
}
