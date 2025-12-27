# Database Read Scaling Tutorial: PostgreSQL Master-Replica with Redis Caching

This tutorial will guide you through building and understanding a database read scaling system using PostgreSQL master-replica architecture, automatic load balancing, and Redis caching. By the end, you'll understand how to distribute read operations across multiple database instances while maintaining data consistency and improving performance.

## Table of Contents

- [What is Database Read Scaling?](#what-is-database-read-scaling)
- [Core Concepts](#core-concepts)
- [System Architecture](#system-architecture)
- [Prerequisites](#prerequisites)
- [Step-by-Step Setup](#step-by-step-setup)
- [Understanding the Code](#understanding-the-code)
- [Practical Examples](#practical-examples)
- [Troubleshooting](#troubleshooting)
- [Advanced Topics](#advanced-topics)

## What is Database Read Scaling?

Database read scaling is a technique to handle high-volume read operations by distributing them across multiple database instances. Instead of all queries hitting a single database, read operations are load-balanced across replica databases while write operations remain centralized on a master database.

### Why Read Scaling Matters

- **Performance**: Multiple replicas can handle more concurrent read requests
- **Availability**: System continues working even if one replica fails
- **Cost Efficiency**: Replicas can use less powerful hardware than the master
- **User Experience**: Faster response times for read-heavy applications

### Real-World Applications

Read scaling is essential for:
- Social media platforms (timeline reads)
- E-commerce sites (product catalog browsing)
- Analytics dashboards (report generation)
- Content management systems (article viewing)

## Core Concepts

### Master-Replica Architecture

**Master Database**: The single source of truth that handles all write operations (INSERT, UPDATE, DELETE). It maintains the authoritative copy of all data.

**Replica Databases**: Read-only copies of the master that receive data changes through streaming replication. They handle SELECT queries and are automatically kept in sync.

### Load Balancing

Load balancing distributes read requests across multiple replica databases to prevent any single instance from becoming a bottleneck. Our implementation uses Npgsql's built-in load balancing, which automatically routes queries to available replicas.

### Caching Layer

Redis caching stores frequently accessed data in memory for ultra-fast retrieval. This reduces database load and improves response times for popular content.

## System Architecture

```
Application → Load Balancer → Replicas (5433, 5434) + Redis Cache
                      ↓
               Master Database (5432)
```

### Data Flow

1. **Write Operations**: Application → Master Database
2. **Read Operations**: Application → Load Balancer → Replica 1/2 (with Redis cache)
3. **Replication**: Master → Replicas (automatic streaming)

## Prerequisites

Before starting, ensure you have:

1. **.NET 10.0 SDK**
   ```bash
   # Download from https://dotnet.microsoft.com/download/dotnet/10.0
   dotnet --version  # Should show 10.0.x
   ```

2. **Docker Desktop**
   ```bash
   # Download from https://www.docker.com/products/docker-desktop
   docker --version  # Should show version info
   ```

3. **Git** (for cloning the repository)
   ```bash
   git --version
   ```

## Step-by-Step Setup

### Step 1: Get the Project

```bash
# Clone the repository
git clone <repository-url>
cd ex-scalingreads

# Or if you already have it, navigate to the directory
cd /path/to/ex-scalingreads
```

### Step 2: Start the Database Infrastructure

Our system uses Docker Compose to run PostgreSQL master-replica setup and Redis cache.

```bash
# Start all services
docker-compose up -d

# Check that all services are running
docker-compose ps
```

You should see output like:
```
      Name                    Command               State           Ports
-------------------------------------------------------------------------------------
pg-master        docker-entrypoint.sh postgres    Up      0.0.0.0:5432->5432/tcp
pg-replica-1     docker-entrypoint.sh postgres    Up      0.0.0.0:5433->5432/tcp
pg-replica-2     docker-entrypoint.sh postgres    Up      0.0.0.0:5434->5432/tcp
redis-cache      docker-entrypoint.sh redis ...   Up      0.0.0.0:6379->6379/tcp
```

### Step 3: Verify Database Replication

Let's check that our databases are properly set up and replicating.

```bash
# Check master is ready
docker exec -it pg-master pg_isready -U admin -d appdb

# Check replication status (should show 2 replicas)
docker exec -it pg-master psql -U admin -d appdb -c "SELECT * FROM pg_stat_replication;"
```

The replication query should show 2 connected replicas.

### Step 4: Set Up the Database Schema

```bash
# Navigate to the application directory
cd ScalingReads.Core

# Create and apply database migrations
dotnet ef database update -c AppDbContext

# Verify tables were created
docker exec -it pg-master psql -U admin -d appdb -c "\dt"
```

You should see tables like "Albums" and "Songs" created.

### Step 5: Start the Application

```bash
# Run the .NET application
dotnet run

# Or for HTTPS development
dotnet run --launch-profile https
```

The application will start on:
- HTTP: http://localhost:5291
- HTTPS: https://localhost:7062
- API Documentation: http://localhost:5291/swagger

## PostgreSQL Master-Replica Configuration Deep Dive

This section provides an in-depth explanation of the PostgreSQL streaming replication setup used in this tutorial, covering every configuration aspect from parameters to security.

### 1. PostgreSQL Configuration Parameters

The master database is configured with specific parameters in `docker-compose.yaml` to enable streaming replication:

- **`wal_level=replica`**: Sets the Write-Ahead Logging level to 'replica', which includes all information needed for archiving and replication. This parameter is essential for streaming replication as it ensures WAL contains sufficient data for replicas to reconstruct the master's state.

- **`max_wal_senders=10`**: Specifies the maximum number of concurrent connections from standby servers or streaming base backup clients. Set to 10 to allow multiple replicas and backup operations, providing headroom for scaling.

- **`max_replication_slots=10`**: Defines the maximum number of replication slots that the server can support. Replication slots ensure that the master retains WAL segments until all replicas have received them, preventing premature cleanup that could break replication.

### 2. Replication User Setup

The `init/01-replication.sql` script creates a dedicated replication user:

```sql
CREATE ROLE replicator
  WITH REPLICATION
  LOGIN
  PASSWORD 'repl_password';
```

This user is specifically created for replication purposes and has the `REPLICATION` privilege, which allows it to connect for replication operations. Using a separate user follows security best practices by limiting permissions to only what's necessary for replication, reducing the attack surface compared to using administrative accounts.

### 3. Authentication Configuration

The `init/02-pg-hba.sh` script configures client authentication by appending entries to `pg_hba.conf`:

```bash
echo "host replication replicator 0.0.0.0/0 scram-sha-256" >> "$PGDATA/pg_hba.conf"
echo "host all all 0.0.0.0/0 scram-sha-256" >> "$PGDATA/pg_hba.conf"
```

- The first line allows the `replicator` user to connect for replication from any IP address within the Docker network (0.0.0.0/0) using SCRAM-SHA-256 authentication.
- The second line allows all users to connect to any database using SCRAM-SHA-256 authentication.
- SCRAM-SHA-256 (Salted Challenge Response Authentication Mechanism) is a modern, secure password-based authentication method that provides better security than older methods like MD5 by using salted hashing and preventing replay attacks.

### 4. Replica Initialization Process

The `replica.sh` script orchestrates the complete replica initialization and startup process:

1. **Directory Preparation**: Creates and sets proper permissions on the PostgreSQL data directory, ensuring the postgres user has ownership and secure access (700 permissions).

2. **Master Readiness Verification**: Uses a loop with `pg_isready` to wait for the master database to become available, preventing startup failures.

3. **Base Backup Execution**: When the data directory is empty, performs an initial base backup from the master using `pg_basebackup`:
   - `-h pg-master`: Specifies the master host
   - `-U replicator`: Authenticates using the replication user
   - `-D $DATA_DIR`: Sets the destination data directory
   - `-Fp`: Uses plain format for easier inspection
   - `-Xs`: Includes required WAL segments in the backup for consistency
   - `-P`: Displays progress information
   - `-R`: Automatically creates `standby.signal` and configures recovery settings

4. **PostgreSQL Startup**: Launches PostgreSQL in hot standby mode with `hot_standby=on`, enabling read-only queries while maintaining replication capability.

### 5. Health Checks and Startup Order

Docker Compose manages service dependencies and health verification:

- **`depends_on` with `condition: service_healthy`**: Replicas explicitly wait for the master to pass its health check before starting, ensuring proper initialization order.
- **Master Health Check**: Uses `pg_isready` to verify the master database is accepting connections, with 5-second intervals, 5-second timeouts, and 10 retries for robust startup.
- **Sequential Startup**: This dependency chain ensures the master is fully operational before replicas attempt to connect, preventing replication setup failures.

### 6. Streaming Replication Mechanics

Data flows from master to replicas through PostgreSQL's streaming replication:

1. **WAL Generation**: The master generates Write-Ahead Log (WAL) records for every database change, providing a sequential record of all modifications.

2. **Real-time Streaming**: WAL records are transmitted to replicas via dedicated TCP connections established by the replication user, ensuring minimal latency.

3. **Continuous Replay**: Replicas receive WAL records and replay them in order, applying changes to maintain an exact copy of the master's data.

4. **Hot Standby Operation**: While receiving updates, replicas can simultaneously serve read-only queries, enabling load distribution without interrupting replication.

### 7. Security Considerations

The configuration implements multiple security layers:

- **Strong Authentication**: SCRAM-SHA-256 provides modern cryptographic protection for passwords.
- **Network Segmentation**: Docker's internal networking limits database access to containerized services only.
- **Principle of Least Privilege**: The replication user has only the minimum permissions required for its function.
- **Access Control**: `pg_hba.conf` entries restrict connection types and authentication methods appropriately.
- **Encrypted Communication**: All replication traffic occurs over authenticated, encrypted connections within the Docker network.

## Understanding the Code

### Database Contexts: Separating Reads and Writes

Our application uses two Entity Framework contexts to enforce separation of concerns:

**AppDbContext** (for writes):
```csharp
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {

    }

    public DbSet<Album> Albums => Set<Album>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Album>(e =>
        {
            e.HasIndex(a => a.Id);
            e.Property(a => a.Title).IsRequired();
            e.OwnsMany(a => a.Songs, sa =>
            {
                sa.WithOwner().HasForeignKey("AlbumId");
                sa.Property(s => s.Title).IsRequired();
            });
        });
    }
}
```

**ReadOnlyDbContext** (for reads):
```csharp
public class ReadOnlyDbContext : AppDbContext
{
    public ReadOnlyDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
    }

    public override int SaveChanges() => ThrowReadOnlyException();
    public override int SaveChanges(bool acceptAllChangesOnSuccess) => ThrowReadOnlyException();
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => ThrowReadOnlyException();
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) => ThrowReadOnlyException();

    private static int ThrowReadOnlyException()
        => throw new InvalidOperationException();
}
```

### Connection Configuration

In `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "MasterConnection": "Host=localhost;Port=5432;Database=appdb;Username=admin;Password=admin_password",
    "ReplicaConnection": "Host=localhost,localhost;Port=5433,5434;Database=appdb;Username=postgres;Password=admin_password;Load Balance Hosts=true",
    "RedisConnection": "localhost:6379"
  }
}
```

The `Load Balance Hosts=true` setting tells Npgsql to automatically distribute read queries across the replica databases.

### Dependency Injection Setup

In `Program.cs`:
```csharp
var masterCs = builder.Configuration.GetConnectionString("MasterConnection");
var replicaCs = builder.Configuration.GetConnectionString("ReplicaConnection");

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(masterCs));

builder.Services.AddDbContext<ReadOnlyDbContext>(options => options.UseNpgsql(replicaCs));

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
    options.InstanceName = "app-cache:";
});
```

### API Controller with Caching

The `AlbumController` demonstrates both write and read patterns:

```csharp
[Route("api/[controller]")]
[ApiController]
public class AlbumController : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PostAlbumOutput>> Post([FromServices] AppDbContext dbContext, [FromBody] PostAlbumInput input)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        using var transaction = await dbContext.Database.BeginTransactionAsync();

        var newAlbum = new Album()
        {
            Title = input.Title,
            Songs = [.. input.Songs.Select(s => new Song(s.Title))]
        };

        await dbContext.Albums.AddAsync(newAlbum);

        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        var result = new PostAlbumOutput(newAlbum.Id);

        return Ok(result);
    }

    [HttpGet("{id}")]
    [Cache(ttlSeconds: 120)]
    public async Task<ActionResult<GetAlbumOutput>> Get([FromServices] ReadOnlyDbContext dbContext, [FromRoute] int id)
    {
        var album = await dbContext.Albums
            .Where(a => a.Id == id)
            .Select(a => new GetAlbumOutput(
                a.Id,
                a.Title,
                a.Songs.Select(s => new GetAlbumSongOutput(s.Title)).ToList()
            )).FirstOrDefaultAsync();

        return Ok(album);
    }
}
```

### Custom Caching Attribute

The `CacheAttribute` provides automatic Redis caching:

```csharp
[AttributeUsage(AttributeTargets.Method)]
public class CacheAttribute(int ttlSeconds = 60) : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var cache = context.HttpContext.RequestServices.GetRequiredService<IDistributedCache>();

        var cacheKey = BuildCacheKey(context);

        var cached = await cache.GetStringAsync(cacheKey);

        if (cached is not null)
        {
            var result = JsonSerializer.Deserialize<object>(cached);
            context.Result = new OkObjectResult(result);
            return;
        }

        var executed = await next();

        if (executed.Result is ObjectResult objectResult && objectResult.Value is not null)
        {
            var data = JsonSerializer.Serialize(objectResult.Value);

            await cache.SetStringAsync(
                cacheKey,
                data,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow =
                        TimeSpan.FromSeconds(ttlSeconds)
                });
        }
    }

    private static string BuildCacheKey(ActionExecutingContext context)
    {
        var route = context.HttpContext.Request.Path.Value ?? "";

        if (context.ActionDescriptor is not ControllerActionDescriptor controllerAction)
            return $"endpoint:{route}";

        var relevantArgs = new Dictionary<string, object?>();

        foreach (var param in controllerAction.MethodInfo.GetParameters())
        {
            if (param.GetCustomAttributes(typeof(FromServicesAttribute), false).Length != 0)
                continue;

            if (context.ActionArguments.TryGetValue(param.Name!, out var value))
            {
                relevantArgs[param.Name!] = value;
            }
        }

        var argsJson = JsonSerializer.Serialize(relevantArgs);

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(argsJson));
        var hash = Convert.ToHexString(hashBytes);

        return $"endpoint:{route}:{hash}";
    }
}
```

## Practical Examples

### Creating Data

```bash
# Create a new album (writes to master)
curl -X POST http://localhost:5291/api/album \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Test Album",
    "songs": [
      {"title": "Song 1"},
      {"title": "Song 2"}
    ]
  }'
```

Response:
```json
{
  "id": 1
}
```

### Reading Data with Caching

```bash
# First read (goes to database, then cached)
curl http://localhost:5291/api/album/1

# Second read (served from Redis cache)
curl http://localhost:5291/api/album/1
```

Both requests return:
```json
{
  "id": 1,
  "title": "Test Album",
  "songs": [
    {"title": "Song 1"},
    {"title": "Song 2"}
  ]
}
```

### Verifying Load Balancing

```bash
# Make multiple read requests
for i in {1..10}; do
  curl -s http://localhost:5291/api/album/1 > /dev/null
  echo "Request $i completed"
  sleep 0.5
done
```

Check which replica handled the requests:
```bash
# Check replica 1 connections
docker exec -it pg-replica-1 psql -U postgres -d appdb -c "SELECT count(*) FROM pg_stat_activity WHERE datname='appdb';"

# Check replica 2 connections
docker exec -it pg-replica-2 psql -U postgres -d appdb -c "SELECT count(*) FROM pg_stat_activity WHERE datname='appdb';"
```

### Checking Cache Contents

```bash
# View all cache keys
docker exec -it redis-cache redis-cli keys "*"

# Get cached album data
docker exec -it redis-cache redis-cli get "app-cache:album-1"
```

## Troubleshooting

### Database Connection Issues

**Problem**: Can't connect to databases
```bash
# Check container status
docker-compose ps

# View logs
docker-compose logs pg-master
docker-compose logs pg-replica-1
```

**Solution**:
```bash
# Restart services
docker-compose down
docker-compose up -d

# Wait for health checks
sleep 30
```

### Replication Not Working

**Problem**: Replicas not receiving data
```bash
# Check replication status
docker exec -it pg-master psql -U admin -d appdb -c "SELECT * FROM pg_stat_replication;"
```

**Solution**:
```bash
# Restart replicas
docker-compose restart pg-replica-1 pg-replica-2

# Check replica logs
docker-compose logs pg-replica-1
```

### Migration Failures

**Problem**: Database migrations fail
```bash
# Ensure master is ready
docker exec -it pg-master pg_isready -U admin -d appdb

# Reset if needed (WARNING: destroys data)
dotnet ef database drop -c AppDbContext
dotnet ef database update -c AppDbContext
```

### Cache Not Working

**Problem**: Redis caching not functioning
```bash
# Test Redis connection
docker exec -it redis-cache redis-cli ping  # Should return PONG

# Clear cache if needed
docker exec -it redis-cache redis-cli FLUSHALL
```

### Port Conflicts

**Problem**: Ports already in use
```bash
# Find conflicting processes
netstat -ano | findstr :5432
netstat -ano | findstr :5433
netstat -ano | findstr :5434
netstat -ano | findstr :6379

# Kill process (replace PID)
taskkill /PID <PID> /F
```

## Advanced Topics

### Monitoring Replication Lag

```bash
# Check replication lag
docker exec -it pg-master psql -U admin -d appdb -c "
SELECT
  application_name,
  replay_lag,
  write_lag,
  flush_lag
FROM pg_stat_replication;
"
```

### Performance Optimization

- **Connection Pooling**: Npgsql automatically pools connections
- **No-Tracking Queries**: ReadOnlyDbContext disables change tracking
- **Strategic Caching**: Cache frequently accessed data with appropriate TTL
- **Index Optimization**: Ensure proper indexes on frequently queried columns

### Scaling Further

- **Add More Replicas**: Scale horizontally by adding more replica containers
- **Read/Write Split**: Extend to route different query types appropriately
- **Multi-Region**: Deploy replicas across geographic regions
- **Cache Warming**: Pre-populate cache with popular data

### Production Considerations

- **Backup Strategy**: Regular backups of master database
- **Failover**: Automatic promotion of replica to master if needed
- **Security**: Use proper authentication and network isolation
- **Monitoring**: Implement comprehensive monitoring and alerting

## Conclusion

You've now built and understood a complete database read scaling system. The architecture demonstrates:

- **Separation of concerns** between read and write operations
- **Automatic load balancing** across replica databases
- **High-performance caching** with Redis
- **Data consistency** through streaming replication
- **Production-ready patterns** for scalable applications

This foundation can be extended for more complex scenarios and serves as a solid starting point for understanding enterprise-grade database scaling patterns.

## Next Steps

- Experiment with adding more replicas
- Implement cache invalidation strategies
- Add monitoring and metrics
- Explore advanced PostgreSQL features like logical replication
- Consider implementing write-through caching

The API documentation is available at `http://localhost:5291/swagger` for interactive testing.