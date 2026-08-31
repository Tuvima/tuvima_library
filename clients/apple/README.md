# Tuvima for Apple platforms

The iOS application includes the CarPlay audio scene. Generate the Xcode project
with `xcodegen generate`, then build the `Tuvima` scheme. A CarPlay Audio managed
entitlement must be granted to the product's Apple Developer account before a
signed CarPlay build can be distributed.

The app consumes only the Dashboard `/api/v1` contract. Tokens are stored in
Keychain, downloaded files are excluded from backup, and signed HLS URLs are
never persisted.
