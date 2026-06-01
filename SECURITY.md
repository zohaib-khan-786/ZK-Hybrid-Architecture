# Security Policy

## Supported Versions

The following versions of ZMA packages currently receive security updates:

| Package | Version | Supported |
|---|---|---|
| ZMA.Tool | 1.0.x (latest) | ✅ |
| ZMA.Migrator | 1.0.x (latest) | ✅ |
| ZMA.Licensing | 1.0.x (latest) | ✅ |
| ZMA.Small.Template | 1.0.x (latest) | ✅ |
| ZMA.Medium.Template | 1.0.x (latest) | ✅ |
| ZMA.Large.Template | 1.0.x (latest) | ✅ |
| Older versions | < 1.0.0 | ❌ |

Always use the latest published version on [NuGet](https://www.nuget.org/profiles/ZohaibKhan).

---

## Reporting a Vulnerability

**Please do NOT report security vulnerabilities through public GitHub Issues.**

If you discover a security vulnerability in ZMA, please report it responsibly by emailing:

📧 **killerzobi893@gmail.com**

Include the word **[SECURITY]** in the subject line so it gets prioritized.

---

## What to Include in Your Report

To help us triage and fix the issue quickly, please include:

- **Description** — what the vulnerability is and what it affects
- **Affected package(s)** — which ZMA package(s) are impacted
- **Steps to reproduce** — exact commands or code that triggers the issue
- **Impact** — what an attacker could do if they exploited this
- **Your environment** — OS, .NET version, ZMA tool version
- **Suggested fix** (optional) — if you have one

---

## What Happens After You Report

| Timeline | Action |
|---|---|
| Within 48 hours | Acknowledgement of your report |
| Within 7 days | Initial assessment and severity classification |
| Within 30 days | Fix released or mitigation guidance provided |
| After fix | Public disclosure coordinated with reporter |

We follow responsible disclosure — we'll keep you informed throughout the process and credit you in the release notes unless you prefer to remain anonymous.

---

## Scope

### In Scope

- **ZMA.Tool** — CLI scaffolding tool
- **ZMA.Migrator** — architecture migration CLI
- **ZMA.Licensing** — license validation library
- **ZMA templates** — scaffolded project security defaults
- **License server** — authentication and license key validation endpoints

### Out of Scope

- Vulnerabilities in generated project code that are introduced by the developer after scaffolding
- Issues in third-party dependencies (report directly to their maintainers)
- Social engineering attacks
- Denial of service on the public license server without prior coordination

---

## Security Best Practices for ZMA Users

If you are using ZMA templates in production, please ensure:

**Authentication**
- Replace `UseInMemoryDatabase` with a real database before going to production
- Rotate JWT signing keys and store them in environment variables, not `appsettings.json`
- Set appropriate JWT expiry times for your use case

**Secrets Management**
- Never commit connection strings or API keys to source control
- Use environment variables or a secrets manager (Azure Key Vault, AWS Secrets Manager)
- The `.gitignore` in ZMA templates excludes `appsettings.Development.json` — use it for local secrets

**Dependencies**
- Run `dotnet list package --vulnerable` periodically to check for known CVEs
- Keep EF Core, ASP.NET Core, and other dependencies up to date

**Large Tier (Microservices)**
- Secure inter-service communication with mTLS or API Gateway authentication
- Validate all integration events before processing — never trust event payloads blindly
- Implement idempotency keys on all event consumers

---

## Known Security Considerations

**ZMA.Licensing machine fingerprinting**  
ZMA.Licensing generates a machine fingerprint using system identifiers to bind licenses to machines. This data is hashed with SHA256 and never stored in plain text. No personally identifiable information is transmitted to the license server.

**License server**  
The license server is hosted on Render. License validation requests contain only the license key and machine fingerprint hash. No source code or project data is ever sent to the server.

---

## Acknowledgements

We thank the security researchers and developers who responsibly disclose vulnerabilities. Contributors will be credited in release notes with their permission.

---

Built with ❤️ by [Zohaib Khan](https://github.com/zohaib-khan-786) in Pakistan 🇵🇰
