// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Google.Protobuf.Reflection;

namespace Canton.Ledger.Grpc.Client;

internal readonly record struct ServerCall(ServiceDescriptor Descriptor, string Method);
