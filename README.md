# ScalingReads - PostgreSQL Master-Replica Read Scaling Project

A comprehensive .NET 10.0 Web API demonstrating database read scaling using PostgreSQL master-replica setup with Redis caching and Entity Framework Core.

## 📋 Table of Contents

- [Architecture Overview](#architecture-overview)
- [Database Setup](#database-setup)
- [Entity Framework Context Strategy](#entity-framework-context-strategy)
- [Prerequisites](#prerequisites)
- [Setup and Installation](#setup-and-installation)
- [Database Operations](#database-operations)
- [Development Workflow](#development-workflow)
- [Testing the Setup](#testing-the-setup)
- [Troubleshooting](#troubleshooting)
- [Project Structure](#project-structure)

## 🏗️ Architecture Overview

### Read Scaling Strategy

This project implements a sophisticated read scaling architecture that distributes database load across multiple PostgreSQL instances:

```
Write Ops → Master DB (5432)
Read Ops → Replica 1 (5433) / Replica 2 (5434) [Load Balanced]
Cache → Redis (6379)
```

### Key Components

1. **Master Database (PostgreSQL - Port 5432)**
   - Handles all write operations (INSERT, UPDATE, DELETE)
   - Source of truth for all data
   - Applies database migrations
   - Replicates data to replica databases

2. **Replica Databases (PostgreSQL - Ports 5433, 5434)**
   - Handle all read operations (SELECT)
   - Receive data from master via streaming replication
   - Load-balanced automatically by Npgsql
   - Configured as hot standbys (read-only)

3. **Redis Cache**
   - Provides high-performance caching layer
   - Reduces database load for frequently accessed data
   - Improves response times for read operations

4. **Application Layer**
   - `AppDbContext`: Used for write operations
   - `ReadOnlyDbContext`: Used for read operations (prevents writes)
   - Automatic load balancing between replicas via Npgsql connection string

### How It Works

- **Write Operations**: Always routed to the master database (port 5432)
- **Read Operations**: Load-balanced across replica databases (ports 5433, 5434)
- **Data Consistency**: PostgreSQL streaming replication ensures replicas stay synchronized
- **Caching Strategy**: Redis cache reduces database queries for frequently accessed data

## 🗄️ Database Setup

### Docker Compose Configuration

The project uses Docker Compose to orchestrate a PostgreSQL master-replica setup:

```yaml
services:
  redis:
    image: redis:alpine
    container_name: redis-cache
    ports:
      - "6379:6379"

  pg-master:
    image: postgres:18-alpine
    container_name: pg-master
    environment:
      POSTGRES_USER: admin
      POSTGRES_PASSWORD: admin_password
      POSTGRES_DB: appdb
    ports:
      - "5432:5432"

  pg-replica-1:
    image: postgres:18-alpine
    container_name: pg-replica-1
    environment:
      POSTGRES_PASSWORD: admin_password
    depends_on:
      - pg-master
    ports:
      - "5433:5432"

  pg-replica-2:
    image: postgres:18-alpine
    container_name: pg-replica-2
    environment:
      POSTGRES_PASSWORD: admin_password
    depends_on:
      - pg-master
    ports:
      - "5434:5432"
```

### Database Configuration

**Master Database (Port 5432)**
- **Host**: localhost
- **Port**: 5432
- **Database**: appdb
- **Username**: admin
- **Password**: admin_password
- **Purpose**: Write operations, migrations, data source of truth

**Replica Databases (Ports 5433, 5434)**
- **Host**: localhost
- **Port**: 5433 (replica 1), 5434 (replica 2)
- **Database**: appdb
- **Username**: postgres
- **Password**: admin_password
- **Purpose**: Read operations only

**Redis Cache**
- **Host**: localhost
- **Port**: 6379
- **Purpose**: High-performance caching

### Replication Setup

1. **Master Configuration** (`master/init-master.sh`):
   - Creates replication user (`replicator`)
   - Configures pg_hba.conf for replication access
   - Enables replication logging

2. **Replica Configuration** (`replica/init-replica.sh`):
   - Waits for master to be ready
   - Performs initial data sync using `pg_basebackup`
   - Configures as hot standby for read operations
   - Enables streaming replication

### Connection Strings

```json
{
  "ConnectionStrings": {
    "MasterConnection": "Host=localhost;Port=5432;Database=appdb;Username=admin;Password=admin_password",
    "ReplicaConnection": "Host=localhost,localhost;Port=5433,5434;Database=appdb;Username=postgres;Password=admin_password;Load Balance Hosts=true",
    "RedisConnection": "localhost:6379"
  }
}
```

**Key Points**:
- `Load Balance Hosts=true` enables automatic load balancing between replicas
- Multiple hosts are specified as comma-separated values
- Npgsql automatically distributes read queries across available replicas

## 🔧 Entity Framework Context Strategy

### AppDbContext - Write Operations

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

**Purpose**: 
- Handle all write operations (INSERT, UPDATE, DELETE)
- Manage database migrations
- Ensure data consistency
- Connected to master database

### ReadOnlyDbContext - Read Operations

```csharp
public class ReadOnlyDbContext : AppDbContext
{
    public ReadOnlyDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
        // Optimization: Disable change tracking by default
        // since this context should never perform writes
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
    }

    // Override all SaveChanges methods to prevent writes
    public override int SaveChanges() => ThrowReadOnlyException();
    public override int SaveChanges(bool acceptAllChangesOnSuccess) => ThrowReadOnlyException();
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => ThrowReadOnlyException();
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) => ThrowReadOnlyException();

    private static int ThrowReadOnlyException()
        => throw new InvalidOperationException("This context is read-only and does not allow data persistence.");
}
```

**Purpose**:
- Handle all read operations (SELECT)
- Prevent accidental writes by overriding SaveChanges methods
- Connected to replica databases with load balancing
- Optimized with no tracking for better performance

### Dependency Injection Configuration

```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. Connection Strings
var masterCs = builder.Configuration.GetConnectionString("MasterConnection");
var replicaCs = builder.Configuration.GetConnectionString("ReplicaConnection");

// 2. Register WRITE DbContext (Master - Port 5432)
// Used for: Migrations, Inserts, Updates, Deletes
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(masterCs));

// 3. Register READ DbContext (Replicas - Ports 5433 and 5434)
// Npgsql will automatically balance between the two ports
builder.Services.AddDbContext<ReadOnlyDbContext>(options => options.UseNpgsql(replicaCs));

// 4. Redis Cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("RedisConnection");
    options.InstanceName = "ScalingReads_";
});
```

## 📋 Prerequisites

Before setting up the project, ensure you have the following installed:

1. **.NET 10.0 SDK**
   - Download from: https://dotnet.microsoft.com/download/dotnet/10.0
   - Verify installation: `dotnet --version`

2. **Docker Desktop**
   - Download from: https://www.docker.com/products/docker-desktop
   - Ensure Docker daemon is running

3. **Git** (optional, for version control)
   - Download from: https://git-scm.com/downloads

## 🚀 Setup and Installation

### Step 1: Clone or Navigate to Project

```bash
# If cloning from repository
git clone <repository-url>
cd ex-scalingreads

# Or navigate to the project directory
cd /path/to/ex-scalingreads
```

### Step 2: Start Database Infrastructure

```bash
# Start all services (master, replicas, Redis)
docker-compose up -d

# Check service status
docker-compose ps
```

**Expected Output:**
```
     Name                    Command               State           Ports         
-------------------------------------------------------------------------------------
pg-master        docker-entrypoint.sh postgres    Up      0.0.0.0:5432->5432/tcp
pg-replica-1     docker-entrypoint.sh postgres    Up      0.0.0.0:5433->5432/tcp
pg-replica-2     docker-entrypoint.sh postgres    Up      0.0.0.0:5434->5432/tcp
redis-cache      docker-entrypoint.sh redis ...   Up      0.0.0.0:6379->6379/tcp
```

### Step 3: Verify Database Setup

```bash
# Check master database
docker exec -it pg-master psql -U admin -d appdb -c "SELECT version();"

# Check replication status (should show replica processes)
docker exec -it pg-master psql -U admin -d appdb -c "SELECT * FROM pg_stat_replication;"
```

### Step 4: Run Database Migrations

```bash
# Navigate to the project directory
cd ScalingReads.Core

# Add initial migration
dotnet ef migrations add initial -c AppDbContext

# Apply migrations to master database
dotnet ef database update -c AppDbContext

# Verify tables created
docker exec -it pg-master psql -U admin -d appdb -c "\dt"
```

### Step 5: Start the Application

```bash
# From ScalingReads.Core directory
dotnet run

# Or for HTTPS
dotnet run --launch-profile https

# Application will start on:
# HTTP: http://localhost:5291
# HTTPS: https://localhost:7062
```

## 🗄️ Database Operations

### Migration Commands

```bash
# Add a new migration
dotnet ef migrations add MigrationName -c AppDbContext

# Apply pending migrations
dotnet ef database update -c AppDbContext

# Remove last migration (if not applied)
dotnet ef migrations remove -c AppDbContext

# Reset database (⚠️ Destroys all data)
dotnet ef database drop -c AppDbContext
dotnet ef database update -c AppDbContext
```

### Entity Framework CLI Tools

If you encounter issues with `dotnet ef` commands, install the tools globally:

```bash
dotnet tool install --global dotnet-ef
```

### Understanding Migrations

Migrations are applied **only to the master database**. The replicas automatically receive changes through PostgreSQL streaming replication. This ensures:

1. **Data Consistency**: All databases have identical schemas
2. **Automatic Sync**: Replicas stay synchronized without manual intervention
3. **Single Source of Truth**: Master database is the only source for schema changes

## 🔄 Development Workflow

### For New Developers

1. **Initial Setup**:
   ```bash
   # Start infrastructure
   docker-compose up -d
   
   # Wait for databases to be ready (30 seconds)
   sleep 30
   
   # Run migrations
   cd ScalingReads.Core
   dotnet ef database update -c AppDbContext
   
   # Start application
   dotnet run
   ```

2. **Making Changes**:
   ```bash
   # Create new migration for schema changes
   dotnet ef migrations add YourMigrationName -c AppDbContext
   
   # Apply to master database
   dotnet ef database update -c AppDbContext
   
   # Test your changes
   dotnet run
   ```

3. **Common Development Tasks**:
   ```bash
   # View current migration status
   dotnet ef migrations list -c AppDbContext
   
   # Generate SQL script (for review)
   dotnet ef migrations script -c AppDbContext
   
   # Check database schema
   docker exec -it pg-master psql -U admin -d appdb -c "\dt"
   ```

### Adding New Features

1. **Create Models**:
   ```csharp
   public class YourEntity
   {
       public int Id { get; set; }
       public required string Name { get; set; }
   }
   ```

2. **Update DbContext**:
   ```csharp
   public DbSet<YourEntity> YourEntities => Set<YourEntity>();
   ```

3. **Create Migration**:
   ```bash
   dotnet ef migrations add AddYourEntity -c AppDbContext
   dotnet ef database update -c AppDbContext
   ```

### Code Organization

- **Models**: `ScalingReads.Core/Models/`
- **Data Contexts**: `ScalingReads.Core/Data/`
- **Controllers**: `ScalingReads.Core/Controllers/`
- **IO DTOs**: `ScalingReads.Core/IO/`
- **Migrations**: `ScalingReads.Core/Migrations/`

## 🧪 Testing the Setup

### Test Write Operations

```bash
# Test creating an album (writes to master)
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

**Expected Response:**
```json
{
  "id": 1
}
```

### Test Read Operations

```bash
# Check if data is replicated to replicas
docker exec -it pg-replica-1 psql -U postgres -d appdb -c "SELECT * FROM \"Albums\";"
docker exec -it pg-replica-2 psql -U postgres -d appdb -c "SELECT * FROM \"Albums\";"
```

### Test Load Balancing

```bash
# Multiple read requests (should be distributed across replicas)
for i in {1..10}; do
  curl http://localhost:5291/api/album/1
  echo "Request $i completed"
  sleep 1
done
```

### Verify Replication Status

```bash
# Check replication lag
docker exec -it pg-master psql -U admin -d appdb -c "
SELECT 
  application_name,
  client_addr,
  state,
  replay_lag,
  write_lag,
  flush_lag
FROM pg_stat_replication;
"

# Check database connections
docker exec -it pg-master psql -U admin -d appdb -c "
SELECT 
  datname,
  usename,
  client_addr,
  state
FROM pg_stat_activity 
WHERE datname = 'appdb';
"
```

## 🔧 Troubleshooting

### Common Issues and Solutions

#### 1. Database Connection Issues

**Problem**: Cannot connect to databases
```bash
# Check if containers are running
docker-compose ps

# Check logs
docker-compose logs pg-master
docker-compose logs pg-replica-1
docker-compose logs pg-replica-2
```

**Solutions**:
```bash
# Restart services
docker-compose down
docker-compose up -d

# Check port conflicts
netstat -an | grep 543
```

#### 2. Migration Issues

**Problem**: Migration fails or database not found

**Solutions**:
```bash
# Ensure master database is ready
docker exec -it pg-master pg_isready -U admin -d appdb

# Check connection string
# Verify appsettings.json has correct values

# Reset migrations (⚠️ Deletes data)
dotnet ef database drop -c AppDbContext
dotnet ef database update -c AppDbContext
```

#### 3. Replication Issues

**Problem**: Replicas not receiving data from master

**Check replication status**:
```bash
# On master
docker exec -it pg-master psql -U admin -d appdb -c "
SELECT * FROM pg_stat_replication;
"

# Expected: Should show 2 replica connections
```

**Solutions**:
```bash
# Restart replicas
docker-compose restart pg-replica-1 pg-replica-2

# Check replica logs
docker-compose logs pg-replica-1
docker-compose logs pg-replica-2

# Manual replica sync (⚠️ Advanced)
docker exec -it pg-replica-1 psql -U postgres -c "SELECT pg_drop_replication_slot('default');"
docker-compose restart pg-replica-1
```

#### 4. Redis Connection Issues

**Problem**: Redis cache not working

**Check Redis status**:
```bash
# Test Redis connection
docker exec -it redis-cache redis-cli ping
# Expected: PONG
```

**Solutions**:
```bash
# Restart Redis
docker-compose restart redis-cache

# Clear Redis cache
docker exec -it redis-cache redis-cli FLUSHALL
```

#### 5. Port Conflicts

**Problem**: Ports already in use

**Solutions**:
```bash
# Find process using port
netstat -ano | findstr :5432
netstat -ano | findstr :5433
netstat -ano | findstr :5434
netstat -ano | findstr :6379

# Kill process (replace PID with actual process ID)
taskkill /PID <PID> /F
```

### Performance Troubleshooting

#### Check Database Performance

```bash
# Check slow queries
docker exec -it pg-master psql -U admin -d appdb -c "
SELECT query, mean_time, calls 
FROM pg_stat_statements 
ORDER BY mean_time DESC 
LIMIT 10;
"

# Check connection counts
docker exec -it pg-master psql -U admin -d appdb -c "
SELECT state, count(*) 
FROM pg_stat_activity 
GROUP BY state;
"
```

#### Monitor Application Logs

```bash
# Enable detailed EF Core logging
# In appsettings.Development.json:
{
  "Logging": {
    "LogLevel": {
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  }
}
```

### Health Checks

#### Database Health

```bash
# Master database
docker exec -it pg-master pg_isready -U admin -d appdb

# Replica databases
docker exec -it pg-replica-1 pg_isready -U postgres -d appdb
docker exec -it pg-replica-2 pg_isready -U postgres -d appdb

# Redis
docker exec -it redis-cache redis-cli ping
```

#### Application Health

```bash
# Check application is responding
curl http://localhost:5291/api/album/1

# Check OpenAPI documentation
open http://localhost:5291/swagger
```

## 📁 Project Structure

```
ex-scalingreads/
├── README.md                           # This file
├── docker-compose.yaml                 # Database infrastructure
├── .gitignore                          # Git ignore rules
├── ex-scalingreads.slnx                # Solution file
├── master/                             # Master database configuration
│   ├── init-master.sh                  # Master initialization script
│   └── pg_hba.conf                     # PostgreSQL authentication config
├── replica/                            # Replica database configuration
│   └── init-replica.sh                 # Replica initialization script
└── ScalingReads.Core/                  # Main application project
    ├── Controllers/                    # API Controllers
    │   └── AlbumController.cs          # Album CRUD operations
    ├── Data/                           # Entity Framework contexts
    │   ├── AppDbContext.cs             # Write operations context
    │   └── ReadOnlyDbContext.cs        # Read-only context
    ├── IO/                             # Input/Output DTOs
    │   ├── PostAlbumInput.cs           # Album creation input
    │   └── PostAlbumOutput.cs          # Album creation output
    ├── Models/                         # Entity models
    │   ├── Album.cs                    # Album entity
    │   └── Song.cs                     # Song entity (owned by Album)
    ├── Migrations/                     # Entity Framework migrations
    │   ├── 20251227154713_initial.cs   # Initial migration
    │   ├── 20251227154713_initial.Designer.cs
    │   └── AppDbContextModelSnapshot.cs
    ├── Properties/                     # Application properties
    │   └── launchSettings.json         # Launch configuration
    ├── Program.cs                      # Application entry point
    ├── appsettings.json                # Application configuration
    └── ScalingReads.Core.csproj        # Project file
```

### Key Files Explained

- **docker-compose.yaml**: Defines the PostgreSQL master-replica infrastructure
- **Program.cs**: Configures dependency injection and database contexts
- **AppDbContext.cs**: Handles write operations and migrations
- **ReadOnlyDbContext.cs**: Handles read operations with write protection
- **AlbumController.cs**: Demonstrates write operations using AppDbContext
- **init-master.sh**: Sets up replication on the master database
- **init-replica.sh**: Configures replicas to sync from master

## 🎯 Key Takeaways

1. **Separation of Concerns**: Write operations use AppDbContext, read operations use ReadOnlyDbContext
2. **Automatic Load Balancing**: Npgsql distributes read queries across replicas
3. **Data Consistency**: PostgreSQL streaming replication keeps replicas synchronized
4. **Performance Optimization**: Redis caching reduces database load
5. **Development Friendly**: Clear separation makes it easy to understand and maintain

This project demonstrates enterprise-grade read scaling patterns that can be adapted for production use. The architecture ensures high availability, performance, and data consistency while maintaining a clean, maintainable codebase.