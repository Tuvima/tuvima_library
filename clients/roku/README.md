# Tuvima for Roku

This is a native SceneGraph/BrightScript client. Sideload the `clients/roku`
directory as a development application, or package it through the Roku
Developer Dashboard.

The client implements server entry, device pairing, browse rows, search-ready
API plumbing, details metadata, HLS/direct playback, captions supplied by the
manifest, heartbeats, and resume. Roku does not provide a Keychain-equivalent;
the refresh token is stored in the device registry with the minimum consumer
scopes and remains protected by server-side rotation, replay revocation, and
device revocation.

Public publication remains blocked until Roku confirms that Tuvima's
browser-approved device flow satisfies the current on-device authentication
criterion. Store artwork and localization assets must also be supplied before
certification packaging.
