---
title: "Network and Remote Access"
summary: "How Tuvima separates desired network configuration from observed connectivity and chooses remote playback delivery."
audience: "developer"
category: "architecture"
product_area: "networking"
---

# Network and Remote Access

Tuvima treats networking as an appliance-style workflow. Administrators use
**Settings → Network & Remote Access** for a health overview, LAN settings,
remote access, network-aware streaming, and advanced diagnostics. A new install
uses the same panels in `/setup/network`; there is no second onboarding model.

## Source of truth

Desired state lives in `config/network.json` and is validated against
`config/schemas/network.schema.json`. It includes the Dashboard port, binding,
local discovery, remote mode, router automation, custom HTTPS address, trusted
proxies, and remote-streaming policy.

Observed state stays in memory: usable interfaces, current Tuvima-owned router
mapping, public address, latest tests, measured bandwidth, and provider state.
The API never writes those observations into configuration.

## Connectivity lifecycle

- Local discovery announces `_tuvima._tcp.local` when enabled.
- Automatic router setup tries PCP, NAT-PMP, then UPnP IGD.
- Lease mappings are renewed every 15 minutes and after a safe port change.
- Disabling remote access or a clean Engine shutdown removes only the mapping
  that Tuvima created.
- A failed automatic setup produces a dynamic manual TCP forwarding rule.
- CGNAT evidence changes the guidance instead of repeatedly recommending a
  router rule that is unlikely to work.

External reachability and upload measurements remain **unknown** when no trusted
external measurement target exists. The UI does not substitute sample success,
public-IP, or bandwidth values.

## Playback context

Playback manifests and heartbeats carry connection path, provider, approximate
bandwidth, latency, device, profile, and optional room identity. These facts are
used only for delivery selection and diagnostics—not authentication.

For remote playback, the existing playback manifest can prefer its existing HLS
delivery path when source resolution or calculated bitrate exceeds the configured
quality/bandwidth budget. Local compatible clients retain direct delivery. This
does not introduce a second transcoding engine.

## Security boundary

Every network settings and diagnostics endpoint requires the Administrator role.
Reachability never grants authentication. Secure-provider credentials are not
part of the public contracts. Forwarded host, scheme, and client-address headers
are accepted only from exact proxy IP addresses in `trusted_proxies`, with one
forwarding hop allowed.
