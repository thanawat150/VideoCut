# Mobile Control security model

- The server binds to a local TCP port and does not require administrator URL registration.
- A random access token is generated for each server session.
- API endpoints require a constant-time token comparison.
- Uploads are limited to MP4, use versioned names, are capped by size, and are imported through FFprobe before use.
- Job control paths are constrained beneath the active project root.
- The server does not expose arbitrary filesystem browsing.
- Closing the server cancels pending approval requests.
- The feature is designed for a trusted local network. It should not be port-forwarded to the public internet.
