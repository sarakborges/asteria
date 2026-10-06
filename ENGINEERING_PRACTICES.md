# Asteria Engineering Practices

This document is normative together with `AGENTS.md` and `ARCHITECTURE.md`.
The goal is to make the correct design the easiest design to extend.

## 0. Non-negotiable quality gate

Code is accepted only when both behavior and design meet the project standard. "It works" is necessary but not sufficient.

- Code that knowingly violates these practices does not enter `main` unless the user explicitly authorizes an exception.
- Temporary shortcuts, duplicated ownership, god objects, hidden coupling, leaky boundaries, unbounded work, stale-async hazards, and unjustified architectural debt must be corrected before a change is considered complete.
- Prefer fixing structural problems while the affected system is still small.
- Review the complete changed design, not only the new lines. If the new feature exposes an existing architectural violation in the touched area, reduce or remove that violation rather than building on it.

### SOLID and related principles

Use SOLID as a mandatory design review lens where it is applicable:

- **Single Responsibility Principle:** a type/module should own one coherent responsibility and one primary reason to change.
- **Open/Closed Principle:** prefer stable extension points and data/composition for real variants; do not require unrelated edits across the system for every new variant.
- **Liskov Substitution Principle:** abstractions/subtypes must preserve the contract callers rely on; do not use inheritance when variants are not genuinely substitutable.
- **Interface Segregation Principle:** consumers depend only on the capabilities they need; avoid broad interfaces and dependency bags.
- **Dependency Inversion Principle:** stable domain policy must not depend directly on volatile framework/platform details; dependencies point toward domain contracts and explicit boundaries.

These principles do not require maximum abstraction. Avoid interface-for-every-class designs, speculative factories, deep inheritance, generic repositories, or extra layers without a concrete invariant or boundary. Simplicity, cohesion, explicit ownership, composition, encapsulation, separation of concerns, and testability remain the deciding criteria.

## 1. Responsibility and ownership

- Every module/type has one primary reason to change.
- Every mutable domain fact has one owner.
- Keep domain, orchestration, rendering, persistence, serialization, UI/transport and background execution behind explicit boundaries.
- Keep related invariant + state + transitions + validation + invalidation together.
- Prefer composition over manager/god objects.
- Public APIs expose capabilities, not internal collections.

## 2. Modeling and mutation

- Separate immutable authored definitions from mutable runtime instances.
- Encapsulate mutable collections.
- Use strong domain types when they prevent accidental mixing or centralize validation.
- Make invalid states hard to represent.
- Prefer explicit lifecycle/state-machine transitions to scattered booleans.
- Do not publish/update derived state when the authoritative value did not change.
- Use monotonic revisions when they simplify stale-result rejection, snapshots, cache invalidation or publication.

## 3. APIs

- Keep APIs narrow and intent-revealing.
- Avoid `Manager`, `Utils`, `Context` or `Service` dumping grounds.
- Avoid boolean flags when separate operations/enums express intent better.
- Add abstractions only for a real invariant/boundary, not visual similarity.
- Prefer concrete implementation, observed duplication, extracted invariant, then stable abstraction.
- Orchestration should validate boundary input, call the owner, and publish/map output; it should not reproduce domain logic.

## 4. Side effects and failures

- Separate pure calculation/policy from engine mutation, I/O, logging and transport where practical.
- Query-looking APIs must not hide writes.
- Fail fast on impossible programmer invariants.
- Treat authored JSON, files, browser messages and external/plugin data as untrusted boundaries and validate them.
- Never silently swallow failures.
- Setup and cleanup must mirror one another.
- Atomic publication is preferred when partial application would expose an invalid state.

## 5. Concurrency and workers

- Concurrency is primarily an ownership problem.
- Prefer task-local state, immutable snapshots and bounded message/result transfer.
- Do not add concurrency without a real workload reason.
- Async results that can become stale MUST carry a revision/generation/request identity.
- Bound worker count, pending tasks, result queues and memory.
- Stale work is discarded, never forced into current state.
- Long-lived irrelevant work should support cancellation or cheap abandonment.

## 6. Change-driven work and caches

- Prefer change-driven work over every-frame recomputation.
- Every cache documents or makes evident: key, owner, validity rule, invalidation, lifetime and memory bound.
- Derived metadata lives near the owner of its source.
- Precompute immutable/rare relationships when they are used repeatedly.
- Use specialized structures when semantics justify them: deduplicated queues, priority queues, sparse occupancy indexes, stable ordered sets and bounded caches.
- Test specialized collections independently.

## 7. Determinism and time

- Explicitly sort whenever output/behavior depends on order.
- Stable tie-breakers are mandatory.
- Gameplay/simulation uses logical world ticks where appropriate; wall-clock time is for realtime presentation/measurement.
- Tests must not depend on uncontrolled wall clock, filesystem order, global randomness, network timing or task completion order.

## 8. Performance

- Correctness first; measure before micro-optimizing.
- Remove unnecessary work before tuning the remaining work.
- Optimize where the cost is owned.
- Avoid repeated allocation, copying, serialization and string churn in hot paths.
- Prefer compact contiguous runtime data.
- Batch expensive engine/GPU boundary calls when semantics allow.
- Long world operations must be incremental/budgeted when they can affect frame latency.
- Diagnostic logging/metrics are observational; they never become correctness state.

## 9. Code clarity

- Functions operate at one abstraction level.
- Split by named responsibility, not line count.
- Use guard clauses to reduce deep nesting.
- Name by domain intent; avoid vague `Process`/`Handle`/`Helper` names when a precise concept exists.
- Constants include semantic names and units.
- Avoid hidden control flow, ambient service lookup, reflection-based runtime magic and global mutable state unless a concrete requirement justifies them.

## 10. Testing

- Test behavior and invariants, not private layout.
- Reproducible bugs receive regression tests.
- Keep Core tests framework-light and fast.
- Use integration tests at real boundaries such as Godot adapters, serialization and WebUI transport.
- Queue/cache/scheduler/state-machine tests directly validate uniqueness, ordering, invalidation, transitions and failure behavior.
- Fixtures stay small and domain-focused.

## 11. Mandatory refactoring triggers

A refactor is required when a touched area exhibits one or more of these:

- multiple components can mutate the same fact;
- a class/file repeatedly changes for unrelated features;
- a dependency set is unrelated/cohesion is low;
- consumers bypass the intended owner;
- the same invariant or queue/cache mechanic is copied;
- lifecycle is scattered across booleans/conditionals;
- strings/integers have become an undocumented protocol;
- async work can publish stale results;
- queues/caches/task registries can grow without bounds;
- expensive work repeats despite unchanged inputs;
- framework code leaks into Core;
- engine-agnostic code is trapped in the Godot adapter;
- a helper accumulates many flags/exceptions;
- performance fixes require cross-layer hacks because ownership is unclear.

Do not solve excessive dependencies by creating an everything-context.

## 12. Mandatory review checklist

Before accepting a coherent change block, verify. Any relevant failure blocks acceptance:

1. What invariant does each changed/new component own?
2. Is there exactly one mutable owner for each fact?
3. Are dependencies narrow and cohesive?
4. Are Core/Godot/WebUI boundaries respected?
5. Is the abstraction based on a real shared invariant?
6. Is input validated at the correct boundary?
7. Are invalid states prevented where practical?
8. Do changes flow through explicit mutation/lifecycle APIs?
9. Are side effects obvious?
10. Can async results become stale, and are they revision-checked?
11. Are workers, queues and memory bounded?
12. Does each cache have validity/invalidation/lifetime rules?
13. Can repeated work be change-driven or precomputed?
14. Is observable ordering deterministic?
15. Is hot-path copying/allocation justified?
16. Does every growing collection have a retention strategy?
17. Is the public surface minimal?
18. Are failures meaningful and visible?
19. Do tests protect the changed invariants?
20. Is the resulting design easier to extend without duplicating ownership?
21. Does each type/module satisfy single responsibility and have a cohesive reason to change?
22. Are extension points/data variants preferable to scattered edits for likely repeated variants?
23. Do abstractions preserve their caller contracts and represent real substitutable concepts?
24. Are consumers depending only on the narrow capabilities they need?
25. Does dependency direction point toward stable domain policy rather than framework/platform details?
26. Would a competent reviewer describe any part of this change as a shortcut, workaround, god object, hidden coupling, or avoidable debt?

If the answer to any relevant quality question is no—or to question 26 is yes—the change is not complete and must be refactored before acceptance.
