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

Prompts you to select a tier (Small, Medium, Large), enter a project name, and choose an output directory.

### Non-interactive mode (CI pipelines)

```shell
zma --tier Small --name MyApp --output ./projects --non-interactive
```

Arguments:

| Flag | Alias | Description |
|------|-------|-------------|
| `--tier` | `-t` | Architecture tier: Small, Medium, or Large |
| `--name` | `-n` | Project name |
| `--output` | `-o` | Output directory (defaults to current directory) |
| `--non-interactive` | `--auto` | Skip all prompts; requires `--tier` and `--name` |

## What it scaffolds

- **Small**: Monolith with Domain, Application, Infrastructure, Presentation layers
- **Medium**: Modular Monolith with Catalog + Orders modules, separate DbContexts
- **Large**: Microservices with CatalogService, OrderService, PaymentService + API Gateway + SharedKernel

## Learn More

- GitHub: https://github.com/zohaib-khan-786/ZK-Hybrid-Architecture
