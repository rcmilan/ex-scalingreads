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
    options.Configuration = "localhost:6379";
    options.InstanceName = "app-cache:";
});

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add Swagger UI services
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    
    // Add Swagger UI middleware
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "ScalingReads API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
