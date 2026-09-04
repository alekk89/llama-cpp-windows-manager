# Schema-driven launch settings

Last reviewed: 2026-08-25

## Goal

Render the existing polished launch form and additional runtime-supported settings through one extensible pipeline without making runtime help text the source of application policy.

The implementation deliberately separates four concerns:

1. **UI schema** — labels, sections, editor types, choices, picker behavior, and advanced visibility.
2. **Launch projection** — conversion from `AppSettings` to `RuntimeLaunchRequest` and then to argument tokens.
3. **Runtime capabilities** — options advertised by the selected executable's `--help` output.
4. **Safety policy** — flags owned by the application or unsafe to expose as generic launch fields.

## Rendering flow

`LaunchSettingUiSchema` is the curated schema for settings that need application-level polish or composite behavior. `LaunchSettingsPanelFactory` iterates this schema and creates text fields, dropdowns, and the existing model/head file pickers. The form binder continues to own parsing and cross-field validation, so the migration does not change existing values or validation rules.

After a runtime is selected:

1. The previous runtime's structured values are first materialized back into `CustomParameters`, and its controls are cleared immediately so stale settings are never presented as belonging to the new runtime.
2. `RuntimeLaunchOptionDiscoveryService` invokes that exact executable with `--help`.
3. Native and WSL runtimes are handled separately; stdout and stderr are always combined.
4. `RuntimeLaunchHelpParser` preserves every advertised alias and infers switch, choice, text, file, or directory semantics. It parses only the declaration column as aliases, joins wrapped descriptions, treats bracketed/explicit enumerations as choices, and keeps descriptive default/disabled/range text as a free-form value instead of inventing duplicate choices.
5. `RuntimeLaunchOptionPolicy` removes application-managed, credential-bearing, network/security, utility/action, removed, deprecated, and model-replacement flags.
6. `RuntimeLaunchOptionGroupingService` classifies the safe remainder into stable, ordered sections such as Performance & Memory, Context & Model Behavior, Generation & Sampling, Speculative & Draft, Vision & Multimodal, Server & Slots, and Diagnostics & Output. Unrecognized flags remain visible under Other Runtime Options.
7. `LaunchRuntimeOptionsPanel` renders those groups only in Advanced mode, using the same 28px editors, compact label proportions, visual section language, and two-column field rhythm as the curated form. Raw flags are converted to readable display labels while the exact advertised long alias remains in the tooltip, search index, persistence, and emitted command. Options without an advertised default render a blank text/choice value instead of generic placeholder copy. Choice controls distinguish inheritance (`Inherit (runtime default: X)`) from explicitly setting the same raw value. Discovered positive/negative switch pairs become one tri-state Default/Enabled/Disabled control: Default emits nothing, while Enabled or Disabled emits only the corresponding alias actually advertised by the runtime. Unpaired switches expose only their supported direction. Search results reflow across the two columns so filtered groups do not retain empty cells.

Discovery is generation-safe: each selector change captures the newly selected runtime, cancels the previous scan, and accepts a result only while that runtime is still selected. Loading and discovery errors remain available in Advanced mode; the successful form does not add a redundant discovered-setting count.

Native discovery is cached by runtime identity, executable path, and file modification time. WSL discovery is repeated because a reliable remote executable timestamp is not available locally.

## Discovery diagnostics

Each discovery attempt writes a compact JSON record under `diagnostics/runtime-options` in the app workspace. The record contains the selected runtime identity, executable fingerprint, help exit code, first non-empty banner line, a SHA-256 fingerprint of the help output, parse/render counts, and a stable status such as `success`, `empty-help`, or `unrecognized-help`.

The full help output is deliberately not persisted. This keeps diagnostics useful for identifying runtime-help format changes without turning the diagnostic folder into a copy of arbitrary process output.

## Persistence and import

Additional structured values serialize into the existing `CustomParameters` profile field. This keeps global defaults, per-model profiles, and existing databases backward compatible.

When the runtime advertises `--mmproj-offload` / `--no-mmproj-offload`, the
projector GPU offload switch is available in the additional settings. Default
emits nothing, Enabled requests GPU offload, and Disabled requests CPU projector
execution. This switch does not change the selected projector file or disable
vision.

On profile load, known tokens hydrate their structured runtime editors and unknown tokens remain in the raw fallback field. When the selected runtime changes, structured values are materialized before the old editor set is removed, so settings are not lost. Unsupported aliases remain raw rather than being silently rewritten.

The Runtime Command panel remains visible in Basic and Advanced modes. Its generated portion is editable only as a staging surface: users append one or more flags and select **Apply added flags**. The app rejects changes to the generated prefix, validates the appended tokens against the application-owned argument policy, hydrates matching discovered controls, and preserves safe unknown tokens in **Custom params**.

## Launch parity

`RuntimeLaunchRequestFactory` is shared by real launches and previews. `LlamaCppLaunchValidator` owns launch-request policy and `LlamaCppArgumentBuilder.Build` remains the single token-emission implementation. The preview substitutes placeholders for the model and secret validation only; it does not display the API key, which continues to be passed through `LLAMA_API_KEY`.

Custom arguments are validated before application-owned arguments such as metrics are appended. Model, host, port, credentials, curated performance fields, and other managed flags cannot be overridden through raw parameters.

Host is a curated per-profile Server setting. It defaults from the application
settings, round-trips through `ModelLaunchSettings`, and is projected to
`--host`. The direct-model LAN access policy remains authoritative: a profile
cannot use a non-loopback host while direct LAN serving is disabled.

## Adding a polished setting

The advanced Vulkan **Allocation block size (MiB)** field uses the typed
`vulkanAllocationBlockSizeMiB` profile value. Zero (shown as **Runtime default**)
or a blank editor leaves the inherited runtime environment unchanged. A positive
integer is converted to bytes with 64-bit arithmetic and passed as
`GGML_VK_SUBALLOCATION_BLOCK_SIZE` only to Vulkan child processes. Native servers,
WSL servers, profile benchmarks, and `llama-fit-params` use the same conversion.
It is an environment setting, not a command-line argument. Older profiles default
to zero; the value is preserved when switching runtimes but inactive outside Vulkan.

1. Add the persistent value to `AppSettings` and, when model-specific, `ModelLaunchSettings`.
2. Add a `LaunchSettingUiDefinition` to `LaunchSettingUiSchema` with the appropriate section and editor metadata.
3. Add binder parsing, application, and cross-field validation when the value is not a plain string.
4. Add validation to `LlamaCppLaunchValidator`, command projection to `LlamaCppArgumentBuilder`, and mark every owned alias in `RuntimeLaunchOptionPolicy`.
5. Add round-trip, validation, and argument-emission tests.

Settings discovered at runtime require none of these steps unless they need richer validation or composite behavior than help metadata can express.

App-shell preferences such as the `showOverview*` and `showModelsHuggingFace`
fields do not belong in
this launch schema or in `ModelLaunchSettings`: they do not affect
`llama-server` arguments and must not create profile variants. Implement those
through `SettingsPageDefinitionService`, `AppSettingsUpdateService`, explicit
`StateStore.Settings` key mappings, and the owning WPF page state. See
`docs/DEVELOPMENT.md` for the complete app-level UI preference checklist.

## Verification

Tests cover:

- exact alias preservation and choice inference;
- deterministic runtime-option grouping with a lossless fallback;
- readable runtime-option labels with exact raw-flag search and emission;
- safe/app-managed filtering;
- rejection of model, port, and credential overrides;
- shared preview/launch argument generation;
- curated schema validity and uniqueness;
- the existing application architecture and full regression suite.
