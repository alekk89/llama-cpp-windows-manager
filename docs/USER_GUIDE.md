# User Guide

llama.cpp Windows Manager installs or registers llama.cpp runtimes, catalogs
GGUF models, saves launch profiles, supervises multiple model servers, and
provides direct or shared OpenAI-compatible endpoints.

## First model

1. In **Runtimes**, install a prebuilt package matching Windows or WSL and your
   CPU/GPU backend. CUDA is for NVIDIA, Vulkan commonly serves AMD and other
   Vulkan GPUs, and SYCL targets Intel Arc.
2. In **Models**, download a GGUF from Hugging Face. Alternatively copy `.gguf`
   files under the configured models folder and scan, or register a file kept
   elsewhere.
3. Select the model and runtime, adjust launch settings, and save a named
   profile. Profiles let one model keep several ports, contexts, or backends.
4. In **Overview**, choose the model/profile and select **Load**. Use the shown
   direct `/v1` endpoint, or enable the shared gateway in **Settings**.

The model API key in **Settings** is required for inference. It is separate from
the Manager's process-local control token used automatically by `llwmctl`.

## Runtime trust

Managed package downloads validate available size and SHA-256 information.
New managed installs also record a manifest of installed-file hashes. Select a
runtime row to see provider, repository, release, assets, source, checksum and
signature status, install time, backend, and version. Use **Verify** to compare
the current files with that manifest.

- **Hash verified** means every recorded installed file still matches.
- **Local files modified** or **files missing** requires inspection or reinstall.
- **Managed runtime; re-verification unavailable** is a legacy install without a
  file manifest; reinstall it to establish one.
- **Unverified custom runtime** is a manually supplied runtime trusted by you.

## Profiles, groups, and loading

A profile owns its runtime, port, context, GPU allocation, server options, and
optional vision/draft/MTP companions. A model can have multiple profiles, but
only one profile for that physical model can run at a time.

Groups coordinate profile loading. The Manager preflights runtimes, ports,
duplicate models, and aggregate VRAM, then rolls back members if loading fails.
Retention can inherit the global idle timeout, pin profiles, or define a group
timeout. Priority affects automatic idle eviction only.

If a GGUF is removed, scanning keeps its registration and profiles and marks it
**Missing**. Restore the file or delete the registration explicitly.

Right-click table rows for the same safe actions available in their buttons.
Model files offer **Open Folder**, **Save New Profile**, and **Delete**; saved
profiles offer **Load**, group assignment, and **Remove**. Runtime, log, and
Metrics rows also expose their applicable row actions this way.

## Metrics

**Metrics** shows token activity by local calendar day, cache reuse, average
prompt and generation throughput, request counts when the runtime exposes them,
and a model breakdown with each model's tracked-token share. Its activity
calendar provides the latest 24 calendar months and can visualize total, input,
output, cached tokens, or requests.
Day boxes stay fixed in size, so resizing the window reveals more or less
history. Dates before daily tracking began and future dates are subdued and cannot
be selected, so preserved legacy totals are never presented as invented daily
usage. Point to a tracked day for evaluated input, cached input, generated
output, totals, and cache hit rate.

Click a tracked day to inspect that exact date and click it again to clear the
selection. **Ctrl+click** toggles individual dates; **Shift+click** selects the
continuous range from the previous anchor, which makes selecting a week or a
longer period quick. **Ctrl+Shift+click** adds a continuous range. The summary
cards and model breakdown follow the selected dates. Choosing **1D / 7D / 30D /
All** clears the custom dates and restores that rolling period. **All** is the
default. Model, launch-profile, and runtime filters apply to both the calendar
intensity and selected totals. Cache hit rate is unavailable when the selected
runtime does not expose a cache counter.

**Active days**, **average per active day**, and **peak day** describe the
selected period. Prompt and generation rates divide tokens by llama.cpp's
reported active processing seconds, not elapsed wall time. Request totals are
shown only when the runtime exports a compatible completed-request counter;
unsupported counters display as unavailable rather than an estimated zero.

Existing lifetime totals are preserved during upgrade. Accurate daily history
begins with the first token activity after this feature is installed, so older
totals are never assigned to invented dates. Resetting a model removes both its
legacy total and its daily history.

## Endpoints and automation

Direct endpoints serve one loaded model. The gateway exposes one stable address
and routes model IDs returned by `GET /v1/models` to saved profiles. LAN access
is opt-in and never exposes the Manager control API.

Double-click a loaded-session or gateway row, or select its endpoint link, to
open the endpoint report. Its text and table cells are selectable, and the top
actions copy the endpoint, a complete secret-free report, or the model API key.
The key is copied only by its dedicated action and is never included in **Copy
report**.

Use `llwmctl` to operate the running Manager:

```powershell
llwmctl status
llwmctl capabilities
llwmctl self
llwmctl models list
llwmctl runtimes list
llwmctl load <model> --profile <profile> --wait
llwmctl sessions inspect <session>
llwmctl metrics usage --range month
llwmctl metrics usage --date 2026-08-18 --date 2026-08-20
```

Read [Control API](CONTROL_API.md) and [AGENTS.md](../AGENTS.md) before building
automation around consequential operations.

## Troubleshooting

- Search the in-app **Help** page first; it includes setup, API, backend,
  download, memory, port, and authentication topics.
- Inspect the live runtime log when loading stalls or a server exits.
- Try a CPU runtime to separate a model issue from GPU driver/backend issues.
- Confirm the profile port is unused and the selected runtime executable still
  exists.
- A `401` from model inference normally means the model API key is missing or
  wrong. `llwmctl` uses a different automatically discovered credential.
- On **Logs**, select **Create Diagnostics Bundle** to collect versions,
  inventory, session state, runtime trust details, WSL/GPU summaries, and up to
  ten sanitized recent log tails. The ZIP excludes keys, control tokens,
  database contents, launch arguments, and full model/runtime paths. Redaction
  is best effort, so review the archive before sharing it.
- For support, use the issue template and attach the reviewed diagnostics bundle
  or only sanitized logs. See [SUPPORT.md](../SUPPORT.md).
