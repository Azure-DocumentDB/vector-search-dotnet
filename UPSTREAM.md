# Upstream provenance

- Source repository: https://github.com/Azure-Samples/documentdb-samples
- Exact source revision: [f5a2675bbf349737234d0391e3ebee24b260e06f](https://github.com/Azure-Samples/documentdb-samples/commit/f5a2675bbf349737234d0391e3ebee24b260e06f)
- Source directory: [ai/vector-search-dotnet](https://github.com/Azure-Samples/documentdb-samples/tree/f5a2675bbf349737234d0391e3ebee24b260e06f/ai/vector-search-dotnet)
- Extraction date: 2026-09-30
- License: original Microsoft MIT notice preserved verbatim in [LICENSE.md](LICENSE.md)
- History model: independently maintained source snapshot, not a GitHub fork or ownership transfer

## Standalone adaptations

Source is at repository root. The README is rewritten for that layout, and placeholder-only configuration/data setup replaces reliance on the parent checkout.
- Adapted source file: `.gitignore`
- Adapted source file: `README.md`

Hotel fixtures are not redistributed: [data/README.md](data/README.md) pins the original input locations and checksums. Saved output, Learn-only quickstart pages, and devcontainer automation are omitted. Shared infrastructure and root monorepo CI are not copied.

At initial extraction, dependency versions and vector algorithms were retained. Subsequent .NET-specific changes are recorded below; the upstream revision remains the provenance baseline, not a claim that the downstream code is unchanged.

### Configuration example added (2026-10-01)

Upstream ships no `.env.example` for the .NET samples because configuration comes from
`appsettings.json`. `Program.cs` does call `.AddEnvironmentVariables()`, so process
environment overrides are supported, and a placeholder-only `.env.example` was added
enumerating every overridable key using .NET's `Section__Key` double-underscore
separator. Keys were verified to match `appsettings.json` exactly in both directions.

.NET does not auto-load `.env` files; the file documents how to export the values. No
application code, dependency versions, or `appsettings.json` defaults were changed.

## Maintenance

There is no automatic synchronization. Review upstream changes against this pinned baseline, apply only relevant fixes, retain notices, and record the imported revision and local adaptations here.
Issue/PR history is not transferred. The maintainers of the repository hosting this extraction own its downstream fixes; contribute reusable fixes upstream under its contribution policy.

### Safe deterministic quickstart adaptation (2026-10-12)

The original Microsoft MIT notice and pinned upstream provenance remain unchanged. Downstream code now targets patched .NET 10 LTS; MongoDB.Driver/Azure.Identity and locked transitive dependencies are updated. Unused Newtonsoft/configuration/DI/logging dependencies and DTOs are removed in favor of BSON fixture documents and a small explicit environment configuration.

The original-style C# menu is retained with deterministic CLI commands, EOF handling and nonzero sanitized failures. The default workflow uses a pinned 1536D precomputed fixture query instead of billable embedding calls; both the original .NET fixture and shared upstream comparison fixture are accepted. This validates a synthetic self-match only, not semantic quality.

Explicit disposable namespace/write guards, exact Azure credential destination allowlists, verified TLS, native SCRAM and a real Entra OIDC callback replace implicit database/client setup. Stable-ID insert-only seeding preserves existing/user-modified/unrelated records; populated collections still get catalog-verified indexes. Conflicts fail without dropping or resetting data. Broad all-database index enumeration and automatic embedding-output overwrites are removed. The optional model command only validates a single explicitly requested query embedding and disables redirects; live inference remains unvalidated/blocked on S0 eligibility.

Permanent .NET unit/mocked/CLI safety tests, an explicit six-case Native+OIDC Azure integration gate, current-feed direct/transitive NuGet advisory checks, and pinned read-only GitHub Actions CI are added. No live Azure success is inferred from these code changes or local tests. Shared provisioning remains separately owned and is not copied into this repository.
