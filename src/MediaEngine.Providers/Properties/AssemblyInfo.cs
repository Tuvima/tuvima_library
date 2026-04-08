using System.Runtime.CompilerServices;

// Allow the test project to access internal types and methods.
// Bucket B refactor: ReconcileAsync, ReconcileMultiLanguageAsync, ReconcileBatchAsync,
// ResolveBridgeAsync, ResolveMusicAlbumAsync are internal — public callers should use
// the new ResolveAsync / ResolveBatchAsync facade. Tests still exercise the internal
// reconciliation primitives directly to validate parity.
[assembly: InternalsVisibleTo("MediaEngine.Providers.Tests")]
