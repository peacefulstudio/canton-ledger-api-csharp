# Canton.Ledger.Auth

OAuth2 client-credentials token provider for Canton participant nodes.

Provides `ITokenProvider` with two implementations:
- **`StaticTokenProvider`** -- wraps a fixed token string
- **`ClientCredentialsProvider`** -- OAuth2 client-credentials flow with TTL-based caching
