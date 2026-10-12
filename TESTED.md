# Validation record

## Real managed Azure validation — 2026-10-12

Tested on one disposable Azure DocumentDB M30 in West US 2, one shard, MongoDB 8.0 compatibility, 128 GiB storage and HA disabled. Native SCRAM-SHA-256 and Microsoft Entra OIDC were exercised separately with verified TLS and a client-IP firewall. No OpenAI/model calls were made.

Fixture: 50 public pinned records with 1,536-dimensional vectors; the query is the first stored vector. This is a numerical vector/database contract test, not semantic relevance, performance, production-readiness or a tier/version support matrix. No real credentials or infrastructure identifiers are published.

IVF, HNSW and DiskANN passed under SCRAM and OIDC: **6/6 live cases**. Each checked the actual index catalog, five-result fixture self-match, an idempotent rerun, an edited fixture-record canary and an unrelated document, with at least 51 documents retained. Actual `all` CLI commands also passed under both auth modes.

Wrong password, disabled write opt-in, unsafe database name and foreign host each returned nonzero without credential leakage. The final all-case continuation/aggregate-failure fix was included. Local Release build has zero warnings/errors; 109 permanent tests and four audit-helper tests pass; fresh NuGet scan reports no vulnerable direct/transitive packages. Tools: .NET SDK 10.0.401/runtime 10.0.12.

The optional Azure OpenAI command remains **not live-validated** because the available subscription was ineligible for OpenAI S0. It is not required for this precomputed quickstart.

Tested implementation: `78d3e97eeccd91f976302f61d68d37a96404afc5`.

## Scope and cleanup

These tests used only separately approved synthetic namespaces on a disposable cluster. The sample never automatically deletes databases, collections or indexes. Resource cleanup is an explicit environment-owner responsibility. Publication is conditioned on verifying that the test resource group is gone, hosted CI passes, and an anonymous clean checkout works. MIT licensing and pinned upstream data provenance are retained.
