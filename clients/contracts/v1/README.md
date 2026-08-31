# Native client API v1 conformance

Native clients consume only the Dashboard origin and the public `/api/v1`
surface. The executable fixtures live in `tests/fixtures/native-client-v1` and
are validated against `MediaEngine.Contracts` by
`NativeClientContractFixtureTests`.

Client rules:

- discover the server through `/.well-known/tuvima` or an explicitly entered
  Dashboard HTTPS address;
- never connect to the Engine port or receive the Dashboard service key;
- ignore additive JSON fields that the client does not understand;
- treat bearer tokens, refresh tokens, and signed HLS URLs as secrets;
- follow `recommendedDelivery` from the playback manifest;
- bind all progress and queue mutations to the profile and device claims in the
  access token;
- use `OfflineVariantDto.downloadUrl`, never a server filesystem path;
- re-pair to switch profiles until a separately approved public profile-switch
  contract exists.

Changing an existing v1 field name, type, meaning, route, or required status is
a breaking change and requires a new major API path. New optional fields are
allowed when all clients remain tolerant readers.
