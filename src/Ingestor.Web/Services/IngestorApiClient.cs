using System.Net.Http.Headers;
using Ingestor.Contracts.V1.Enums;
using Ingestor.Contracts.V1.Responses;

namespace Ingestor.Web.Services;

public sealed class IngestorApiClient(HttpClient http)
{
    // ── Imports ──────────────────────────────────────────────────────────────

    public async Task<CursorPagedResponse<ImportJobResponse>?> GetJobsAsync(
        string? status = null,
        Guid? cursor = null,
        int pageSize = 25,
        CancellationToken ct = default)
    {
        var query = BuildQuery(
            ("status", status),
            ("cursor", cursor?.ToString()),
            ("pageSize", pageSize.ToString()));

        return await http.GetFromJsonAsync<CursorPagedResponse<ImportJobResponse>>(
            $"/api/imports{query}", ct);
    }

    public async Task<ImportJobDetailResponse?> GetJobDetailAsync(Guid id, CancellationToken ct = default)
        => await http.GetFromJsonAsync<ImportJobDetailResponse>($"/api/imports/{id}", ct);

    public async Task<IReadOnlyList<AuditEventResponse>?> GetJobHistoryAsync(Guid id, CancellationToken ct = default)
        => await http.GetFromJsonAsync<IReadOnlyList<AuditEventResponse>>($"/api/imports/{id}/history", ct);

    public async Task<(ImportJobResponse? job, bool isDuplicate, string? error)> UploadAsync(
        string supplierCode,
        ImportType importType,
        string fileName,
        Stream fileContent,
        CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(supplierCode), "supplierCode");
        form.Add(new StringContent(importType.ToString()), "importType");

        var fileStreamContent = new StreamContent(fileContent);
        fileStreamContent.Headers.ContentType = new MediaTypeHeaderValue(importType.ToMediaType());
        form.Add(fileStreamContent, "file", fileName);

        var response = await http.PostAsync("/api/imports", form, ct);

        if (response.IsSuccessStatusCode)
        {
            var job = await response.Content.ReadFromJsonAsync<ImportJobResponse>(ct);
            return (job, response.StatusCode == System.Net.HttpStatusCode.OK, null);
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        return (null, false, $"{(int)response.StatusCode}: {body}");
    }

    public async Task<bool> RequeueAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"/api/imports/{id}/requeue", null, ct);
        return response.IsSuccessStatusCode;
    }

    // ── Metrics ──────────────────────────────────────────────────────────────

    public async Task<JobMetricsResponse?> GetJobMetricsAsync(CancellationToken ct = default)
        => await http.GetFromJsonAsync<JobMetricsResponse>("/api/metrics/jobs", ct);

    public async Task<ProcessingMetricsResponse?> GetProcessingMetricsAsync(CancellationToken ct = default)
        => await http.GetFromJsonAsync<ProcessingMetricsResponse>("/api/metrics/processing", ct);

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string BuildQuery(params (string key, string? value)[] pairs)
    {
        var parts = pairs
            .Where(p => p.value is not null)
            .Select(p => $"{p.key}={Uri.EscapeDataString(p.value!)}");

        var qs = string.Join("&", parts);
        return qs.Length > 0 ? $"?{qs}" : string.Empty;
    }
}
