// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using WireUpdate = Canton.Ledger.Rest.Client.Raw.GetUpdatesResponse;

namespace Canton.Ledger.Rest.Client.Tests;

public class ContractStreamProjectorTransactionEventsTests
{
    private sealed record TemplateMarker : ITemplate
    {
        public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "Holding");
        public static string PackageId => "tmpl-pkg";
        public static string PackageName => "token-impl";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, []);
    }

    private static Raw.Transaction TransactionFrom(string json)
    {
        var update = JsonSerializer.Deserialize<WireUpdate>(json, RestRefitSettings.SerializerOptions);
        return update!.Update.Transaction;
    }

    private static Raw.Reassignment ReassignmentFrom(string json)
    {
        var update = JsonSerializer.Deserialize<WireUpdate>(json, RestRefitSettings.SerializerOptions);
        return update!.Update.Reassignment;
    }

    [Fact]
    public void ProjectTransactionEvents_projects_a_matching_created_event()
    {
        var transaction = TransactionFrom(
            """
            {
              "update": {
                "Transaction": {
                  "value": {
                    "offset": "7",
                    "synchronizerId": "sync-1",
                    "events": [
                      {
                        "CreatedEvent": {
                          "offset": "7",
                          "contractId": "00holding",
                          "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                          "createArgument": {"fields": [{"label": "owner", "value": {"party": "alice::ns1"}}]},
                          "witnessParties": ["alice::ns1"]
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);

        var projected = ContractStreamProjector.ProjectTransactionEvents<TemplateMarker>(transaction).Should().ContainSingle().Subject;

        var created = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject;
        created.ContractId.Value.Should().Be("00holding");
        created.Offset.Value.Should().Be(7L);
        created.SynchronizerId.Should().Be(new SynchronizerId("sync-1"));
        created.WitnessParties.Should().ContainSingle().Which.Should().Be((Party)"alice::ns1");
    }

    [Fact]
    public void ProjectTransactionEvents_projects_a_matching_archived_event()
    {
        var transaction = TransactionFrom(
            """
            {
              "update": {
                "Transaction": {
                  "value": {
                    "offset": "8",
                    "synchronizerId": "sync-1",
                    "events": [
                      {
                        "ArchivedEvent": {
                          "offset": "8",
                          "contractId": "00holding",
                          "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                          "witnessParties": ["alice::ns1"]
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);

        var projected = ContractStreamProjector.ProjectTransactionEvents<TemplateMarker>(transaction).Should().ContainSingle().Subject;

        var archived = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Archived>().Subject;
        archived.ContractId.Value.Should().Be("00holding");
        archived.Offset.Value.Should().Be(8L);
        archived.SynchronizerId.Should().Be(new SynchronizerId("sync-1"));
    }

    [Fact]
    public void ProjectTransactionEvents_projects_a_matching_exercised_event()
    {
        var transaction = TransactionFrom(
            """
            {
              "update": {
                "Transaction": {
                  "value": {
                    "offset": "9",
                    "synchronizerId": "sync-1",
                    "events": [
                      {
                        "ExercisedEvent": {
                          "offset": "9",
                          "contractId": "00holding",
                          "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                          "choice": "Archive",
                          "choiceArgument": {"record": {"fields": []}},
                          "actingParties": ["alice::ns1"],
                          "consuming": true,
                          "witnessParties": ["alice::ns1"],
                          "exerciseResult": {"unit": {}}
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);

        var projected = ContractStreamProjector.ProjectTransactionEvents<TemplateMarker>(transaction).Should().ContainSingle().Subject;

        var exercised = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Exercised>().Subject;
        exercised.ContractId.Value.Should().Be("00holding");
        exercised.ChoiceName.Should().Be("Archive");
        exercised.Consuming.Should().BeTrue();
        exercised.Offset.Value.Should().Be(9L);
    }

    [Fact]
    public void ProjectTransactionEvents_surfaces_a_template_mismatch_as_Unclassified()
    {
        var transaction = TransactionFrom(
            """
            {
              "update": {
                "Transaction": {
                  "value": {
                    "offset": "7",
                    "synchronizerId": "sync-1",
                    "events": [
                      {
                        "CreatedEvent": {
                          "offset": "7",
                          "contractId": "00other",
                          "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Other"},
                          "createArgument": {"fields": []}
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);

        var projected = ContractStreamProjector.ProjectTransactionEvents<TemplateMarker>(transaction).Should().ContainSingle().Subject;

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.CreatedEvent);
    }

    [Fact]
    public void ProjectTransactionEvents_surfaces_a_missing_synchronizer_id_as_Unclassified()
    {
        var transaction = TransactionFrom(
            """
            {
              "update": {
                "Transaction": {
                  "value": {
                    "offset": "7",
                    "events": [
                      {
                        "CreatedEvent": {
                          "offset": "7",
                          "contractId": "00holding",
                          "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                          "createArgument": {"fields": []}
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);

        var projected = ContractStreamProjector.ProjectTransactionEvents<TemplateMarker>(transaction).Should().ContainSingle().Subject;

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
    }

    [Fact]
    public void ProjectTransactionEvents_surfaces_undecodable_choice_argument_as_Unclassified_decode_failure_and_logs_a_warning()
    {
        var transaction = TransactionFrom(
            """
            {
              "update": {
                "Transaction": {
                  "value": {
                    "offset": "9",
                    "synchronizerId": "sync-1",
                    "events": [
                      {
                        "ExercisedEvent": {
                          "offset": "9",
                          "contractId": "00holding",
                          "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                          "choice": "Archive",
                          "choiceArgument": {"int64": "not-a-number"},
                          "actingParties": ["alice::ns1"],
                          "consuming": true,
                          "witnessParties": ["alice::ns1"]
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);
        var loggerFactory = new CapturingLoggerFactory();

        var projected = ContractStreamProjector
            .ProjectTransactionEvents<TemplateMarker>(transaction, loggerFactory.CreateLogger("test"))
            .Should().ContainSingle().Subject;

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning);
    }

    [Fact]
    public void ProjectTransactionEvents_surfaces_an_unparseable_archived_offset_as_Unclassified_decode_failure()
    {
        var transaction = TransactionFrom(
            """
            {
              "update": {
                "Transaction": {
                  "value": {
                    "offset": "8",
                    "synchronizerId": "sync-1",
                    "events": [
                      {
                        "ArchivedEvent": {
                          "offset": "not-a-number",
                          "contractId": "00holding",
                          "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                          "witnessParties": ["alice::ns1"]
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);
        var loggerFactory = new CapturingLoggerFactory();

        var projected = ContractStreamProjector
            .ProjectTransactionEvents<TemplateMarker>(transaction, loggerFactory.CreateLogger("test"))
            .Should().ContainSingle().Subject;

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        unclassified.Offset.Value.Should().Be(8L);
        loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning);
    }

    [Fact]
    public void ProjectTransactionEvents_surfaces_an_unparseable_exercised_offset_as_Unclassified_decode_failure()
    {
        var transaction = TransactionFrom(
            """
            {
              "update": {
                "Transaction": {
                  "value": {
                    "offset": "9",
                    "synchronizerId": "sync-1",
                    "events": [
                      {
                        "ExercisedEvent": {
                          "offset": "not-a-number",
                          "contractId": "00holding",
                          "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                          "choice": "Archive",
                          "choiceArgument": {"record": {"fields": []}},
                          "actingParties": ["alice::ns1"],
                          "consuming": true,
                          "witnessParties": ["alice::ns1"]
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);
        var loggerFactory = new CapturingLoggerFactory();

        var projected = ContractStreamProjector
            .ProjectTransactionEvents<TemplateMarker>(transaction, loggerFactory.CreateLogger("test"))
            .Should().ContainSingle().Subject;

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        unclassified.Offset.Value.Should().Be(9L);
        loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning);
    }

    [Fact]
    public void ProjectTransactionEvents_surfaces_an_unparseable_transaction_offset_as_Unclassified_decode_failure()
    {
        var transaction = TransactionFrom(
            """{"update": {"Transaction": {"value": {"offset": "not-a-number", "synchronizerId": "sync-1", "events": [{}]}}}}""");
        var loggerFactory = new CapturingLoggerFactory();

        var projected = ContractStreamProjector
            .ProjectTransactionEvents<TemplateMarker>(transaction, loggerFactory.CreateLogger("test"))
            .Should().ContainSingle().Subject;

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(
            record => record.Level == LogLevel.Warning && record.Message.Contains("not-a-number"));
    }

    [Fact]
    public void ProjectTransactionEvents_warns_that_an_unparseable_transaction_offset_is_reported_at_the_begin_of_the_ledger()
    {
        var transaction = TransactionFrom(
            """{"update": {"Transaction": {"value": {"offset": "not-a-number", "synchronizerId": "sync-1", "events": [{}]}}}}""");
        var loggerFactory = new CapturingLoggerFactory();

        var projected = ContractStreamProjector
            .ProjectTransactionEvents<TemplateMarker>(transaction, loggerFactory.CreateLogger("test"))
            .Should().ContainSingle().Subject;

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.Begin);
        loggerFactory.Records.Should().ContainSingle(
            record => record.Level == LogLevel.Warning
                && record.Message.Contains("begin-of-ledger offset")
                && record.Message.Contains("re-read the stream from the start"));
    }

    [Fact]
    public void ProjectReassignmentEvents_projects_a_matching_assigned_event()
    {
        var reassignment = ReassignmentFrom(
            """
            {
              "update": {
                "Reassignment": {
                  "value": {
                    "offset": "20",
                    "events": [
                      {
                        "JsAssignmentEvent": {
                          "source": "sync-1",
                          "target": "sync-2",
                          "reassignmentId": "reassign-1",
                          "reassignmentCounter": "3",
                          "createdEvent": {
                            "offset": "20",
                            "contractId": "00holding",
                            "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                            "createArgument": {"fields": []},
                            "witnessParties": ["alice::ns1"]
                          }
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);

        var projected = ContractStreamProjector.ProjectReassignmentEvents<TemplateMarker>(reassignment).Should().ContainSingle().Subject;

        var assigned = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Assigned>().Subject;
        assigned.ContractId.Value.Should().Be("00holding");
        assigned.Source.Should().Be(new SynchronizerId("sync-1"));
        assigned.Target.Should().Be(new SynchronizerId("sync-2"));
        assigned.ReassignmentId.Should().Be("reassign-1");
        assigned.ReassignmentCounter.Should().Be(3L);
    }

    [Fact]
    public void ProjectReassignmentEvents_projects_a_matching_unassigned_event()
    {
        var reassignment = ReassignmentFrom(
            """
            {
              "update": {
                "Reassignment": {
                  "value": {
                    "offset": "21",
                    "events": [
                      {
                        "JsUnassignedEvent": {
                          "value": {
                            "source": "sync-1",
                            "target": "sync-2",
                            "contractId": "00holding",
                            "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                            "reassignmentId": "reassign-2",
                            "reassignmentCounter": "4",
                            "offset": "21",
                            "witnessParties": ["alice::ns1"]
                          }
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);

        var projected = ContractStreamProjector.ProjectReassignmentEvents<TemplateMarker>(reassignment).Should().ContainSingle().Subject;

        var unassigned = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unassigned>().Subject;
        unassigned.ContractId.Value.Should().Be("00holding");
        unassigned.Source.Should().Be(new SynchronizerId("sync-1"));
        unassigned.Target.Should().Be(new SynchronizerId("sync-2"));
        unassigned.ReassignmentId.Should().Be("reassign-2");
        unassigned.ReassignmentCounter.Should().Be(4L);
    }

    [Fact]
    public void ProjectReassignmentEvents_surfaces_a_template_mismatch_on_unassigned_as_Unclassified()
    {
        var reassignment = ReassignmentFrom(
            """
            {
              "update": {
                "Reassignment": {
                  "value": {
                    "offset": "21",
                    "events": [
                      {
                        "JsUnassignedEvent": {
                          "value": {
                            "source": "sync-1",
                            "target": "sync-2",
                            "contractId": "00holding",
                            "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Other"},
                            "reassignmentId": "reassign-2",
                            "reassignmentCounter": "4",
                            "offset": "21"
                          }
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);

        var projected = ContractStreamProjector.ProjectReassignmentEvents<TemplateMarker>(reassignment).Should().ContainSingle().Subject;

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.UnassignedEvent);
    }

    [Theory]
    [InlineData("not-a-number", "3")]
    [InlineData("20", "not-a-number")]
    public void ProjectReassignmentEvents_surfaces_an_unparseable_assigned_offset_or_counter_as_Unclassified_decode_failure(
        string createdOffset,
        string reassignmentCounter)
    {
        var reassignment = ReassignmentFrom(
            $$"""
            {
              "update": {
                "Reassignment": {
                  "value": {
                    "offset": "20",
                    "events": [
                      {
                        "JsAssignmentEvent": {
                          "source": "sync-1",
                          "target": "sync-2",
                          "reassignmentId": "reassign-1",
                          "reassignmentCounter": "{{reassignmentCounter}}",
                          "createdEvent": {
                            "offset": "{{createdOffset}}",
                            "contractId": "00holding",
                            "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                            "createArgument": {"fields": []},
                            "witnessParties": ["alice::ns1"]
                          }
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);
        var loggerFactory = new CapturingLoggerFactory();

        var projected = ContractStreamProjector
            .ProjectReassignmentEvents<TemplateMarker>(reassignment, loggerFactory.CreateLogger("test"))
            .Should().ContainSingle().Subject;

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        unclassified.Offset.Value.Should().Be(20L);
        loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning);
    }

    [Theory]
    [InlineData("not-a-number", "4")]
    [InlineData("21", "not-a-number")]
    public void ProjectReassignmentEvents_surfaces_an_unparseable_unassigned_offset_or_counter_as_Unclassified_decode_failure(
        string unassignedOffset,
        string reassignmentCounter)
    {
        var reassignment = ReassignmentFrom(
            $$"""
            {
              "update": {
                "Reassignment": {
                  "value": {
                    "offset": "21",
                    "events": [
                      {
                        "JsUnassignedEvent": {
                          "value": {
                            "source": "sync-1",
                            "target": "sync-2",
                            "contractId": "00holding",
                            "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Holding"},
                            "reassignmentId": "reassign-2",
                            "reassignmentCounter": "{{reassignmentCounter}}",
                            "offset": "{{unassignedOffset}}",
                            "witnessParties": ["alice::ns1"]
                          }
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);
        var loggerFactory = new CapturingLoggerFactory();

        var projected = ContractStreamProjector
            .ProjectReassignmentEvents<TemplateMarker>(reassignment, loggerFactory.CreateLogger("test"))
            .Should().ContainSingle().Subject;

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        unclassified.Offset.Value.Should().Be(21L);
        loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning);
    }

    [Fact]
    public void ProjectReassignmentEvents_surfaces_an_unparseable_reassignment_offset_as_Unclassified_decode_failure()
    {
        var reassignment = ReassignmentFrom(
            """{"update": {"Reassignment": {"value": {"offset": "not-a-number", "events": [{}]}}}}""");
        var loggerFactory = new CapturingLoggerFactory();

        var projected = ContractStreamProjector
            .ProjectReassignmentEvents<TemplateMarker>(reassignment, loggerFactory.CreateLogger("test"))
            .Should().ContainSingle().Subject;

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(
            record => record.Level == LogLevel.Warning && record.Message.Contains("not-a-number"));
    }

    [Fact]
    public void ProjectTransactionEvents_separates_an_archived_event_without_a_templateId_from_a_different_template_archive()
    {
        var transaction = TransactionFrom(
            """
            {
              "update": {
                "Transaction": {
                  "value": {
                    "offset": "8",
                    "synchronizerId": "sync-1",
                    "events": [
                      {
                        "ArchivedEvent": {
                          "offset": "8",
                          "contractId": "00other",
                          "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Other"},
                          "witnessParties": ["alice::ns1"]
                        }
                      },
                      {
                        "ArchivedEvent": {
                          "offset": "8",
                          "contractId": "00holding",
                          "witnessParties": ["alice::ns1"]
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);
        var loggerFactory = new CapturingLoggerFactory();

        var projected = ContractStreamProjector
            .ProjectTransactionEvents<TemplateMarker>(transaction, loggerFactory.CreateLogger("test"))
            .ToList();

        projected.Should().HaveCount(2);
        projected[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>()
            .Which.Kind.Should().Be(UnclassifiedKind.ArchivedEvent);
        projected[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>()
            .Which.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning);
    }

    [Fact]
    public void ProjectReassignmentEvents_separates_an_unassigned_event_without_a_templateId_from_a_different_template_unassign()
    {
        var reassignment = ReassignmentFrom(
            """
            {
              "update": {
                "Reassignment": {
                  "value": {
                    "offset": "21",
                    "events": [
                      {
                        "JsUnassignedEvent": {
                          "value": {
                            "source": "sync-1",
                            "target": "sync-2",
                            "contractId": "00other",
                            "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Other"},
                            "reassignmentId": "reassign-2",
                            "reassignmentCounter": "4",
                            "offset": "21"
                          }
                        }
                      },
                      {
                        "JsUnassignedEvent": {
                          "value": {
                            "source": "sync-1",
                            "target": "sync-2",
                            "contractId": "00holding",
                            "reassignmentId": "reassign-3",
                            "reassignmentCounter": "5",
                            "offset": "21"
                          }
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);
        var loggerFactory = new CapturingLoggerFactory();

        var projected = ContractStreamProjector
            .ProjectReassignmentEvents<TemplateMarker>(reassignment, loggerFactory.CreateLogger("test"))
            .ToList();

        projected.Should().HaveCount(2);
        projected[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>()
            .Which.Kind.Should().Be(UnclassifiedKind.UnassignedEvent);
        projected[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>()
            .Which.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning);
    }

    [Fact]
    public void ProjectTransactionEvents_separates_a_created_event_without_a_templateId_from_a_different_template_create()
    {
        var transaction = TransactionFrom(
            """
            {
              "update": {
                "Transaction": {
                  "value": {
                    "offset": "7",
                    "synchronizerId": "sync-1",
                    "events": [
                      {
                        "CreatedEvent": {
                          "offset": "7",
                          "contractId": "00other",
                          "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Other"},
                          "createArgument": {"fields": []},
                          "witnessParties": ["alice::ns1"]
                        }
                      },
                      {
                        "CreatedEvent": {
                          "offset": "7",
                          "contractId": "00holding",
                          "createArgument": {"fields": []},
                          "witnessParties": ["alice::ns1"]
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);
        var loggerFactory = new CapturingLoggerFactory();

        var projected = ContractStreamProjector
            .ProjectTransactionEvents<TemplateMarker>(transaction, loggerFactory.CreateLogger("test"))
            .ToList();

        projected.Should().HaveCount(2);
        projected[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>()
            .Which.Kind.Should().Be(UnclassifiedKind.CreatedEvent);
        projected[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>()
            .Which.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning);
    }

    [Fact]
    public void ProjectTransactionEvents_separates_an_exercised_event_without_a_templateId_from_a_different_template_exercise()
    {
        var transaction = TransactionFrom(
            """
            {
              "update": {
                "Transaction": {
                  "value": {
                    "offset": "9",
                    "synchronizerId": "sync-1",
                    "events": [
                      {
                        "ExercisedEvent": {
                          "offset": "9",
                          "contractId": "00other",
                          "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Other"},
                          "choice": "Archive",
                          "choiceArgument": {"record": {"fields": []}},
                          "actingParties": ["alice::ns1"],
                          "consuming": true,
                          "witnessParties": ["alice::ns1"]
                        }
                      },
                      {
                        "ExercisedEvent": {
                          "offset": "9",
                          "contractId": "00holding",
                          "choice": "Archive",
                          "choiceArgument": {"record": {"fields": []}},
                          "actingParties": ["alice::ns1"],
                          "consuming": true,
                          "witnessParties": ["alice::ns1"]
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);
        var loggerFactory = new CapturingLoggerFactory();

        var projected = ContractStreamProjector
            .ProjectTransactionEvents<TemplateMarker>(transaction, loggerFactory.CreateLogger("test"))
            .ToList();

        projected.Should().HaveCount(2);
        projected[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>()
            .Which.Kind.Should().Be(UnclassifiedKind.ExercisedEvent);
        projected[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>()
            .Which.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning);
    }

    [Fact]
    public void ProjectReassignmentEvents_separates_an_assigned_event_without_a_templateId_from_a_different_template_assign()
    {
        var reassignment = ReassignmentFrom(
            """
            {
              "update": {
                "Reassignment": {
                  "value": {
                    "offset": "20",
                    "events": [
                      {
                        "JsAssignmentEvent": {
                          "source": "sync-1",
                          "target": "sync-2",
                          "reassignmentId": "reassign-1",
                          "reassignmentCounter": "3",
                          "createdEvent": {
                            "offset": "20",
                            "contractId": "00other",
                            "templateId": {"packageId": "tmpl-pkg", "moduleName": "Sample.Token", "entityName": "Other"},
                            "createArgument": {"fields": []},
                            "witnessParties": ["alice::ns1"]
                          }
                        }
                      },
                      {
                        "JsAssignmentEvent": {
                          "source": "sync-1",
                          "target": "sync-2",
                          "reassignmentId": "reassign-2",
                          "reassignmentCounter": "4",
                          "createdEvent": {
                            "offset": "20",
                            "contractId": "00holding",
                            "createArgument": {"fields": []},
                            "witnessParties": ["alice::ns1"]
                          }
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);
        var loggerFactory = new CapturingLoggerFactory();

        var projected = ContractStreamProjector
            .ProjectReassignmentEvents<TemplateMarker>(reassignment, loggerFactory.CreateLogger("test"))
            .ToList();

        projected.Should().HaveCount(2);
        projected[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>()
            .Which.Kind.Should().Be(UnclassifiedKind.AssignedEvent);
        projected[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>()
            .Which.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        loggerFactory.Records.Should().ContainSingle(record => record.Level == LogLevel.Warning);
    }
}
