# ADR 0001: serving-engine boundary timing

Status: accepted, deferred until implementation of a second serving engine.

The project will not introduce a speculative adapter while llama.cpp is the only
engine. When vLLM or another engine enters active implementation, the first change
must route llama.cpp through an `IServingEngineAdapter`-style boundary with
behavioural parity before adding the second adapter.

Engine-owned responsibilities are validation, launch-plan command construction,
start/readiness/endpoint discovery, capability reporting, telemetry normalization,
verified stop, and failure classification. Common session state, path safety,
supervision, authentication, persistence, diagnostics, and gateway policy remain
canonical and engine-neutral.

Engine selection belongs in the composition root. UI state is capability-driven;
controllers, persistence, gateway policy, and `MainWindow` must not accumulate
engine-name conditionals. A fake adapter must prove lifecycle parity first.
