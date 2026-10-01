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

No dependency versions or vector/agent algorithms are intentionally changed. The Go agent module namespace and local data/environment-file paths are adjusted where necessary. Invalid monorepo-only npm entry points are removed rather than advertised as working commands.

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
