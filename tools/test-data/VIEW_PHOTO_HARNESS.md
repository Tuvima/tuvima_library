# View photo harness

Run the Engine in Development, then call:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:61495/dev/view-photo-harness
```

The harness downloads five attributed, free-licensed Wikimedia Commons images,
uploads them into the seed profile's managed Personal Space, and applies stable
test metadata for capture date, device, coordinates, and place name. It verifies:

- image upload and indexing;
- Places grouping from real stored coordinates;
- People grouping from reviewed, provenance-bearing fixture annotations;
- tags and searchable fixture identity;
- reversible trash and restore behavior.

Every response row includes the Commons source page, author, and license. The
stored metadata also includes those fields plus a SHA-256 hash. People fixtures
are deliberately recorded as manual reviewed test evidence; this harness does
not claim that automatic face recognition exists.

The endpoint is registered only in Development. A generated-state database wipe
removes harness records and managed fixture files while leaving external linked
sources alone.
