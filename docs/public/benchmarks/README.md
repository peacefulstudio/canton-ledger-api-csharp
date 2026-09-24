# Streaming Benchmarks

This page covers the end-to-end benchmarks for the `Canton.Ledger.*` client packages. They measure submission latency, stream delivery latency and read throughput over the three transports: the gRPC Ledger API, the JSON Ledger API (REST) and the Participant Query Store (PQS). Every figure below was measured against a running LocalNet. None is estimated.

The harness is in [`benchmarks/Canton.Ledger.Benchmarks`](../../../benchmarks/Canton.Ledger.Benchmarks). It drives the public client surface, `ICantonLedgerClient` and `IPqsClient`, through the same `IAsyncEnumerable` paths an application uses. It is not a BenchmarkDotNet micro-benchmark. At this level the participant and the network dominate the time, so the harness measures wall-clock time around real ledger round-trips.

## Scenarios

All contracts are `Marker` or `Asset` instances from the pinned conformance DAR (`Daml.Codegen.Testing.Conformance`, RichTypes). The DAR is uploaded on startup.

| Scenario | What is timed | Transports |
|---|---|---|
| Submit-and-wait (sequential) | One `create`, from submission until the transaction is returned, one command at a time | gRPC, REST |
| Submit-and-wait, N in flight | Wall-clock time for `--seed-contracts` creates with `--concurrency` commands in flight | gRPC, REST |
| Update stream replay (bounded range) | Reading every created event between two recorded offsets with `SubscribeAsync` | gRPC, REST |
| Active-contract snapshot | Reading a party's ACS with `SubscribeActiveAsync` up to its terminal checkpoint | gRPC, REST |
| Submit → completion on the completion stream | From `SubmitAsync` until the completion arrives on an open, unbounded completion stream | gRPC, REST, REST with a shorter idle window |
| Submit → created event on a live update tail | From a gRPC submission until the created event arrives on an open, unbounded `SubscribeAsync` tail | gRPC, REST, REST with a shorter idle window |
| Submit → contract visible in PQS | From a gRPC create until `ExistsAsync` sees the contract (polled every 10 ms) | PQS |
| Paged template query | One `QueryAsync<Asset>` page of `--pqs-rows` rows | PQS |

Each read-throughput scenario runs `--repetitions` times. The table reports the median and the best repetition. Each tail scenario sends a few warm-up samples that are not counted, then samples one command at a time with `--tail-spacing-ms` between samples. If a stream ends with an in-band `StreamError`, it is reopened from its last offset, and the report records that in its run notes.

## Results

Latest run: [`results/2026-09-24-local-macos-arm64`](results/2026-09-24-local-macos-arm64/results.md) ([JSON](results/2026-09-24-local-macos-arm64/results.json)). The client packages are 0.5.0-preview.2 against a Canton 3.5.18 participant, on an Apple Silicon laptop running LocalNet in Docker Desktop. This run predates REST ACS paging (`POST /v2/state/active-contracts-page`): its large-ACS row is gRPC only, and its REST ACS row is capped at 200 contracts. A re-measure with both transports on the large-ACS row is pending.

The run used traffic-budgeted parameters: `--warmup 10 --seed-contracts 50 --acs-contracts 50 --latency-samples 50 --tail-samples 30 --pqs-lag-samples 30 --pqs-rows 100`. The Caveats section explains why. It records commit `eb33bef`. The later commit `4c64ef8` changes only the retry backoff, and this run needed no retries.

### Latency, p50 / p99 (ms)

| Scenario | gRPC | REST (2 s idle window) | REST (250 ms idle window) | PQS |
|---|---:|---:|---:|---:|
| Submit-and-wait, sequential | 359.9 / 369.3 | 359.7 / 375.1 | | |
| Submit → completion on the completion stream | 338.9 / 349.9 | 2559.6 / 2567.8 | 699.4 / 718.6 | |
| Submit → created event on a live update tail | 338.0 / 352.9 | 2559.5 / 2578.8 | 586.3 / 707.3 | |
| Submit → contract visible in PQS | | | | 359.4 / 508.5 |

### Throughput, median of 5 repetitions

| Scenario | gRPC | REST | PQS |
|---|---:|---:|---:|
| Update stream replay, 100 events | 9 752 events/s | 6 424 events/s | |
| ACS snapshot, 50 contracts | 4 865 contracts/s | 3 899 contracts/s | |
| ACS snapshot, 120 contracts (gRPC only; predates REST ACS paging) | 7 570 contracts/s | | |
| Paged template query, 100 rows | | | 66 876 rows/s |
| Submit-and-wait, 16 in flight, 50 commands (one burst) | 35 commands/s | 35 commands/s | |

### What the numbers say

- **Submission cost does not depend on the transport.** gRPC and REST submit-and-wait match within a millisecond at p50, at about 360 ms. The participant's commit path sets the time, not the client.
- **Live REST delivery waits for the stream window to close.** The REST client streams by re-POSTing bounded windows. Over a live tail, an event only reaches the caller when its window idles out, so delivery takes about the commit time plus `StreamWindowIdleTimeout`: 2.56 s with a 2 s window, against 0.34 s over gRPC. A 250 ms window brings REST down to 0.59–0.70 s, at the cost of more requests. The recorded run predates the change of the client's default from 2 s to 250 ms, so its plain `REST` rows in `results.json` used the 2 s window; a follow-up run with the 250 ms default measured 699.9 ms p50 on the completion stream and 698.7 ms on the update tail. This is the measurement behind the open questions of a websocket transport and adaptive window backoff for REST streaming.
- **Bounded reads are fast on both transports.** Replaying a fixed offset range or reading an ACS takes around 10–16 ms for 50–120 items. gRPC is 1.25–1.5× faster than REST.
- **PQS trails the ledger by about the commit time.** A contract becomes queryable in PQS about 360 ms after the create is submitted, which is close to the submit-and-wait time itself.

## Reproducing

1. Bring up a LocalNet with PQS enabled ([`canton-localnet`](https://github.com/peacefulstudio/canton-localnet)): `canton-localnet up && canton-localnet wait-ready`.
2. Export the validator endpoints and credentials. These are the same variables the integration tests read:

   | Variable | Example |
   |---|---|
   | `CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL` | `http://localhost:11975` |
   | `CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL` | `http://localhost:11901` |
   | `CANTON_LOCALNET_A_VALIDATOR_1_CLIENT_ID` / `_CLIENT_SECRET` | from the LocalNet keycloak module |
   | `CANTON_LOCALNET_A_VALIDATOR_1_PQS_CONNECTION_STRING` | `Host=localhost;Port=15432;Database=pqs-a-validator-1;Username=…;Password=…` |

   PQS is skipped, with a run note, when its connection string is not set.
3. Run the harness:

   ```bash
   dotnet run -c Release --project benchmarks/Canton.Ledger.Benchmarks -- --output benchmark-results
   ```

   The run writes `results.json` and `results.md` to the output directory and prints the Markdown to stdout. Flags: `--warmup`, `--seed-contracts`, `--concurrency`, `--repetitions`, `--latency-samples`, `--tail-samples`, `--tail-spacing-ms`, `--rest-idle-windows-ms` (comma-separated), `--acs-contracts`, `--pqs-lag-samples`, `--pqs-rows`, `--commit`, `--runner`.

A run with the parameters above takes about 7 minutes. Each run allocates fresh parties, so repeated runs on the same LocalNet don't read each other's contracts. PQS scenarios are the exception, as the caveats below explain.

## Caveats

- **LocalNet is not production.** One machine runs the participant, the sequencer, the mediator, PQS and the harness. Treat the figures as a comparison between transports and between versions of these packages, not as a capacity statement for a Canton deployment.
- **The recorded run predates REST ACS paging.** `SubscribeActiveAsync` over REST used to read the ACS in one un-paged call, which the LocalNet participant rejected with `413 JSON_API_MAXIMUM_LIST_ELEMENTS_NUMBER_REACHED` past 200 elements; the recorded run therefore compares transports on a dedicated 200-contract party and runs the large-ACS row on gRPC only. The client now pages the ACS read over `POST /v2/state/active-contracts-page`, so both rows run on both transports going forward; the dedicated 200-contract party stays, since it gives a size that stays fixed regardless of `--seed-contracts` or `--warmup`, unlike the large-ACS row's incidental size.
- **LocalNet traffic caps sustained submission.** Each submission spends the validator's synchronizer traffic. LocalNet tops it up at `TARGET_TRAFFIC_THROUGHPUT=20000` bytes/s, about two `Marker` creates per second at the cost we observed, which was about 9.3 k per submission. Short bursts draw on a reserve. Longer runs exhaust it and fail with `SEQUENCER_NOT_ENOUGH_TRAFFIC_CREDIT`, which the Ledger API reports as `SEQUENCER_REQUEST_FAILED` in the `ContentionOnSharedResources` category. The harness retries retryable categories with exponential backoff, and it records the retry count in the run notes because the retry time falls inside the measured figures. The submit-throughput row is therefore a single 50-command burst, not a sustained rate. Raise `TARGET_TRAFFIC_THROUGHPUT` when you bring LocalNet up to benchmark larger volumes.
- **PQS scenarios create contracts as the validator operator.** The PQS scribe only projects contracts visible to the validator's primary party. Those `Asset` contracts are never archived, so they build up across runs on a long-lived LocalNet.
