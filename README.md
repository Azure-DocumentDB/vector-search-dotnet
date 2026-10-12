# vector-search-dotnet

A **C# / .NET 10** Azure DocumentDB quickstart: explicitly seed a disposable namespace, ensure IVF/HNSW/DiskANN indexes, and verify a top-5 **precomputed fixture self-match**. The default path makes **no model calls**. It is a functional smoke test, **not a semantic-quality, performance, or recall benchmark**.

Standalone downstream adaptation of the [pinned Microsoft sample](UPSTREAM.md), with the original [MIT license](LICENSE.md) preserved.

## Safety and prerequisites

- [.NET 10 LTS SDK](https://dotnet.microsoft.com/download/dotnet/10.0), **10.0.401 or a newer patch in that feature band** (`global.json`), Python 3 for the explicit data/audit helpers, and Bash for commands below.
- An **existing, separately approved disposable Azure DocumentDB cluster**, with TLS network access and permissions to insert documents, list/create indexes, and query the chosen database. For all three algorithms, use a tier/region that supports DiskANN; the live gate, not a successful build, proves availability. No provisioning is performed here.
- Native SCRAM-SHA-256 credentials, or a registered/authorized Microsoft Entra database principal. `az login` supplies a credential but **does not grant database access**.
- Review the namespace before opting in. Database names must start `dotnet_`, `sample_`, or `vectorsearch_disposable_`; only lowercase ASCII letters, digits and underscores, max 63 characters. Collection prefixes are explicitly supplied lowercase ASCII letters/digits/underscores, start with a letter, max 48 characters.

**Mutations:** insert missing fixture `_id` values and create missing vector indexes, only after `ALLOW_DATABASE_WRITES=true`. Existing documents (including user edits and unrelated records) are never updated, replaced or deleted. Repeated runs fill missing fixture IDs and still verify indexes in populated collections. Index conflicts fail closed; there is **no drop/reset/cleanup command**. A failed run can leave partial inserts or an index. Review or use a new disposable prefix; cleanup remains the operator's separate responsibility.

Strict TLS/certificate verification cannot be disabled. Both the Azure suffix and explicit exact host allowlist are checked **before credential creation**. Connection URIs must not contain passwords/userinfo. No arbitrary Mongo servers or Azure OpenAI endpoints are accepted. The optional model transport refuses redirects.

## One clean path: precomputed vectors with native authentication

From the repository root:

### 1. Restore, build, and run local tests (no database access)

```bash
dotnet restore tests/DocumentDBVectorSearch.Tests.csproj --locked-mode --no-http-cache
dotnet build tests/DocumentDBVectorSearch.Tests.csproj --no-restore --configuration Release
dotnet test tests/DocumentDBVectorSearch.Tests.csproj --no-restore --no-build --configuration Release --filter 'Category!=Live'
python3 -m unittest discover -s tools -p 'test_*.py'
python3 tools/check-vulnerabilities.py
```

The NuGet check queries the current public advisory feed, includes transitive dependencies, and fails on findings or incomplete/failed queries. CI uses the same read-only checks, pinned GitHub-owned actions, **no database secrets and no cloud deployment**.

### 2. Explicitly download and verify the fixture

```bash
python3 tools/prepare-data.py
dotnet run --project DocumentDBVectorSearch.csproj --configuration Release --no-build -- validate-fixture
```

This downloads the pinned **50-record, 1536-dimensional** hotel fixture, verifies SHA-256, and extracts the **first record's vector** to `data/query-vector.json`. It refuses to overwrite either file. See [data/README.md](data/README.md) for provenance, accepted legacy fixture, and offline reuse. Nothing is downloaded on clone/build/startup.

### 3. Set the disposable destination and native credentials

Replace the example host/database/prefix deliberately. Do not paste real passwords into shell history or Git.

```bash
export MONGO_URI='mongodb+srv://YOUR-RESOURCE.global.mongocluster.cosmos.azure.com/?tls=true&retrywrites=false'
export MONGO_ALLOWED_HOST='YOUR-RESOURCE.global.mongocluster.cosmos.azure.com'
export MONGO_DATABASE='dotnet_disposable_example'
export MONGO_COLLECTION_PREFIX='native'
export MONGO_AUTH_MODE='scram'
read -r -p 'Native username: ' MONGO_USERNAME; export MONGO_USERNAME
read -r -s -p 'Native password: ' MONGO_PASSWORD; printf '\n'; export MONGO_PASSWORD
export VECTOR_DATA_FILE="$PWD/data/Hotels_Vector.json"
export QUERY_VECTOR_FILE="$PWD/data/query-vector.json"
export ALLOW_DATABASE_WRITES='true'
```

`MONGO_ALLOWED_HOST` must equal the URI host. Only `tls=true`, `retrywrites=false`, and `authSource=admin` URI options are accepted; all other options, userinfo, non-SRV URIs, paths, custom ports and TLS-bypass parameters are rejected. SCRAM uses `admin` and **SCRAM-SHA-256** explicitly. The URI is never printed.

The [`.env.example`](.env.example) is optional reference. **Neither `.env` nor `appsettings.json` is loaded.** Export process variables explicitly. Legacy `MongoDB__...` / `VectorSearch__...` settings are no longer read, avoiding silent namespace fallback; there is no default database or collection prefix.

### 4. Run the three algorithms

```bash
dotnet run --project DocumentDBVectorSearch.csproj --configuration Release --no-build -- all
# Or one at a time:
dotnet run --project DocumentDBVectorSearch.csproj --configuration Release --no-build -- ivf
dotnet run --project DocumentDBVectorSearch.csproj --configuration Release --no-build -- hnsw
dotnet run --project DocumentDBVectorSearch.csproj --configuration Release --no-build -- diskann
```

Collections are `${MONGO_COLLECTION_PREFIX}_ivf`, `_hnsw`, `_diskann`. Each JSON result reports inserts/preserved fixture IDs, verified index, five results, and first ID `dotnet-hotel-1`. The program checks catalog definition and first-record self-match; it exits **1** on any failure, including unsupported algorithms, insertion errors, mismatched indexes or unexpected search results. No example output here is evidence of a live Azure run.

Re-run to test idempotence (zero inserted when all fixture IDs exist). Existing edited vectors are preserved too; if an edit breaks the self-match, the verification fails rather than overwriting it.

```bash
# Reads only the three configured collections; does not enumerate other databases.
dotnet run --project DocumentDBVectorSearch.csproj --configuration Release --no-build -- show-indexes
# No arguments opens the original-style menu. EOF or 0 exits cleanly.
dotnet run --project DocumentDBVectorSearch.csproj --configuration Release --no-build
```

Errors print a safe category, not exception text, tokens, URI, server replies, or stack traces. Each algorithm gets an independent deadline of 300 seconds by default (`OPERATION_TIMEOUT_SECONDS`, range 30–600); fixture loading has the same bounded preparation budget. `all` attempts all three algorithms even after first/middle failures, emits one JSON result row per algorithm (sanitized `Status=FAIL` rows on errors), and exits 1 if any fail. Thus an `all` run may take up to three case deadlines plus preparation; connection/server selection/socket/query deadlines and index readiness retries are also bounded.

## Microsoft Entra OIDC alternative

Keep the same credential-free URI/allowed host and approved database. Register and authorize the intended Entra principal in DocumentDB first, then:

```bash
az login
unset MONGO_PASSWORD MONGO_USERNAME
export MONGO_AUTH_MODE='oidc'
export MONGO_COLLECTION_PREFIX='oidc'
dotnet run --project DocumentDBVectorSearch.csproj --configuration Release --no-build -- all
```

The MongoDB driver's **real OIDC callback** calls Azure.Identity `DefaultAzureCredential` with `https://ossrdbms-aad.database.windows.net/.default` and returns the token's actual remaining lifetime (rejecting expired/near-expiry tokens). OIDC uses `$external`; the token principal is used, not a URI username. Standard Azure.Identity environment/managed-identity credentials are supported; interactive browser login is not opened by this program. Native credentials are not a fallback for OIDC.

## Explicit live integration gate — Native + Entra × all three algorithms

**Operator-run only, against approved paid resources; never CI.** Export the destination/fixture/write variables above and native username/password. Also provide a usable, authorized Entra credential. Choose a separate short collection prefix such as `gate`:

```bash
export MONGO_COLLECTION_PREFIX='gate'
export RUN_LIVE_INTEGRATION='true'
export LIVE_AUTH_MODES='scram,oidc'
dotnet test tests/DocumentDBVectorSearch.Tests.csproj --configuration Release --no-restore --filter 'Category=Live'
```

Six required cases use `gate_scram_{ivf,hnsw,diskann}` and `gate_oidc_{ivf,hnsw,diskann}` in the single configured database. The gate isolates native passwords from OIDC. Each case checks the catalog, top five/first fixture ID, repeat-run insert/count stability, and preservation of a preexisting fixture-record canary and unrelated document. It seeds canaries **only when absent**, so each fresh collection has 51 documents. It never updates/deletes/drops. Resources/data remain for operator review.

**Selecting live tests without their opt-in, required environment, or complete `scram,oidc` matrix fails (not skipped).** Ordinary local tests explicitly exclude `Category=Live`; a plain unfiltered run intentionally fails closed when not configured. Unit/mocked tests do not prove Azure compatibility. Publication/live readiness requires this entire gate to pass on the reviewed commit.

## Optional Azure OpenAI query embedding — unvalidated

This is **not required by the quickstart**. Live model validation is blocked on **Azure OpenAI S0 resource/deployment eligibility** in the validation environment; no real model inference is claimed. Resolve eligibility, deployment access and cost approval separately.

The `embed-query` command requires **all** of `ALLOW_MODEL_CALLS=true`, `AZURE_OPENAI_ENDPOINT=https://RESOURCE.openai.azure.com/`, exact `AZURE_OPENAI_ALLOWED_HOST`, `AZURE_OPENAI_EMBEDDING_DEPLOYMENT`, `QUERY_TEXT`, and an authorized Azure credential. It generates and dimension-checks one query embedding only; it **does not persist it, overwrite fixture files, or run a semantic search**. It is bounded by a 60-second total deadline, uses strict HTTPS, and never follows redirects. Neither the local tests nor the fixture live gate validate this optional service path. Legacy automatic document-embedding/file-overwrite behavior has been removed.

## Maintenance and license

See [UPSTREAM.md](UPSTREAM.md) for the exact upstream revision and downstream adaptations. This is independently maintained, not automatically synced. Review dependency advisories and supported SDK patches before live runs. The original Microsoft [MIT notice](LICENSE.md) is unchanged; fixture source/terms are linked in the data guide. No infrastructure templates or provisioning credentials are included.
