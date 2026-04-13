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
    - The event log is the source of truth
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

[![bg h:670 left](https://mermaid.ink/img/pako:eNp9Uk1TgzAQ_Ss7e5VWaKEtOThT8OwPcLhE2LYoJBgSq3b6313oUPulnMi-97Jv32aHuS4IBbb07kjl9FjKtZF1poC_Rhpb5mUjlYUlyBaemAzLazA5gsk1mB7BNFMHWGlLoD_IwFIAVZRbKqAiWZDxwJKp4WmgLkcPD4mArSlZMkADkN4CkhEjfK_M387q6Y36DTuNdC3BPSiyW23eDqPoK2bicXO2zFz6bEpDLYwYhg0x_4WkhZXRdRfW4Kq3a7qcWwsf-uj6Ljjxx6P2EC9BdaGcU07a_5VbRz3reBrQZavfKH5lZ2HwWK7uR3NKbqVhZMXJbHvVxYpaKyv6Zx-GXg-GR0zSat2T0MO1KQsU1jjysOaS7I6468QZ2g3VlKHg34JW0lU2w0ztWcZv61nrelAa7dYbFCtZtXxyTSHt8JiPFFIcVKqdsihm8_4KFDv8RBHHY38x8cP5LAwmiygKPPxCEcyjcRT7YehHs3gex4to7-F339QfM2syC6aL2J9OpkEY7n8AFSgFWQ?type=png)](https://mermaid.ai/live/edit#pako:eNp9Uk1TgzAQ_Ss7e5VWaKEtOThT8OwPcLhE2LYoJBgSq3b6313oUPulnMi-97Jv32aHuS4IBbb07kjl9FjKtZF1poC_Rhpb5mUjlYUlyBaemAzLazA5gsk1mB7BNFMHWGlLoD_IwFIAVZRbKqAiWZDxwJKp4WmgLkcPD4mArSlZMkADkN4CkhEjfK_M387q6Y36DTuNdC3BPSiyW23eDqPoK2bicXO2zFz6bEpDLYwYhg0x_4WkhZXRdRfW4Kq3a7qcWwsf-uj6Ljjxx6P2EC9BdaGcU07a_5VbRz3reBrQZavfKH5lZ2HwWK7uR3NKbqVhZMXJbHvVxYpaKyv6Zx-GXg-GR0zSat2T0MO1KQsU1jjysOaS7I6468QZ2g3VlKHg34JW0lU2w0ztWcZv61nrelAa7dYbFCtZtXxyTSHt8JiPFFIcVKqdsihm8_4KFDv8RBHHY38x8cP5LAwmiygKPPxCEcyjcRT7YehHs3gex4to7-F339QfM2syC6aL2J9OpkEY7n8AFSgFWQ)

---

# The high-water mark

- The commit position/index (high-water mark) marks the last majority-acknowledged event
- Below the high-water mark - stable, committed, safe to read
- Above the high-water mark - in-flight, not yet committed, may be rolled back
- Consumers only read up to the high-water mark - never above it

---

# Raft commit index example

| Node                    | idx 1  | idx 2  | idx 3  | * | idx 4  | idx 5  | idx 6  |
|-------------------------|--------|--------|--------|---|--------|--------|--------|
| **Node A** (leader, t1) | t1 / A | t1 / B | t1 / C | * | t1 / D | t1 / E | -      |
| **Node B** (leader, t2) | t1 / A | t1 / B | t1 / C | * | t2 / F | t2 / G | t2 / H |
| **Node C**              | t1 / A | t1 / B | t1 / C | * | t1 / D | -      | -      |

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

# Event writes and position advancement (1)

[![h:500 left](https://mermaid.ink/img/pako:eNqVkk2L1EAQhv9KUXhwIRvyYZKdBgdELx5m9OJFAtJ0apKGpDvbH-7okP9uZ5IMi66IOTSk662q563qCwrdEDK09OhJCfogeWv4UCsI38iNk0KOXDk4psAtHIMa0hei2S2a_Rk9zMGDVq2u1RJV2hHo72RCJoOeuCWg8ygN2Qho1KLbujxTpgy4ePSzaEnZpNlW9pje7_eHUFC3saGx54I-KXp9gW-yYVCsegYXeNU7BhlMEcRxvJx3vzcMhZSGRgvgDkZtoVgUB7gPba481gtB1i73L1nzij9xQ6vFLsxhsdmswNl_Aaf_BJ5p6SytszdoeJKuWye1f7vNdTMRGE9c9t7QcxNh23P0sO7GxiepmkD2TjVfxoa7GTGsQXsj6GMgrTE4qPHGm10BZ9pAbsnNDoQeBuk-axuMwTTd_W2YGGFrZIPMGU8RDmQGPv_iZc6o0XU0UI1z04ZO3PeuxlpNIS08tq9aD1um0b7tkJ14H54K-iv3-r5vt4ZUQ-a99sohq3bltQiyC56R5Ulc7vIqKXdZVTyUSZJH-ANZ-lDGeZW-yXZVHo6yyKcIf177JnFV7aqkSJKiyNOkSovpF5MdBxE?type=png)](https://mermaid.ai/live/edit#pako:eNqVkk2L1EAQhv9KUXhwIRvyYZKdBgdELx5m9OJFAtJ0apKGpDvbH-7okP9uZ5IMi66IOTSk662q563qCwrdEDK09OhJCfogeWv4UCsI38iNk0KOXDk4psAtHIMa0hei2S2a_Rk9zMGDVq2u1RJV2hHo72RCJoOeuCWg8ygN2Qho1KLbujxTpgy4ePSzaEnZpNlW9pje7_eHUFC3saGx54I-KXp9gW-yYVCsegYXeNU7BhlMEcRxvJx3vzcMhZSGRgvgDkZtoVgUB7gPba481gtB1i73L1nzij9xQ6vFLsxhsdmswNl_Aaf_BJ5p6SytszdoeJKuWye1f7vNdTMRGE9c9t7QcxNh23P0sO7GxiepmkD2TjVfxoa7GTGsQXsj6GMgrTE4qPHGm10BZ9pAbsnNDoQeBuk-axuMwTTd_W2YGGFrZIPMGU8RDmQGPv_iZc6o0XU0UI1z04ZO3PeuxlpNIS08tq9aD1um0b7tkJ14H54K-iv3-r5vt4ZUQ-a99sohq3bltQiyC56R5Ulc7vIqKXdZVTyUSZJH-ANZ-lDGeZW-yXZVHo6yyKcIf177JnFV7aqkSJKiyNOkSovpF5MdBxE)

---

# Event writes and position advancement (2)

[![h:500 left](https://mermaid.ink/img/pako:eNqVUk2L2zAQ_SvD0EMXvMYfGzsWZaG0lx6c9tJLMRQhTxKBLXn10U0b_N8rJXEKTZelOgik997MvMccUeiekKGlJ09K0EfJd4aPnYJwJm6cFHLiysEmB25hE9iQ_wMtrmhxi7YRbLXa6U6dUaUdgf5BJigZDMR7MgnQpMU-lj-TNsX942MbYL1LDU0DF_RZ0dsjfJc9g9WFz-AIbwbHIIc5gTRNz_fd341CIaWh1wK4g0lbWJ0ZLdyHNqc5rBeCrH1xSEtAh0kasgl4xZ-5oRtmzoCLJx9JZ8liq_hjC2LD9lLSplup-mDsveq_Tj130WFQa28EfQpGOwwBdHi1m5_8RbPBuCUXAxB6HKX7om3IBeb57sbalsvBL-Ne5sj_K97i1XhjtnSQ1ll4lm5_8f1u2YhlmvwaNCa4M7JH5oynBEcyI49PPEZFh25PI3UYI-hpy_3gOuzUHGRhrb5pPS5Ko_1uj2zLh5A3-lOKl02-_hpSYck-aK8csnVZnYogO-IBWZmlVVPWWdUU9WpdZVmZ4E9k-bpKyzp_KJq6DFe1KucEf536ZmldN3X20KzXWVbUdZ3PvwHQnACU?type=png)](https://mermaid.ai/live/edit#pako:eNqVUk2L2zAQ_SvD0EMXvMYfGzsWZaG0lx6c9tJLMRQhTxKBLXn10U0b_N8rJXEKTZelOgik997MvMccUeiekKGlJ09K0EfJd4aPnYJwJm6cFHLiysEmB25hE9iQ_wMtrmhxi7YRbLXa6U6dUaUdgf5BJigZDMR7MgnQpMU-lj-TNsX942MbYL1LDU0DF_RZ0dsjfJc9g9WFz-AIbwbHIIc5gTRNz_fd341CIaWh1wK4g0lbWJ0ZLdyHNqc5rBeCrH1xSEtAh0kasgl4xZ-5oRtmzoCLJx9JZ8liq_hjC2LD9lLSplup-mDsveq_Tj130WFQa28EfQpGOwwBdHi1m5_8RbPBuCUXAxB6HKX7om3IBeb57sbalsvBL-Ne5sj_K97i1XhjtnSQ1ll4lm5_8f1u2YhlmvwaNCa4M7JH5oynBEcyI49PPEZFh25PI3UYI-hpy_3gOuzUHGRhrb5pPS5Ko_1uj2zLh5A3-lOKl02-_hpSYck-aK8csnVZnYogO-IBWZmlVVPWWdUU9WpdZVmZ4E9k-bpKyzp_KJq6DFe1KucEf536ZmldN3X20KzXWVbUdZ3PvwHQnACU)

---

# Benchmark results

| Approach                   | p50 latency | p99 latency | Throughput (500)  | Correctness |
|----------------------------|-------------|-------------|-------------------|-------------|
| Original (w:1, no journal) | 3.4ms       | 3.8ms       | 532 ops/sec       | ❌           |
| Original + majority r/w    | 5.7ms       | 6.6ms       | 530 ops/sec       | ⚠️ partial  |
| bulkWrite + transaction    | 15.3ms      | 21.4ms      | 149 ops/sec       | ✅           |
| **DomainBlocks**           | **22.7ms**  | **25.8ms**  | **4,541 ops/sec** | ✅           |

---

# Designed for MongoDB, designed for correctness

- Embraces MongoDB's primitives - document atomicity, CAS, change streams
- Single writer via lease-based election - global ordering without transaction contention on every write
- Epoch-filtered writes - split-brain protection using document-level atomicity
- Built for distributed environments - multi-node coordination is a first-class concern
- Addresses fundamental limitations that are often overlooked in MongoDB event store implementations

---

# Demo
