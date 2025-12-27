using Microsoft.EntityFrameworkCore;
using ScalingReads.Core.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// 1. Connection Strings
var masterCs = builder.Configuration.GetConnectionString("MasterConnection");
var replicaCs = builder.Configuration.GetConnectionString("ReplicaConnection");

// 2. Registro do DbContext de ESCRITA (Master - Porta 5432)
// Usado para: Migrations, Inserts, Updates, Deletes.
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(masterCs));

// 3. Registro do DbContext de LEITURA (Réplicas - Portas 5433 e 5434)
// O Npgsql fará o balanceamento entre as duas portas automaticamente.
builder.Services.AddDbContext<ReadOnlyDbContext>(options => options.UseNpgsql(replicaCs));


builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("RedisConnection");
    options.InstanceName = "ScalingReads_";
});

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    using var scope = app.Services.CreateScope();
    var masterDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    masterDb.Database.Migrate();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
