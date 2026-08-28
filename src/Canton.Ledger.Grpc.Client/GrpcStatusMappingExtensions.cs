// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;
using Grpc.Core;

namespace Canton.Ledger.Grpc.Client;

/// <summary>
/// gRPC side of the canonical <see cref="DamlErrorCategory"/> status mapping, plus typed
/// accessors for the untyped <c>int</c> transport status that
/// <see cref="ExerciseOutcome{T}.InfraError"/> and <see cref="LedgerOperationException"/>
/// carry. The HTTP side of the same table lives in <c>Canton.Ledger.Abstractions</c> as
/// <c>ToHttpStatusCode()</c>; this half needs the <see cref="StatusCode"/> enum and so
/// ships with the gRPC client.
/// </summary>
public static class GrpcStatusMappingExtensions
{
    /// <summary>
    /// Maps the category to the gRPC status the participant returns it under.
    /// <see cref="DamlErrorCategory.Unknown"/> maps to <see cref="StatusCode.Unknown"/>;
    /// <see cref="DamlErrorCategory.MaliciousOrFaultyBehaviour"/> also maps to
    /// <see cref="StatusCode.Unknown"/> (Canton deliberately obscures the cause);
    /// <see cref="DamlErrorCategory.BackgroundProcessDegradationWarning"/> is a log-only
    /// category never expected on a request path and maps to
    /// <see cref="StatusCode.Internal"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is not a defined <see cref="DamlErrorCategory"/>.
    /// </exception>
    public static StatusCode ToGrpcStatusCode(this DamlErrorCategory category) =>
        category switch
        {
            DamlErrorCategory.Unknown => StatusCode.Unknown,
            DamlErrorCategory.TransientServerFailure => StatusCode.Unavailable,
            DamlErrorCategory.ContentionOnSharedResources => StatusCode.Aborted,
            DamlErrorCategory.DeadlineExceededRequestStateUnknown => StatusCode.DeadlineExceeded,
            DamlErrorCategory.SystemInternalAssumptionViolated => StatusCode.Internal,
            DamlErrorCategory.MaliciousOrFaultyBehaviour => StatusCode.Unknown,
            DamlErrorCategory.AuthInterceptorInvalidAuthenticationCredentials => StatusCode.Unauthenticated,
            DamlErrorCategory.AuthorizationChecksFailed => StatusCode.PermissionDenied,
            DamlErrorCategory.InvalidIndependentOfSystemState => StatusCode.InvalidArgument,
            DamlErrorCategory.InvalidGivenCurrentSystemStateOther => StatusCode.FailedPrecondition,
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists => StatusCode.AlreadyExists,
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing => StatusCode.NotFound,
            DamlErrorCategory.InvalidGivenCurrentSystemStateSeekDifferentResource => StatusCode.FailedPrecondition,
            DamlErrorCategory.BackgroundProcessDegradationWarning => StatusCode.Internal,
            DamlErrorCategory.InternalUnsupportedOperation => StatusCode.Unimplemented,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };

    /// <summary>
    /// Returns <see cref="ExerciseOutcome{T}.InfraError.StatusCode"/> as the typed
    /// <see cref="StatusCode"/> it was recorded from, replacing the
    /// <c>(StatusCode)infraError.StatusCode</c> cast every consumer wrote. Only
    /// meaningful for outcomes produced by the gRPC transport — the REST client records
    /// HTTP status codes in the same field.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The stored value is not a defined <see cref="StatusCode"/> — the outcome did not
    /// come from the gRPC transport.
    /// </exception>
    public static StatusCode AsGrpcStatusCode<T>(this ExerciseOutcome<T>.InfraError infraError)
    {
        ArgumentNullException.ThrowIfNull(infraError);
        return AsDefinedStatusCode(infraError.StatusCode);
    }

    /// <summary>
    /// Returns <see cref="LedgerOperationException.StatusCode"/> as the typed
    /// <see cref="StatusCode"/> it was recorded from, or <c>null</c> when the exception
    /// carries no transport status (it did not wrap an
    /// <see cref="ExerciseOutcome{T}.InfraError"/> outcome). Only meaningful for
    /// exceptions produced by the gRPC transport — the REST client records HTTP status
    /// codes in the same field.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The stored value is not a defined <see cref="StatusCode"/> — the exception did not
    /// come from the gRPC transport.
    /// </exception>
    public static StatusCode? AsGrpcStatusCode(this LedgerOperationException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.StatusCode is { } statusCode ? AsDefinedStatusCode(statusCode) : null;
    }

    private static StatusCode AsDefinedStatusCode(int statusCode) =>
        Enum.IsDefined((StatusCode)statusCode)
            ? (StatusCode)statusCode
            : throw new InvalidOperationException(
                $"Transport status {statusCode} is not a gRPC status code; " +
                "only outcomes produced by the gRPC transport carry one.");
}
