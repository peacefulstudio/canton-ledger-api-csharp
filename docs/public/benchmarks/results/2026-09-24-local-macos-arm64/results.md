## Environment

| | |
| --- | --- |
| Started (UTC) | 2026-09-24 12:21:18Z |
| Commit | `eb33befba4efa9e637e7339d604ca87005f7b2e7` |
| Runner | local workstation: Apple Silicon laptop, LocalNet on Docker Desktop |
| OS / arch / CPUs | macOS 27.0.0 / Arm64 / 12 |
| .NET runtime | .NET 10.0.9 |
| Client packages | 0.5.0-preview.2+eb33befba4efa9e637e7339d604ca87005f7b2e7 |
| Canton protos pinned | 3.5.18 |
| Participant Ledger API version | 3.5.18 |
| PQS measured | yes |

## Latency (milliseconds)

| Scenario | Transport | Samples | p50 | p95 | p99 | Max | Mean |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Submit-and-wait (sequential) | gRPC | 50 | 359.9 | 368.3 | 369.3 | 369.3 | 360.2 |
| Submit-and-wait (sequential) | REST | 50 | 359.7 | 372.2 | 375.1 | 375.1 | 360.0 |
| Submit → completion on the completion stream | gRPC | 30 | 338.9 | 349.5 | 349.9 | 349.9 | 337.9 |
| Submit → completion on the completion stream | REST | 30 | 2559.6 | 2567.5 | 2567.8 | 2567.8 | 2560.8 |
| Submit → completion on the completion stream | REST, 250 ms idle window | 30 | 699.4 | 718.1 | 718.6 | 718.6 | 690.1 |
| Submit → created event on a live update tail | gRPC | 30 | 338.0 | 351.0 | 352.9 | 352.9 | 338.4 |
| Submit → created event on a live update tail | REST | 30 | 2559.5 | 2570.5 | 2578.8 | 2578.8 | 2561.4 |
| Submit → created event on a live update tail | REST, 250 ms idle window | 30 | 586.3 | 705.4 | 707.3 | 707.3 | 632.9 |
| Submit → contract visible in PQS | PQS | 30 | 359.4 | 383.1 | 508.5 | 508.5 | 365.7 |

## Throughput

| Scenario | Transport | Items | Repetitions | Median seconds | Median rate | Best rate |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Submit-and-wait, 16 in flight | gRPC | 50 | 1 | 1.433 | 35 commands/s | 35 commands/s |
| Submit-and-wait, 16 in flight | REST | 50 | 1 | 1.441 | 35 commands/s | 35 commands/s |
| Update stream replay (bounded range) | gRPC | 100 | 5 | 0.010 | 9752 events/s | 11215 events/s |
| Update stream replay (bounded range) | REST | 100 | 5 | 0.016 | 6424 events/s | 7210 events/s |
| Active-contract snapshot, 50 contracts | gRPC | 50 | 5 | 0.010 | 4865 contracts/s | 5311 contracts/s |
| Active-contract snapshot, 50 contracts | REST | 50 | 5 | 0.013 | 3899 contracts/s | 4005 contracts/s |
| Active-contract snapshot, 120 contracts | gRPC | 120 | 5 | 0.016 | 7570 contracts/s | 8407 contracts/s |
| Paged template query | PQS | 100 | 5 | 0.001 | 66876 rows/s | 78604 rows/s |

## Run notes

- The 120-contract snapshot runs over gRPC only: the REST client reads the ACS in one un-paged call, and the LocalNet participant rejects JSON API lists over 200 elements (413 JSON_API_MAXIMUM_LIST_ELEMENTS_NUMBER_REACHED).
