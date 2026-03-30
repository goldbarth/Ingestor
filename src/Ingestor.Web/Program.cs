using Azure.Storage.Blobs;
using Ingestor.Web.Components;
using Ingestor.Web.Services;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

var storageConnectionString = builder.Configuration["DataProtection:StorageConnectionString"];
if (!string.IsNullOrEmpty(storageConnectionString))
{
    var containerClient = new BlobContainerClient(storageConnectionString, "dataprotection");
    containerClient.CreateIfNotExists();
    builder.Services.AddDataProtection()
        .SetApplicationName("ingestor-web")
        .PersistKeysToAzureBlobStorage(containerClient.GetBlobClient("keys.xml"));
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var apiBaseUrl = builder.Configuration["IngestorApi:BaseUrl"]
                 ?? throw new InvalidOperationException("IngestorApi:BaseUrl is not configured.");

builder.Services.AddHttpClient<IngestorApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
