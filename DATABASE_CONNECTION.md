# Database Connection for Integration Tests

## Connection Details

| Parameter | Value |
|-----------|-------|
| Host | localhost |
| Port | 5434 |
| Database | klacks |
| Username | postgres |
| Password | admin |

## Connection String

```
Host=localhost;Port=5434;Database=klacks;Username=postgres;Password=admin
```

## Environment Variable

You can set the connection string via environment variable:

```bash
export DATABASE_URL="Host=localhost;Port=5434;Database=klacks;Username=postgres;Password=admin"
```

## Usage in Tests

```csharp
[OneTimeSetUp]
public void OneTimeSetUp()
{
    _connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
        ?? "Host=localhost;Port=5434;Database=klacks;Username=postgres;Password=admin";
}

[SetUp]
public async Task SetUp()
{
    var options = new DbContextOptionsBuilder<DataBaseContext>()
        .UseNpgsql(_connectionString)
        .Options;

    var mockHttpContextAccessor = Substitute.For<IHttpContextAccessor>();
    _context = new DataBaseContext(options, mockHttpContextAccessor);
}
```

## Running Tests

```bash
# Run all integration tests
dotnet test IntegrationTest/IntegrationTest.csproj

# Run specific test category
dotnet test IntegrationTest/IntegrationTest.csproj --filter "Category=RealDatabase"

# Run with custom connection string
DATABASE_URL="Host=localhost;Port=5434;Database=klacks;Username=postgres;Password=admin" dotnet test IntegrationTest/IntegrationTest.csproj
```

## Direct Database Access via psql

From Windows PowerShell:
```powershell
$env:PGPASSWORD='admin'
& 'C:\Program Files\PostgreSQL\17\bin\psql.exe' -h localhost -p 5434 -U postgres -d klacks -c "SELECT 1"
```

## Client Related Tables

The following tables are related to a Client entity:

| Table | Description |
|-------|-------------|
| `client` | Main client table |
| `membership` | Client membership validity period |
| `address` | Client addresses |
| `communication` | Phone numbers and emails |
| `annotation` | Notes/comments |
| `client_image` | Profile images |
| `client_contract` | Client contract assignments |
| `group_item` | Client group memberships |

## Test Cleanup

All test clients are prefixed with `INTEGRATION_TEST_` for easy cleanup:

```sql
DELETE FROM client WHERE first_name LIKE 'INTEGRATION_TEST_%';
```
