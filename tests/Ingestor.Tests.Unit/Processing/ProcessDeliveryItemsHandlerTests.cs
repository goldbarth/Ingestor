using FluentAssertions;
using Ingestor.Application.Abstractions;
using Ingestor.Application.Processing;
using Ingestor.Domain.Common;
using Ingestor.Domain.DeliveryItems;
using Ingestor.Domain.Jobs;
using Ingestor.Domain.Jobs.Enums;
using Ingestor.Domain.Parsing;

namespace Ingestor.Tests.Unit.Processing;

public sealed class ProcessDeliveryItemsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeImportJobRepository _jobRepository = new();
    private readonly FakeDeliveryItemRepository _deliveryItemRepository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly ProcessDeliveryItemsHandler _sut;

    public ProcessDeliveryItemsHandlerTests()
    {
        _sut = new ProcessDeliveryItemsHandler(
            _jobRepository,
            _deliveryItemRepository,
            _unitOfWork,
            new FakeClock(Now));
    }

    [Fact]
    public async Task HandleAsync_ValidJobAndLines_WritesItemsAndSucceeds()
    {
        var job = CreateJob();
        _jobRepository.Add(job);

        var lines = new[]
        {
            new DeliveryAdviceLine(1, "ART-001", "Oak Dining Table", 5, Now.AddDays(10), "SUP-42"),
            new DeliveryAdviceLine(2, "ART-002", "Leather Sofa",     2, Now.AddDays(20), "SUP-42")
        };

        var result = await _sut.HandleAsync(job.Id, lines);

        result.IsSuccess.Should().BeTrue();
        result.ProcessedItemCount.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_ValidJobAndLines_PersistsCorrectDeliveryItems()
    {
        var job = CreateJob();
        _jobRepository.Add(job);

        var lines = new[]
        {
            new DeliveryAdviceLine(1, "ART-001", "Oak Dining Table", 5, Now.AddDays(10), "SUP-42")
        };

        await _sut.HandleAsync(job.Id, lines);

        _deliveryItemRepository.Items.Should().ContainSingle();
        var item = _deliveryItemRepository.Items[0];
        item.JobId.Should().Be(job.Id);
        item.ArticleNumber.Should().Be("ART-001");
        item.ProductName.Should().Be("Oak Dining Table");
        item.Quantity.Should().Be(5);
        item.SupplierRef.Should().Be("SUP-42");
        item.ProcessedAt.Should().Be(Now);
    }

    [Fact]
    public async Task HandleAsync_ValidJobAndLines_MarksJobAsSucceeded()
    {
        var job = CreateJob();
        _jobRepository.Add(job);

        var lines = new[]
        {
            new DeliveryAdviceLine(1, "ART-001", "Oak Dining Table", 5, Now.AddDays(10), "SUP-42")
        };

        await _sut.HandleAsync(job.Id, lines);

        job.Status.Should().Be(JobStatus.Succeeded);
        job.ProcessedItemCount.Should().Be(1);
        job.CompletedAt.Should().Be(Now);
    }

    [Fact]
    public async Task HandleAsync_ValidJobAndLines_SavesChanges()
    {
        var job = CreateJob();
        _jobRepository.Add(job);

        var lines = new[]
        {
            new DeliveryAdviceLine(1, "ART-001", "Oak Dining Table", 5, Now.AddDays(10), "SUP-42")
        };

        await _sut.HandleAsync(job.Id, lines);

        _unitOfWork.SaveChangesCallCount.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_JobNotFound_ReturnsJobNotFoundResult()
    {
        var unknownJobId = JobId.New();

        var result = await _sut.HandleAsync(unknownJobId, []);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("processing.job_not_found");
    }

    private static ImportJob CreateJob() => new(
        JobId.New(),
        "SUP-42",
        ImportType.CsvDeliveryAdvice,
        "idempotency-key",
        "payload-ref",
        Now,
        maxAttempts: 3);

    private sealed class FakeImportJobRepository : IImportJobRepository
    {
        private readonly Dictionary<JobId, ImportJob> _jobs = new();

        public void Add(ImportJob job) => _jobs[job.Id] = job;

        public Task AddAsync(ImportJob job, ImportPayload payload, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<ImportJob?> GetByIdAsync(JobId id, CancellationToken ct = default)
            => Task.FromResult(_jobs.GetValueOrDefault(id));

        public Task<bool> ExistsByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<ImportJob>> SearchAsync(JobStatus? status, JobId? cursor, int pageSize, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeDeliveryItemRepository : IDeliveryItemRepository
    {
        public List<DeliveryItem> Items { get; } = [];

        public Task AddRangeAsync(IReadOnlyList<DeliveryItem> items, CancellationToken ct = default)
        {
            Items.AddRange(items);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCallCount { get; private set; }

        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}