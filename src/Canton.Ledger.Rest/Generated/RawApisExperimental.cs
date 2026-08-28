// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

// The [Experimental] markers live in this companion partial rather than in the
// Refitter-generated CantonLedgerApi.g.cs, so regenerating the Refit surface
// (Refitter overwrites g.cs wholesale) does not drop them. The generated namespace
// itself is configured from src/Canton.Ledger.Rest/.refitter.
namespace Canton.Ledger.Rest.Client.Raw;

[Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
public partial interface ICommandSubmissionServiceApi { }

[Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
public partial interface ICommandCompletionServiceApi { }

[Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
public partial interface ICommandServiceApi { }

[Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
public partial interface IContractServiceApi { }

[Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
public partial interface IPackageManagementServiceApi { }

[Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
public partial interface IEventQueryServiceApi { }

[Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
public partial interface IIdentityProviderConfigServiceApi { }

[Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
public partial interface IInteractiveSubmissionServiceApi { }

[Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
public partial interface IPackageServiceApi { }

[Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
public partial interface IPartyManagementServiceApi { }

[Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
public partial interface IStateServiceApi { }

[Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
public partial interface IUpdateServiceApi { }

[Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
public partial interface IUserManagementServiceApi { }

[Experimental(CantonRestDiagnostics.ExperimentalDiagnosticId)]
public partial interface IVersionServiceApi { }
