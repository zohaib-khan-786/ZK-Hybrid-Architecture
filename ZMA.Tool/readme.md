# ZMA Tool

**ZMA (Zohaib Modular Architecture) — Interactive CLI Scaffolder**

A dotnet global tool that interactively scaffolds ZMA architecture projects.

## Install

```shell
dotnet tool install -g ZMA.Tool
```

## Usage

### Interactive mode (default)

```shell
zma
```

### Non-interactive mode (CI pipelines)

```shell
zma --tier Small --name MyApp --output ./projects --non-interactive
```

### Check version / license status

```shell
zma --version
```

### Register a license key

```shell
zma --register --key ZMA-XXXX-XXXX-XXXX
```

## Arguments

| Flag | Alias | Description |
|------|-------|-------------|
| `--tier` | `-t` | Architecture tier: Small, Medium, or Large |
| `--name` | `-n` | Project name |
| `--output` | `-o` | Output directory (defaults to current directory) |
| `--non-interactive` | `--auto` | Skip all prompts; requires `--tier` and `--name` |
| `--register` | `-r` | Register a license key |
| `--key` | `-k` | License key (used with `--register`) |
| `--version` | `-v` | Show version and license status |

## Licensing

| Tier | Max Entities | Watermark | Price |
|------|-------------|-----------|-------|
| Free | 2 | Yes | $0 |
| Pro  | 99 | No | Purchase at zma.dev |

The free edition watermarks generated files. Purchase a Pro key at [zma.dev](https://zma.dev) and activate with:

```shell
zma --register --key YOUR-KEY-HERE
```

## What it scaffolds

```mermaid
graph LR
  subgraph Small["Small — Monolith"]
    direction LR
    S_P["Presentation"]
    S_A["Application"]
    S_D["Domain"]
    S_I["Infrastructure"]
    S_P --> S_A --> S_D
    S_A --> S_I
  end
  subgraph Medium["Medium — Modular Monolith"]
    direction LR
    M_P["Presentation"]
    M_AC["Application<br/>CatalogModule · OrdersModule"]
    M_I["Infrastructure<br/>CatalogDB · OrdersDB"]
    M_D["Domain"]
    M_P --> M_AC --> M_D
    M_AC --> M_I
  end
  subgraph Large["Large — Microservices"]
    direction TB
    L_GW["API Gateway"]
    L_CS["CatalogService"]
    L_OS["OrderService"]
    L_PS["PaymentService"]
    L_SK["SharedKernel"]
    L_GW --> L_CS & L_OS & L_PS
    L_CS -.-> L_SK
    L_OS -.-> L_SK
    L_PS -.-> L_SK
  end
  Small -->|grow| Medium -->|scale| Large
```

## Learn More

- GitHub: https://github.com/zohaib-khan-786/ZK-Hybrid-Architecture
