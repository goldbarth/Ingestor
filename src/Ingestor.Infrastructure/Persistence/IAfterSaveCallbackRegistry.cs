namespace Ingestor.Infrastructure.Persistence;

internal interface IAfterSaveCallbackRegistry
{
    void OnAfterSave(Func<CancellationToken, Task> action);
}