---
marp: true
---

# DomainBlocks

## Correctness-first event sourcing with MongoDB

---

# What should an event store guarantee?

- **Atomic multi-event commits**
    - All events from a single transaction are visible together or not at all
    - Partial writes corrupt aggregate state
- **Append-only durability**
    - Once committed, events are permanent and immutable
- **Stream ordering**
    - Events within a stream must reflect the order they were applied
    - Out-of-order reads break aggregate state reconstruction
- **Optimistic concurrency**
    - Concurrent writers to the same stream fail fast

---

# What should an event store guarantee? (continued)

- **Read-your-writes**
    - Successful writes are visible to subsequent reads
- **Resumable subscriptions**
  - A consumer should be able to resume from a precise position after failure, process restart, etc.
  - Requires a stable global position as a first-class primitive, not an approximation
- **Global ordering**
    - A stable total order across all streams (real-time and historical)
    - Causally consistent event delivery
    - Precise checkpoints

---

# MongoDB as an event store

**Advantages**

- Familiar tooling for teams already running MongoDB
- No additional infrastructure to operate
- Rich query model, e.g. aggregation pipelines
- Change streams

**Challenges**

- Document-level atomicity
- Multi-document transaction overhead
- No global ordering primitive

---

# Global ordering - the case for a single writer

- Global position must be assigned sequentially
- Multiple writers:
    - Sequence increment and write must be atomic
    - MongoDB: all writers contend on a single document under a transaction
- Single writer:
    - Sequencing is a natural consequence of the architecture
    - No sequence contention - throughput scales with batching
    - Challenge: how to elect and ensure a single leader?

---

# Leader election

- A single writer requires exactly one node to hold the write role at any time
- Various approaches exist - lease-based, consensus-based (e.g. Raft, Paxos),
  external coordination (e.g. ZooKeeper, etcd)
- With MongoDB, a lease document is a natural fit
    - CAS on a single document for atomic lease acquisition and renewal

---

# The split-brain problem

- We can never *guarantee* exactly one writer at any instant
- Leader may be cut off due to network partition, or may pause (e.g. garbage collection)
- Meanwhile a new leader is elected - now two nodes believe they are leader
- Any writes from the stale leader must be rejected or ignored

---

# How Raft addresses split-brain

[![bg h:670 left](https://mermaid.ink/img/pako:eNp9U8FymzAQ_ZUdnZqJTG1jStAhM4ae8wEdLiqsDQ1IVBKhrcf_3gVicGzHnJDe29333o4OLNM5MsEs_m5RZfi9lHsj61QBfY00rszKRioHW5AWXogM22swnsD4GkwmMOHgeR6Hl1SNNKUdgn5DA1sBWGHmMIcKZY6Gg0NTz9Tt4vk5FiCbBlUOX0bwYcaST7B4QSB1l9nr1LG_T27c3xDVyNYifAWFrtPmdTTmSn3FjTkpUBoKJMZPlI7DTleV7tBYsI4uR4ND6UnaINv00VsHb32vd_GPq4czmeR7AGkzqk_oknSm4rMYH1cXUz-GdTVvjmWu_BCMQdvWaGEBrZKdNITsKKVuqLpYGtmv8O7q7jBOCzT4azS2gKLcF6Ri1ne-0Hu8Gz7aJpeOfPQkTjqwsZDrToHT0wIZZ3tT5kw40yJnNVFlf2SHvmHKXIE1pkzQb4472VYuZak6Uhk9gB9a16dKo9t9wcROVpZO4-j3FzfdGooATaJb5ZhYB8uhCRMH9oeJVbD2NtG3yF8GfrQOfZ-zv0yEK88PN9FTuF76QbTZ-EfO_g1Tl95TGBz_AwSpNXw?type=png)](https://mermaid.ai/live/edit#pako:eNp9U8FymzAQ_ZUdnZqJTG1jStAhM4ae8wEdLiqsDQ1IVBKhrcf_3gVicGzHnJDe29333o4OLNM5MsEs_m5RZfi9lHsj61QBfY00rszKRioHW5AWXogM22swnsD4GkwmMOHgeR6Hl1SNNKUdgn5DA1sBWGHmMIcKZY6Gg0NTz9Tt4vk5FiCbBlUOX0bwYcaST7B4QSB1l9nr1LG_T27c3xDVyNYifAWFrtPmdTTmSn3FjTkpUBoKJMZPlI7DTleV7tBYsI4uR4ND6UnaINv00VsHb32vd_GPq4czmeR7AGkzqk_oknSm4rMYH1cXUz-GdTVvjmWu_BCMQdvWaGEBrZKdNITsKKVuqLpYGtmv8O7q7jBOCzT4azS2gKLcF6Ri1ne-0Hu8Gz7aJpeOfPQkTjqwsZDrToHT0wIZZ3tT5kw40yJnNVFlf2SHvmHKXIE1pkzQb4472VYuZak6Uhk9gB9a16dKo9t9wcROVpZO4-j3FzfdGooATaJb5ZhYB8uhCRMH9oeJVbD2NtG3yF8GfrQOfZ-zv0yEK88PN9FTuF76QbTZ-EfO_g1Tl95TGBz_AwSpNXw)

---

# The high-water mark

- The commit position/index (high-water mark) marks the last majority-acknowledged event
- Below the high-water mark - stable, committed, safe to read
- Above the high-water mark - in-flight, not yet committed, may be rolled back
- Consumers only read up to the high-water mark - never above it

---

# Raft commit index example

Commit index = 3 (agreed by majority)

| Node                    | idx 1  | idx 2  | idx 3  | idx 4  | idx 5  | idx 6  |
|-------------------------|--------|--------|--------|--------|--------|--------|
| Node A (leader, t1)     | t1 / A | t1 / B | t1 / C | t1 / D | t1 / E | -      |
| Node B (leader, t2)     | t1 / A | t1 / B | t1 / C | t2 / F | t2 / G | t2 / H |
| Node C                  | t1 / A | t1 / B | t1 / C | t1 / D | -      | -      |

- Only the leader advances the commit index (after majority replication)
- The leader propagates the commit index via AppendEntries (heartbeats)

---

# Embracing MongoDB's primitives

- **Single-document CAS**
    - Natural fit for lease-based leader election
    - Acquisition increments epoch
- **Event documents written with replaceOne**
    - Leader writes events into position "slots" above commit position
    - Filter ensures only greater epoch can replace
    - Current leaseholder has greatest epoch
- **Atomic commit position advancement**
    - Only current leaseholder can advance commit position (via CAS on lease document)

---

# Lease acquisition

[![h:500 left](https://mermaid.ink/img/pako:eNqlUjtv2zAQ_isHTg4gq7Ir2RKBBJCapYPdqUshoGDJs0SEIhU-6rqG_3spKwmCulu5EMfvwbuPPBNuBBJKHD4H1BwfJessG1oNcY3MesnlyLSHGpiDfSRDfQs2b2BzC-4mcGd0Zx4jOuPaeATzEy3USUPhh_E9MO9xGD0oZA6B8ecgnfTS6FdRvXx42NEZd-lBavFFY63F11Ewj4szfJeCQkuU6VqSQJqmcLlud7O--Q_9321HnzkMOErtYu9mkBzC6ND6f3BxNLwHqbnFAbVHkUBvlED7WcD9lOis2S1jizUFFzhH52D5PgyLAhaz0f7uJkYai-NEj6bJy337mfXOO2Z9YFIFi7AQYVSSx8nhCU_wIephYJ73N97N5O2hRyViR0epFFj09kQS0lkpCPU2YEIGtAObSnKeHFri-zhsS6ZIBR5YUL4lrb5EWfwX34wZXpXWhK4n9MCUi1W4PsfLR3w7tajjZJ9M0J7Q7erqQeiZ_CK0qtKsXGf5dpOv1mVRRPBE6GpbpEWV5XlWbKptVZXFJSG_r7dmaWStN6uPZRHhcr3JL38Ap-bz_Q?type=png)](https://mermaid.ai/live/edit#pako:eNqlUjtv2zAQ_isHTg4gq7Ir2RKBBJCapYPdqUshoGDJs0SEIhU-6rqG_3spKwmCulu5EMfvwbuPPBNuBBJKHD4H1BwfJessG1oNcY3MesnlyLSHGpiDfSRDfQs2b2BzC-4mcGd0Zx4jOuPaeATzEy3USUPhh_E9MO9xGD0oZA6B8ecgnfTS6FdRvXx42NEZd-lBavFFY63F11Ewj4szfJeCQkuU6VqSQJqmcLlud7O--Q_9321HnzkMOErtYu9mkBzC6ND6f3BxNLwHqbnFAbVHkUBvlED7WcD9lOis2S1jizUFFzhH52D5PgyLAhaz0f7uJkYai-NEj6bJy337mfXOO2Z9YFIFi7AQYVSSx8nhCU_wIephYJ73N97N5O2hRyViR0epFFj09kQS0lkpCPU2YEIGtAObSnKeHFri-zhsS6ZIBR5YUL4lrb5EWfwX34wZXpXWhK4n9MCUi1W4PsfLR3w7tajjZJ9M0J7Q7erqQeiZ_CK0qtKsXGf5dpOv1mVRRPBE6GpbpEWV5XlWbKptVZXFJSG_r7dmaWStN6uPZRHhcr3JL38Ap-bz_Q)

---

# Appending events (example 1)

[![h:500 left](https://mermaid.ink/img/pako:eNqVkl2LnDAUhv_K4dCLLrgyal1nAh0o7a3T3vSmCEuIZzSgiZvE7rTif2_8ZGm3lHoR8Lzn43lPMqDQJSFDS089KUGfJK8MbwsF_uu4cVLIjisHlwi4hYvPhugVNd7V-E81n8Rcq0oXalGVdgT6OxlfyaAhbgno1klDNgDqtKi3KS8yIwZcPPVT0lKypcZb20t0fz7nvqGuQkNdwwV9VvR2gEdZMkjXfAYDvGkcgxjGAMIwXM673wf6RkpDqQVwB522kC4ZOdz7MTOP7YUga5f4a9Z6xZ-5odVi7few2CxX4Pi_gKN_Ak-0dJPW2R0anqWr102d32973Ux4xiuXTW_opQl_25Oar3djw6tUpSf7oMqvXcndjligRy9wB41nsgnTI1tyE7rQbSvdF229IxjHu79tEQOsjCyROdNTgC2Zlk-_OEwVBbqaWipwGlrSlfeNK7BQoy_zr-yb1u1WaXRf1ciuvPFvBPsZeH3Ye9SQKsl81L1yyE5zC2QD3pBFaRy-Oz2ckkOanOIsSQL8gSw7hEnmw1n0kB2PcRKPAf6cZx7CY5aOvwBUAgC-?type=png)](https://mermaid.ai/live/edit#pako:eNqVkl2LnDAUhv_K4dCLLrgyal1nAh0o7a3T3vSmCEuIZzSgiZvE7rTif2_8ZGm3lHoR8Lzn43lPMqDQJSFDS089KUGfJK8MbwsF_uu4cVLIjisHlwi4hYvPhugVNd7V-E81n8Rcq0oXalGVdgT6OxlfyaAhbgno1klDNgDqtKi3KS8yIwZcPPVT0lKypcZb20t0fz7nvqGuQkNdwwV9VvR2gEdZMkjXfAYDvGkcgxjGAMIwXM673wf6RkpDqQVwB522kC4ZOdz7MTOP7YUga5f4a9Z6xZ-5odVi7few2CxX4Pi_gKN_Ak-0dJPW2R0anqWr102d32973Ux4xiuXTW_opQl_25Oar3djw6tUpSf7oMqvXcndjligRy9wB41nsgnTI1tyE7rQbSvdF229IxjHu79tEQOsjCyROdNTgC2Zlk-_OEwVBbqaWipwGlrSlfeNK7BQoy_zr-yb1u1WaXRf1ciuvPFvBPsZeH3Ye9SQKsl81L1yyE5zC2QD3pBFaRy-Oz2ckkOanOIsSQL8gSw7hEnmw1n0kB2PcRKPAf6cZx7CY5aOvwBUAgC-)

---

# Appending events (example 2)

[![h:500 left](https://mermaid.ink/img/pako:eNqdU01r3DAQ_SuD6CEBr1nLdbxrSqC0V2976aUYgrBmvQJbciQ52db4v1fyVz82IUl9MEjzZubNe6OelIojyYjB-w5liZ8FqzRrCgnua5m2ohQtkxYOETADB4eG6IkoXaP0Mpr7YK5kpQo5RaWyCOoBtcvMoEbGUQeArSpPvvwEOtDN7W3uwqoKNbY1K_GLxKse7gTPIJnxGfTwrrYZRDAEEIbh9L_-t5ErJBVwVQKz0CoDyYTIYePajDxMV5ZozLMkDQKeW6HRBNBJ9sg0TsiZbzTz9UgTHoXkju9Hyb-1nNmVeEHcQAUZWcJfZHPP5BCtRGAzd2XlfefacriaJKIX0_msGWWmpEVO-vQ4zFpsWmvAKmD8gTnvoVRNI0ZxFgPAS_OGkWZHonU4541B6z2ain9VxlkHw3B9of6Ribp7RtHXbQB9cQO8_XgWxs39KOxplujDsrQLm-iPXZh4_KcQ9M1C_G5NAlJpwUlmdYcBaVA3zB9J7zMKYk_YYEF8U45H1tW2IIUcXJp7dN-VapZMrbrqRLIjq91WkG4kPL_z9VajdE_wk-qkJVkUjzVI1pOzOyU0fL-_2cfbJN7TNHbBHyRLt2Gcuus0ukl3OxrTISA_x6bbcJcmwy9esFNg?type=png)](https://mermaid.ai/live/edit#pako:eNqdU01r3DAQ_SuD6CEBr1nLdbxrSqC0V2976aUYgrBmvQJbciQ52db4v1fyVz82IUl9MEjzZubNe6OelIojyYjB-w5liZ8FqzRrCgnua5m2ohQtkxYOETADB4eG6IkoXaP0Mpr7YK5kpQo5RaWyCOoBtcvMoEbGUQeArSpPvvwEOtDN7W3uwqoKNbY1K_GLxKse7gTPIJnxGfTwrrYZRDAEEIbh9L_-t5ErJBVwVQKz0CoDyYTIYePajDxMV5ZozLMkDQKeW6HRBNBJ9sg0TsiZbzTz9UgTHoXkju9Hyb-1nNmVeEHcQAUZWcJfZHPP5BCtRGAzd2XlfefacriaJKIX0_msGWWmpEVO-vQ4zFpsWmvAKmD8gTnvoVRNI0ZxFgPAS_OGkWZHonU4541B6z2ain9VxlkHw3B9of6Ribp7RtHXbQB9cQO8_XgWxs39KOxplujDsrQLm-iPXZh4_KcQ9M1C_G5NAlJpwUlmdYcBaVA3zB9J7zMKYk_YYEF8U45H1tW2IIUcXJp7dN-VapZMrbrqRLIjq91WkG4kPL_z9VajdE_wk-qkJVkUjzVI1pOzOyU0fL-_2cfbJN7TNHbBHyRLt2Gcuus0ukl3OxrTISA_x6bbcJcmwy9esFNg)

---

# Benchmark results

| Approach                   | p50 latency | p99 latency | Throughput (500)  | Correctness |
|----------------------------|-------------|-------------|-------------------|-------------|
| Original (w:1, no journal) | 3.4ms       | 3.8ms       | 532 ops/sec       | ❌           |
| Original + majority r/w    | 5.7ms       | 6.6ms       | 530 ops/sec       | ⚠️ partial  |
| bulkWrite + transaction    | 15.3ms      | 21.4ms      | 149 ops/sec       | ✅           |
| **DomainBlocks**           | **22.7ms**  | **25.8ms**  | **4,541 ops/sec** | ✅           |

- Write latency is partly driven by idempotency and optimistic concurrency validation in the write path; optimisable

---

# Designed for MongoDB, designed for correctness

- Embraces MongoDB's primitives - document atomicity, CAS, change streams
- Single writer via lease-based election - global ordering without transactions
- Epoch-filtered writes - split-brain protection using document-level atomicity
- Built for distributed environments - multi-node coordination is a first-class concern
- Addresses fundamental limitations that are often overlooked in MongoDB event store implementations

---

# Demo
