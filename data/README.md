# Pinned hotel fixture

The dataset is **not redistributed**. From repository root, explicitly run:

```bash
python3 tools/prepare-data.py
```

This retrieves the upstream shared vector fixture, verifies SHA-256, checks 50 × 1536 shape, and writes the first record's vector to `data/query-vector.json`. Both outputs are git-ignored and created exclusively (no overwrites). No database connection or model invocation occurs.

| Accepted fixture | Pinned source | SHA-256 |
| --- | --- | --- |
| Shared fixture (quickstart) | [ai/data/Hotels_Vector.json](https://github.com/Azure-Samples/documentdb-samples/blob/f5a2675bbf349737234d0391e3ebee24b260e06f/ai/data/Hotels_Vector.json) | `60c08c2cdd439444ba8601d94a1f3487f4a9ee56cd48ba201d983c96722de6e4` |
| Original .NET fixture (also accepted) | [ai/vector-search-dotnet/data/Hotels_Vector.json](https://github.com/Azure-Samples/documentdb-samples/blob/f5a2675bbf349737234d0391e3ebee24b260e06f/ai/vector-search-dotnet/data/Hotels_Vector.json) | `68ca2a0f2a57e095f5c41853301b834779fce30ee7a9eb0aa9b156092945443f` |

For an existing verified download, leave it unchanged and set `VECTOR_DATA_FILE` to its absolute path. Set `QUERY_VECTOR_FILE` to a JSON array containing that same fixture's first `DescriptionVector`; use a new file, not an overwrite. Runtime validates the dataset hash and that the query equals its first record's vector. A query from a different fixture or regenerated embeddings is intentionally rejected by this deterministic smoke mode.

The `_id` generated at load time is `dotnet-hotel-` plus the fixture's unique string `HotelId`. These are the only identities used for insert idempotence; unrelated records with different `_id` are left alone. Stored vector elements are BSON doubles. No model is called to prepare the query, and the resulting self-match says nothing about semantic search quality.

[Upstream data notes](https://github.com/Azure-Samples/documentdb-samples/blob/f5a2675bbf349737234d0391e3ebee24b260e06f/ai/data/README.md) and [license](https://github.com/Azure-Samples/documentdb-samples/blob/f5a2675bbf349737234d0391e3ebee24b260e06f/LICENSE.md) remain authoritative. Hashes identify inputs, not a separate dataset-rights grant. Retain applicable upstream notices.
