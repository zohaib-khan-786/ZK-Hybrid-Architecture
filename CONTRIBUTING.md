# Contributing to ZMA — Zohaib Modular Architecture

First off, thank you for taking the time to contribute! 🙏  
ZMA is built for the .NET community, and every contribution — big or small — makes it better for everyone.

---

## 📋 Table of Contents

- [Code of Conduct](#code-of-conduct)
- [How Can I Contribute?](#how-can-i-contribute)
- [Reporting Bugs](#reporting-bugs)
- [Suggesting Features](#suggesting-features)
- [Submitting a Pull Request](#submitting-a-pull-request)
- [Project Structure](#project-structure)
- [Development Setup](#development-setup)
- [Commit Message Guidelines](#commit-message-guidelines)

---

## Code of Conduct

This project follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md).  
By participating, you agree to uphold it. Please report unacceptable behavior to `killerzobi893@gmail.com`.

---

## How Can I Contribute?

There are many ways to contribute — you don't have to write code:

- ⭐ **Star the repo** — helps other .NET developers discover ZMA
- 🐛 **Report bugs** — open an issue with clear reproduction steps
- 💡 **Suggest features** — open a discussion or issue with your idea
- 📖 **Improve documentation** — fix typos, clarify confusing sections, add examples
- 🧪 **Write tests** — help us improve coverage across the migrator and templates
- 🔧 **Submit a PR** — fix a bug or implement a feature

---

## Reporting Bugs

Before opening a bug report, please:
1. Check [existing issues](https://github.com/zohaib-khan-786/ZK-Hybrid-Architecture/issues) to avoid duplicates
2. Make sure you're on the latest version of `ZMA.Tool` and `ZMA.Migrator`

When reporting a bug, include:

- **What you did** — the exact command you ran
- **What you expected** — what should have happened
- **What actually happened** — error message, wrong output, etc.
- **Your environment** — OS, .NET version, ZMA tool version (`zma --version`)

**Example bug report title:**
```
zma-migrate --tier large fails when project has more than 3 entities
```

---

## Suggesting Features

Open a [GitHub Discussion](https://github.com/zohaib-khan-786/ZK-Hybrid-Architecture/discussions) under the **Ideas** category.

Good feature requests include:
- The problem you're trying to solve
- Why existing functionality doesn't cover it
- What the ideal solution would look like

**Popular ideas we're tracking:**
- Docker / docker-compose scaffolding per tier
- GitHub Actions CI/CD templates
- CQRS + MediatR support in Medium template
- PostgreSQL / SQL Server config out of the box

---

## Submitting a Pull Request

### 1. Fork and clone
```shell
git clone https://github.com/YOUR-USERNAME/ZK-Hybrid-Architecture.git
cd ZK-Hybrid-Architecture
```

### 2. Create a branch
```shell
git checkout -b feature/your-feature-name
# or
git checkout -b fix/bug-description
```

### 3. Make your changes

Follow the existing code style. Keep changes focused — one PR per feature or fix.

### 4. Test your changes

**For ZMA.Tool:**
```shell
cd ZMA.Tool
dotnet run -- --tier Small --name TestApp --output ./test-output --non-interactive
```

**For ZMA.Migrator:**
```shell
cd ZMA.Migrator
dotnet run -- --source ./test-output --tier Medium --dry-run
```

**For templates:**
```shell
dotnet new install ./ZMA.Small.Template
dotnet new zma-small -n MyTestApp
cd MyTestApp/src/MyTestApp.Presentation
dotnet run
```

Make sure the generated project compiles with **0 errors, 0 warnings**.

### 5. Commit and push
```shell
git add .
git commit -m "feat: add docker-compose scaffolding to Large template"
git push origin feature/your-feature-name
```

### 6. Open a Pull Request

Go to GitHub and open a PR against the `master` branch. Fill in:
- What the PR does
- Why it's needed
- How you tested it

---

## Project Structure

```
ZK-Hybrid-Architecture/
├── ZMA.Small.Template/     # Small tier dotnet new template
├── Medium/                 # Medium tier dotnet new template
├── Large/                  # Large tier dotnet new template
├── ZMA.Tool/               # Interactive CLI scaffolder (dotnet global tool)
├── ZMA.Migrator/           # Architecture migration CLI (dotnet global tool)
├── ZMA.Licensing/          # License validation client library
├── license-server/         # Node.js/TypeScript license server
├── docs/                   # Architecture diagrams (SVG, PNG, Mermaid)
└── ZMA Practical Guide.pdf # Full migration guide
```

---

## Development Setup

**Requirements:**
- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Node.js 18+](https://nodejs.org/) (only if working on the license server)
- Git

**Build and run ZMA.Tool locally:**
```shell
cd ZMA.Tool
dotnet build
dotnet run
```

**Build and run ZMA.Migrator locally:**
```shell
cd ZMA.Migrator
dotnet build
dotnet run -- --help
```

**Install a template locally for testing:**
```shell
dotnet new install ./ZMA.Small.Template --force
dotnet new zma-small -n MyApp
```

---

## Commit Message Guidelines

Use the [Conventional Commits](https://www.conventionalcommits.org/) format:

```
type: short description
```

| Type | When to use |
|---|---|
| `feat` | New feature |
| `fix` | Bug fix |
| `docs` | Documentation only |
| `refactor` | Code change that isn't a fix or feature |
| `test` | Adding or fixing tests |
| `chore` | Build process, tooling, dependency updates |

**Examples:**
```
feat: add PaymentService entities to Large template
fix: zma-migrate fails when source path contains spaces
docs: add Docker section to CONTRIBUTING.md
chore: bump EF Core to 9.0.1
```

---

## Questions?

Open a [GitHub Discussion](https://github.com/zohaib-khan-786/ZK-Hybrid-Architecture/discussions) or reach out directly at `killerzobi893@gmail.com`.

Built with ❤️ by [Zohaib Khan](https://github.com/zohaib-khan-786) in Pakistan 🇵🇰
