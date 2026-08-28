// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using AwesomeAssertions;
using Refit;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public class RestEndpointCoverageTests
{
    private const string RawNamespace = "Canton.Ledger.Rest.Client.Raw";
    private const string PathsKey = "paths:";
    private const string OperationIdKey = "operationId";
    private const int PathIndent = 4;
    private const int MethodIndent = 8;
    private const int OperationIdIndent = 12;

    private const string SpecCoverageHint =
        "spec/openapi.yaml is re-derived from the pinned protos and spec/patches/ by "
        + "scripts/regen-rest-client.sh, and rest-drift only proves that derivation is reproducible - "
        + "a google.api.http annotation that silently stopped being applied regenerates cleanly and "
        + "still matches itself. If this coverage change is deliberate, update PinnedSpecOperations "
        + "and the counts in src/Canton.Ledger.Rest/README.md in the same commit";

    private const string RoutingHint =
        "Generated/CantonLedgerApi.g.cs is regenerated from spec/openapi.yaml by Refitter, so every "
        + "spec operation must reach the raw surface as exactly one Refit route on the interface and "
        + "method its operationId names, and the only extra endpoints allowed are the hand-authored "
        + "off-spec ones in PinnedOffSpecEndpoints. Endpoints are qualified by declaring interface "
        + "because the off-spec IDarApi, IPackageApi and IInteractiveSubmissionApi deliberately "
        + "re-declare routes the generated "
        + "surface also carries, and by "
        + "declaring method because two methods sharing one route on one interface would otherwise "
        + "collapse into a single entry and stand in for each other. The cost of the method qualifier "
        + "is that an upstream operationId rename that changes no route fails here too, and is "
        + "answered by renaming the PinnedSpecOperations key";

    private static readonly HashSet<string> PathItemVerbs = new(StringComparer.Ordinal)
    {
        "delete", "get", "head", "options", "patch", "post", "put", "trace",
    };

    private static readonly SortedDictionary<string, string> PinnedSpecOperations = new(StringComparer.Ordinal)
    {
        ["CommandCompletionService_CompletionStream"] = "POST /v2/commands/completions",
        ["CommandService_SubmitAndWait"] = "POST /v2/commands/submit-and-wait",
        ["CommandService_SubmitAndWaitForReassignment"] = "POST /v2/commands/submit-and-wait-for-reassignment",
        ["CommandService_SubmitAndWaitForTransaction"] = "POST /v2/commands/submit-and-wait-for-transaction",
        ["CommandSubmissionService_Submit"] = "POST /v2/commands/async/submit",
        ["CommandSubmissionService_SubmitReassignment"] = "POST /v2/commands/async/submit-reassignment",
        ["ContractService_GetContract"] = "POST /v2/contracts/contract-by-id",
        ["EventQueryService_GetEventsByContractId"] = "POST /v2/events/events-by-contract-id",
        ["IdentityProviderConfigService_CreateIdentityProviderConfig"] = "POST /v2/idps",
        ["IdentityProviderConfigService_DeleteIdentityProviderConfig"] = "DELETE /v2/idps/{identityProviderId}",
        ["IdentityProviderConfigService_GetIdentityProviderConfig"] = "GET /v2/idps/{identityProviderId}",
        ["IdentityProviderConfigService_ListIdentityProviderConfigs"] = "GET /v2/idps",
        ["IdentityProviderConfigService_UpdateIdentityProviderConfig"] = "PATCH /v2/idps/{identity_provider_config.identity_provider_id}",
        ["InteractiveSubmissionService_ExecuteSubmission"] = "POST /v2/interactive-submission/execute",
        ["InteractiveSubmissionService_ExecuteSubmissionAndWait"] = "POST /v2/interactive-submission/executeAndWait",
        ["InteractiveSubmissionService_ExecuteSubmissionAndWaitForTransaction"] = "POST /v2/interactive-submission/executeAndWaitForTransaction",
        ["InteractiveSubmissionService_GetPreferredPackages"] = "POST /v2/interactive-submission/preferred-packages",
        ["InteractiveSubmissionService_GetPreferredPackageVersion"] = "GET /v2/interactive-submission/preferred-package-version",
        ["InteractiveSubmissionService_PrepareSubmission"] = "POST /v2/interactive-submission/prepare",
        ["PackageManagementService_UpdateVettedPackages"] = "POST /v2/package-vetting/update",
        ["PackageManagementService_UploadDarFile"] = "POST /v2/dars",
        ["PackageManagementService_ValidateDarFile"] = "POST /v2/dars/validate",
        ["PackageService_GetPackage"] = "GET /v2/packages/{packageId}",
        ["PackageService_GetPackageStatus"] = "GET /v2/packages/{packageId}/status",
        ["PackageService_ListPackages"] = "GET /v2/packages",
        ["PackageService_ListVettedPackages"] = "POST /v2/package-vetting/list",
        ["PartyManagementService_AllocateExternalParty"] = "POST /v2/parties/external/allocate",
        ["PartyManagementService_AllocateParty"] = "POST /v2/parties",
        ["PartyManagementService_GenerateExternalPartyTopology"] = "POST /v2/parties/external/generate-topology",
        ["PartyManagementService_GetParticipantId"] = "GET /v2/parties/participant-id",
        ["PartyManagementService_GetParties"] = "GET /v2/parties/{parties}",
        ["PartyManagementService_ListKnownParties"] = "GET /v2/parties",
        ["PartyManagementService_UpdatePartyDetails"] = "PATCH /v2/parties/{party_details.party}",
        ["StateService_GetActiveContracts"] = "POST /v2/state/active-contracts",
        ["StateService_GetActiveContractsPage"] = "POST /v2/state/active-contracts-page",
        ["StateService_GetConnectedSynchronizers"] = "GET /v2/state/connected-synchronizers",
        ["StateService_GetLatestPrunedOffsets"] = "GET /v2/state/latest-pruned-offsets",
        ["StateService_GetLedgerEnd"] = "GET /v2/state/ledger-end",
        ["UpdateService_GetUpdateById"] = "POST /v2/updates/update-by-id",
        ["UpdateService_GetUpdateByOffset"] = "POST /v2/updates/update-by-offset",
        ["UpdateService_GetUpdates"] = "POST /v2/updates",
        ["UpdateService_GetUpdatesPage"] = "POST /v2/updates/get-updates-page",
        ["UserManagementService_CreateUser"] = "POST /v2/users",
        ["UserManagementService_DeleteUser"] = "DELETE /v2/users/{userId}",
        ["UserManagementService_GetUser"] = "GET /v2/users/{userId}",
        ["UserManagementService_GrantUserRights"] = "POST /v2/users/{userId}/rights",
        ["UserManagementService_ListUserRights"] = "GET /v2/users/{userId}/rights",
        ["UserManagementService_ListUsers"] = "GET /v2/users",
        ["UserManagementService_RevokeUserRights"] = "PATCH /v2/users/{userId}/rights",
        ["UserManagementService_UpdateUser"] = "PATCH /v2/users/{user.id}",
        ["UserManagementService_UpdateUserIdentityProviderId"] = "PATCH /v2/users/{userId}/identity-provider-id",
        ["VersionService_GetLedgerApiVersion"] = "GET /v2/version",
    };

    private static readonly SortedSet<string> PinnedOffSpecEndpoints = new(StringComparer.Ordinal)
    {
        "IAuthenticatedUserApi.GetAuthenticatedUser: GET /v2/authenticated-user",
        "IDarApi.UploadDar: POST /v2/dars",
        "IDarApi.ValidateDar: POST /v2/dars/validate",
        "IHealthApi.CheckLiveness: GET /livez",
        "IHealthApi.CheckReadiness: GET /readyz",
        "IInteractiveSubmissionApi.GetPreferredPackageVersion: GET /v2/interactive-submission/preferred-package-version",
        "IPackageApi.GetPackage: GET /v2/packages/{packageId}",
    };

    private static string VendoredSpecPath => Path.Combine(AppContext.BaseDirectory, "spec", "openapi.yaml");

    [Fact]
    public void VendoredSpec_declares_every_pinned_ledger_api_operation()
    {
        var declared = ParseSpecOperations();

        var dropped = PinnedSpecOperations
            .Where(pinned => !declared.ContainsKey(pinned.Key))
            .Select(pinned => $"{pinned.Key} ({pinned.Value})")
            .ToList();

        dropped.Should().BeEmpty(SpecCoverageHint);
    }

    [Fact]
    public void VendoredSpec_declares_no_operation_the_pin_has_not_recorded()
    {
        var declared = ParseSpecOperations();

        var added = declared
            .Where(operation => !PinnedSpecOperations.ContainsKey(operation.Key))
            .Select(operation => $"{operation.Key} ({operation.Value})")
            .ToList();

        added.Should().BeEmpty(SpecCoverageHint);
    }

    [Fact]
    public void VendoredSpec_serves_every_pinned_operation_on_its_pinned_route()
    {
        var declared = ParseSpecOperations();

        var rerouted = PinnedSpecOperations
            .Where(pinned => declared.TryGetValue(pinned.Key, out var route) && route != pinned.Value)
            .Select(pinned => $"{pinned.Key}: pinned as '{pinned.Value}', spec now declares '{declared[pinned.Key]}'")
            .ToList();

        rerouted.Should().BeEmpty(SpecCoverageHint);
    }

    [Fact]
    public void RawSurface_routes_every_pinned_operation()
    {
        var routed = DiscoverRoutedEndpoints();

        var unrouted = PinnedGeneratedEndpoints()
            .Where(pinned => !routed.Contains(pinned.Value))
            .Select(pinned => $"{pinned.Key} ({pinned.Value})")
            .Concat(PinnedOffSpecEndpoints.Where(endpoint => !routed.Contains(endpoint)).Select(endpoint => $"off-spec ({endpoint})"))
            .ToList();

        unrouted.Should().BeEmpty(RoutingHint);
    }

    [Fact]
    public void RawSurface_routes_nothing_the_pin_has_not_recorded()
    {
        var pinned = PinnedGeneratedEndpoints()
            .Select(endpoint => endpoint.Value)
            .Concat(PinnedOffSpecEndpoints)
            .ToHashSet(StringComparer.Ordinal);

        var unpinned = DiscoverRoutedEndpoints().Where(endpoint => !pinned.Contains(endpoint)).Order(StringComparer.Ordinal).ToList();

        unpinned.Should().BeEmpty(RoutingHint);
    }

    [Fact]
    public void RoutedEndpoints_key_two_methods_sharing_one_route_on_one_interface_apart()
    {
        var routed = RoutedEndpointsOf([typeof(ITwinRoutedProbes)]);

        routed.Should().BeEquivalentTo(
            [
                "ITwinRoutedProbes.FirstProbe: GET /probe",
                "ITwinRoutedProbes.SecondProbe: GET /probe",
            ],
            RoutingHint);
    }

    private static IReadOnlyDictionary<string, string> ParseSpecOperations()
    {
        var operations = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var inPaths = false;
        var path = string.Empty;
        var method = string.Empty;

        foreach (var line in File.ReadAllLines(VendoredSpecPath))
        {
            if (!inPaths)
            {
                inPaths = line.StartsWith(PathsKey, StringComparison.Ordinal);
                continue;
            }

            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                break;
            }

            var key = KeyAtIndent(line, PathIndent);
            if (key?.StartsWith('/') == true)
            {
                path = key;
                method = string.Empty;
                continue;
            }

            key = KeyAtIndent(line, MethodIndent);
            if (key is not null)
            {
                method = PathItemVerbs.Contains(key) ? key.ToUpperInvariant() : string.Empty;
                continue;
            }

            key = KeyAtIndent(line, OperationIdIndent);
            if (key != OperationIdKey || path.Length == 0 || method.Length == 0)
            {
                continue;
            }

            var operationId = line[(OperationIdIndent + OperationIdKey.Length + 1)..].Trim();
            operations.Add(operationId, $"{method} {path}");
        }

        if (operations.Count == 0)
        {
            throw new InvalidOperationException(
                $"{VendoredSpecPath} yielded no operations; the parser or the spec layout changed and this guard "
                + "would otherwise pass vacuously.");
        }

        return operations;
    }

    private static string? KeyAtIndent(string line, int indent)
    {
        if (line.Length <= indent || line.AsSpan(0, indent).ContainsAnyExcept(' ') || line[indent] == ' ')
        {
            return null;
        }

        var separator = line.IndexOf(':', indent);
        return separator < 0 ? null : line[indent..separator];
    }

    private static IEnumerable<KeyValuePair<string, string>> PinnedGeneratedEndpoints() =>
        PinnedSpecOperations.Select(operation =>
            KeyValuePair.Create(operation.Key, GeneratedEndpointFor(operation.Key, operation.Value)));

    private static string GeneratedEndpointFor(string operationId, string route)
    {
        var serviceEnd = operationId.IndexOf('_', StringComparison.Ordinal);
        if (serviceEnd <= 0 || serviceEnd == operationId.Length - 1)
        {
            throw new InvalidOperationException(
                $"'{operationId}' does not follow the Service_Method operationId convention Refitter turns into "
                + "one interface per service and one method per operation, so its declaring interface and method "
                + "cannot be derived and this guard would otherwise pass vacuously.");
        }

        return DeclaredEndpoint($"I{operationId[..serviceEnd]}Api", operationId[(serviceEnd + 1)..], route);
    }

    private static string DeclaredEndpoint(string declaringInterface, string declaringMethod, string route) =>
        $"{declaringInterface}.{declaringMethod}: {route}";

    private static IReadOnlySet<string> DiscoverRoutedEndpoints()
    {
#pragma warning disable CANTONREST001
        var rawSurfaceAssembly = typeof(Canton.Ledger.Rest.Client.Raw.IStateServiceApi).Assembly;
#pragma warning restore CANTONREST001
        var endpoints = RoutedEndpointsOf(rawSurfaceAssembly.GetTypes()
            .Where(candidate => candidate is { IsInterface: true, IsPublic: true } && candidate.Namespace == RawNamespace));

        if (endpoints.Count == 0)
        {
            throw new InvalidOperationException(
                $"No Refit routes were discovered in {RawNamespace}; this guard would otherwise pass vacuously.");
        }

        return endpoints;
    }

    private static IReadOnlySet<string> RoutedEndpointsOf(IEnumerable<Type> declaringInterfaces) =>
        declaringInterfaces
            .SelectMany(declaringInterface => declaringInterface.GetMethods()
                .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: false)
                    .Select(route => DeclaredEndpoint(declaringInterface.Name, method.Name, $"{route.Method.Method} {route.Path}"))))
            .ToHashSet(StringComparer.Ordinal);

    internal interface ITwinRoutedProbes
    {
        [Get("/probe")]
        Task FirstProbe();

        [Get("/probe")]
        Task SecondProbe();
    }
}
