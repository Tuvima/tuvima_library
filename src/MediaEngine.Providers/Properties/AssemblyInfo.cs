using System.Runtime.CompilerServices;

// Allow the test project to access internal types and methods.
// ReconcileAsync, ReconcileBatchAsync, and ReconcileMultiLanguageAsync on
// ReconciliationAdapter are internal — public callers should use the
// ResolveAsync / ResolveBatchAsync Wikidata identity facade. Tests still exercise the
// internal reconciliation primitives directly to validate parity between the
// manual search path and the automated pipeline path. The legacy
// ResolveBridgeAsync / ResolveMusicAlbumAsync / ResolveByTextAsync helpers
// referenced here in earlier commits were removed in the adapter slimdown
// remediation (Commit F2) and Wikidata identity resolution is now fully delegated
// to Tuvima.Wikidata.BridgeResolutionService.
[assembly: InternalsVisibleTo("MediaEngine.Providers.Tests")]
