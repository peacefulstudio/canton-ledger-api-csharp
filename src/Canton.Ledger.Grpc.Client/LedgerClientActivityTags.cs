// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Grpc.Client;

internal static class LedgerClientActivityTags
{
    public const string DamlChoice = "daml.choice";
    public const string DamlContractId = "daml.contract_id";
    public const string DamlTemplateId = "daml.template_id";
    public const string DamlPackageId = "daml.package_id";
    public const string CantonOffset = "canton.offset";
    public const string CantonFromOffset = "canton.from_offset";
    public const string CantonSubmitterActAs = "canton.submitter.act_as";
    public const string CantonSubmitterReadAs = "canton.submitter.read_as";
    public const string CantonUpdateId = "canton.update_id";
    public const string CantonPartyId = "canton.party_id";
    public const string CantonParticipantId = "canton.participant_id";
    public const string CantonPartyIdHint = "canton.party_id_hint";
    public const string CantonUserId = "canton.user_id";
    public const string CantonSubmissionId = "canton.submission_id";
    public const string CantonTrafficCostBytes = "canton.traffic_cost_bytes";
}
