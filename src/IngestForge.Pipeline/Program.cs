using IngestForge.Extractors;
using IngestForge.Storage;
using IngestForge.Workers;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Database=ingestforge;Username=ingestforge;Password=ingestforge";

builder.Services.AddIngestForgeExtractors();
builder.Services.AddIngestForgeStorage(connectionString);
builder.Services.AddIngestForgeWorkers(builder.Configuration);

var host = builder.Build();
host.Run();
