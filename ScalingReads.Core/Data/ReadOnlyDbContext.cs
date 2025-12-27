using Microsoft.EntityFrameworkCore;

namespace ScalingReads.Core.Data;

public class ReadOnlyDbContext : AppDbContext
{
    public ReadOnlyDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
        // Otimização: Desabilita o rastreamento de mudanças por padrão, 
        // já que este contexto nunca deve realizar Inserts/Updates.
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
    }

    public override int SaveChanges() => ThrowReadOnlyException();
    public override int SaveChanges(bool acceptAllChangesOnSuccess) => ThrowReadOnlyException();
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => ThrowReadOnlyException();
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) => ThrowReadOnlyException();

    private static int ThrowReadOnlyException()
        => throw new InvalidOperationException("Este contexto é exclusivo para leitura e não permite persistência de dados.");
}
