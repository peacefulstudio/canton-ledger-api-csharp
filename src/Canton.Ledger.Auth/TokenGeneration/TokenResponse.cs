// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using System.Text.Json.Serialization;

namespace Canton.Ledger.Auth.TokenGeneration;

internal sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("token_type")] string? TokenType);
