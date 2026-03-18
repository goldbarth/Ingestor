namespace Ingestor.Worker;

public sealed class WorkerHeartbeat
{
    private DateTimeOffset _lastBeat = DateTimeOffset.UtcNow;

    public DateTimeOffset LastBeat => _lastBeat;

    public void Beat() => _lastBeat = DateTimeOffset.UtcNow;
}