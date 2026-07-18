# Daml.Runtime.Grpc

Bidirectional conversion between `Daml.Runtime` value types and Canton Ledger API v2 protobuf messages.

## Overview

This package bridges the transport-neutral `Daml.Runtime` type system and the gRPC wire format defined by `Canton.Ledger.Grpc`. It is consumed internally by `Canton.Ledger.Grpc.Client` to map Daml values, records, variants, identifiers, and other types to and from their protobuf equivalents — you rarely need to reference it directly.

For command submission, contract queries, and subscription streams, use the higher-level `Canton.Ledger.Grpc.Client` package instead. It depends on `Daml.Runtime.Grpc` and handles all wire conversion automatically.

## Key Types

| Type | Purpose |
|------|---------|
| `DamlValueConverter` | Static class providing `ToProtoValue`/`FromProtoValue`, `ToProtoRecord`/`FromProtoRecord`, and `ToProtoIdentifier`/`FromProtoIdentifier` conversions |

## Related Packages

- `Canton.Ledger.Grpc.Client` — High-level gRPC client that consumes this converter; prefer it for application code
- `Canton.Ledger.Grpc` — Generated gRPC stubs providing the proto message types
- `Daml.Runtime` — Runtime types for generated Daml contracts
