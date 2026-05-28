# ZMA.Licensing

**License validation client for ZMA Toolkit — server-side validation, offline caching, machine fingerprinting.**

## Install

```shell
dotnet add package ZMA.Licensing
```

## Usage

```csharp
using ZMA.Licensing;

var validator = new LicenseValidator();

// Register a key
await validator.RegisterAsync("ZMA-XXXX-XXXX-XXXX");

// Check if we can migrate N entities
if (validator.CanMigrateEntityCount(entities.Count))
{
    // proceed with migration
}
else
{
    Console.WriteLine($"Free tier limited to {validator.CurrentLicense.MaxEntities} entities.");
    Console.WriteLine("Purchase a Pro license at https://zma.dev");
}
```

## Validation Flow

```mermaid
sequenceDiagram
  participant Tool as ZMA Tool
  participant Cache as ~/.zma/license
  participant Server as License Server
  participant DB as PostgreSQL

  Tool->>Cache: Check cached license
  alt Cache hit & fresh (&lt; 24h)
    Cache-->>Tool: Return cached license
  else Cache miss or expired
    Tool->>Server: POST /api/license/validate
    Server->>DB: Lookup key + machine fingerprint
    DB-->>Server: License info
    Server-->>Tool: Valid / Invalid
    Tool->>Cache: Save result (24h TTL)
  end
```

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `ZMA_LICENSE_SERVER` | `https://zma-license.up.railway.app` | License validation server URL |

## Caching

License state is cached at `~/.zma/license` with a 24-hour TTL. The tool works offline between refreshes.

## Learn More

- GitHub: https://github.com/zohaib-khan-786/ZK-Hybrid-Architecture
- License Server: `license-server/` in the same repo
