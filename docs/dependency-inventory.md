# V1 dependency and license inventory

Inventory date: 2026-08-31. Versions are the centrally pinned direct package versions in `Directory.Packages.props`. License expressions were read from the locally restored NuGet package metadata. The self-contained publish also contains the Microsoft .NET 10 Windows runtime and transitive dependencies described by `obj/project.assets.json`; this file is a release aid, not a substitute for legal review.

| Package | Version | Use | License |
|---|---:|---|---|
| Microsoft.AspNetCore.Authentication.Negotiate | 10.0.11 | Owner-only local UI authentication | MIT |
| Microsoft.EntityFrameworkCore.Design | 10.0.11 | Migration tooling | MIT |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.11 | Central and Founder Scout SQLite persistence | MIT |
| Microsoft.Extensions.Http | 10.0.11 | Provider HTTP clients | MIT |
| Microsoft.Playwright | 1.62.0 | Founder Scout browser automation | MIT |
| OpenAI | 2.13.0 | OpenAI/Azure structured evaluation adapter | MIT |
| Serilog | 4.4.0 | Structured logging | Apache-2.0 |
| Serilog.Sinks.File | 7.0.0 | Rolling local log files | Apache-2.0 |
| System.Security.Cryptography.ProtectedData | 10.0.11 | Windows DPAPI secret storage | MIT |
| Microsoft.NET.Test.Sdk | 17.14.0 | Test host; not published | MIT |
| NUnit | 4.3.2 | Tests; not published | MIT |
| NUnit3TestAdapter | 5.0.0 | Test discovery; not published | MIT |
| NUnit.Analyzers | 4.7.0 | Test analysis; not published | MIT |
| Moq | 4.20.72 | Test doubles; not published | BSD-3-Clause |
| coverlet.collector | 6.0.4 | Coverage collection; not published | MIT |

## Release review

- `dotnet list HomeBusinessAssistant.sln package --vulnerable --include-transitive`: no known vulnerable packages for all 25 projects.
- `dotnet list HomeBusinessAssistant.sln package --deprecated --include-transitive`: production projects had none. Test projects inherit deprecated `Microsoft.ApplicationInsights 2.22.0` from the test toolchain; it is not in the product publish.
- `dotnet list HomeBusinessAssistant.sln package --outdated --include-transitive`: reported newer major test tools and newer transitive packages. No major upgrade was introduced into the release candidate without a compatibility cycle; the vulnerability result is clean.

The installed Playwright browser is an external prerequisite in the current user's cache and is not embedded in the release archive. Its own notices are distributed with that browser installation.
