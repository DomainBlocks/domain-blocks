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
    - Successul writes are visible to subsequent reads
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

- Raft uses "terms" - each election increments the term
- A write is only valid if it carries the current term
- A stale leader's writes are rejected - its term is outdated
- Majority quorum for commits ensures no split-brain write can be acknowledged

---

# Raft split-brain example

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

[![h:500 left](https://mermaid.ink/img/pako:eNqVkk2L1EAQhv9KUXhwIRvyYZKdBgdELx5m9OJFAtJ0apKGpDvbH-7okP9uZ5IMi66IOTSk662q563qCwrdEDK09OhJCfogeWv4UCsI38iNk0KOXDk4psAtHIMa0hei2S2a_Rk9zMGDVq2u1RJV2hHo72RCJoOeuCWg8ygN2Qho1KLbujxTpgy4ePSzaEnZpNlW9pje7_eHUFC3saGx54I-KXp9gW-yYVCsegYXeNU7BhlMEcRxvJx3vzcMhZSGRgvgDkZtoVgUB7gPba481gtB1i73L1nzij9xQ6vFLsxhsdmswNl_Aaf_BJ5p6SytszdoeJKuWye1f7vNdTMRGE9c9t7QcxNh23P0sO7GxiepmkD2TjVfxoa7GTGsQXsj6GMgrTE4qPHGm10BZ9pAbsnNDoQeBuk-axuMwTTd_W2YGGFrZIPMGU8RDmQGPv_iZc6o0XU0UI1z04ZO3PeuxlpNIS08tq9aD1um0b7tkJ14H54K-iv3-r5vt4ZUQ-a99sohq3bltQiyC56R5Ulc7vIqKXdZVTyUSZJH-ANZ-lDGeZW-yXZVHo6yyKcIf177JnFV7aqkSJKiyNOkSovpF5MdBxE?type=png)](https://mermaid.ai/live/edit#pako:eNqVkk2L1EAQhv9KUXhwIRvyYZKdBgdELx5m9OJFAtJ0apKGpDvbH-7okP9uZ5IMi66IOTSk662q563qCwrdEDK09OhJCfogeWv4UCsI38iNk0KOXDk4psAtHIMa0hei2S2a_Rk9zMGDVq2u1RJV2hHo72RCJoOeuCWg8ygN2Qho1KLbujxTpgy4ePSzaEnZpNlW9pje7_eHUFC3saGx54I-KXp9gW-yYVCsegYXeNU7BhlMEcRxvJx3vzcMhZSGRgvgDkZtoVgUB7gPba481gtB1i73L1nzij9xQ6vFLsxhsdmswNl_Aaf_BJ5p6SytszdoeJKuWye1f7vNdTMRGE9c9t7QcxNh23P0sO7GxiepmkD2TjVfxoa7GTGsQXsj6GMgrTE4qPHGm10BZ9pAbsnNDoQeBuk-axuMwTTd_W2YGGFrZIPMGU8RDmQGPv_iZc6o0XU0UI1z04ZO3PeuxlpNIS08tq9aD1um0b7tkJ14H54K-iv3-r5vt4ZUQ-a99sohq3bltQiyC56R5Ulc7vIqKXdZVTyUSZJH-ANZ-lDGeZW-yXZVHo6yyKcIf177JnFV7aqkSJKiyNOkSovpF5MdBxE)

---

# Appending events (example 2)

[![h:500 left](https://mermaid.ink/img/pako:eNqlU02L2zAQ_SuD6GEXHBPLzToRZaG0lx6S9tJLMRQhTRKBLXn1sZs2-L9Xsp3sttntB_XBYM8bvTfvjY5EGImEEYd3AbXA94rvLG9rDfHpuPVKqI5rD5sCuINNREPxTJWeq_Syuk7FtdE7U-uxqo1HMPdoYyeDBrlEmwF2RuzT8SNoQ2e3t-tYNrvcYtdwgR81Xh3hq5IMFhOewRFeNZ5BAX0GeZ6P7-tfieJB2oA0AriHzjhYjIg1zCLNoMMFIdC5F0U6BDx0yqLLIGj-wC2OyElvMelNSJdvlZZR71stP3eS-7PwmsSBajKohJ_ErpOSTXEWArOJlYu7EGklXI0W0YvpUteEcmPTyU76_Djce2w778Ab4PKex-xBmLZVgzmnACBZ89uRIp8JVuCHp5NNwRTnGWNEDn2KauT4ZFxMEPr--iKELVdNeMHYv1sE-sdFSFuAB-Xi-A_K7yen3px296SmeLISo47_84P-sx-PCkhGdlZJwrwNmJEWbcvTJzmmjpr4PbZYk0QqcctD42tS6z62xSv4xZj21GlN2O0J2_Im7ggJg-7p1p__WtTxQr4zQXvClsVqOISwIzkQVixo_np1syrni3JFq7LMyDfCqnleVvF3VdxUyyUtaZ-R7wPrPF9Wi_4HFRVZcA?type=png)](https://mermaid.ai/live/edit#pako:eNqlU02L2zAQ_SuD6GEXHBPLzToRZaG0lx6S9tJLMRQhTRKBLXn1sZs2-L9Xsp3sttntB_XBYM8bvTfvjY5EGImEEYd3AbXA94rvLG9rDfHpuPVKqI5rD5sCuINNREPxTJWeq_Syuk7FtdE7U-uxqo1HMPdoYyeDBrlEmwF2RuzT8SNoQ2e3t-tYNrvcYtdwgR81Xh3hq5IMFhOewRFeNZ5BAX0GeZ6P7-tfieJB2oA0AriHzjhYjIg1zCLNoMMFIdC5F0U6BDx0yqLLIGj-wC2OyElvMelNSJdvlZZR71stP3eS-7PwmsSBajKohJ_ErpOSTXEWArOJlYu7EGklXI0W0YvpUteEcmPTyU76_Djce2w778Ab4PKex-xBmLZVgzmnACBZ89uRIp8JVuCHp5NNwRTnGWNEDn2KauT4ZFxMEPr--iKELVdNeMHYv1sE-sdFSFuAB-Xi-A_K7yen3px296SmeLISo47_84P-sx-PCkhGdlZJwrwNmJEWbcvTJzmmjpr4PbZYk0QqcctD42tS6z62xSv4xZj21GlN2O0J2_Im7ggJg-7p1p__WtTxQr4zQXvClsVqOISwIzkQVixo_np1syrni3JFq7LMyDfCqnleVvF3VdxUyyUtaZ-R7wPrPF9Wi_4HFRVZcA)

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
