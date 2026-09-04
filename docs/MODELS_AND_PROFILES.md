# Models and profiles

Models are GGUF inventory records. Launch profiles own runtime choice, endpoint,
context, GPU allocation, sampling, multimodal, and speculative settings. One-shot
overrides do not modify a saved profile unless explicitly saved.

In **Runtimes**, right-click an installed runtime and choose **Set as default
runtime**. The default is highlighted with an accent edge and bold text. Only
one runtime can be the default: choosing another replaces it, and **Clear
default runtime** or deleting that runtime leaves no default. The preference
persists across restarts.

New models' initial profiles and **Save New Profile** preselect this runtime.
Profiles created through the control API also use it unless an explicit runtime
is supplied. Existing profiles keep their runtime, and **Save as new** preserves
the runtime currently selected in the editor.

Models and Saved Launch Profiles have compact search fields and persistent star
actions. A favorite model or profile moves to the top of equivalent lists and
selectors without changing the current selection. Profile favorites are shared
with the tray menu, so **Add to favorites** is one application-wide preference.
Model files and profiles also expose the same favorite action from their context
menus.

More than one profile for the same GGUF may run at once when every profile uses
a unique direct port and the requested hardware allocation fits. Advanced
**Server** settings include a profile-specific **Host IP**; non-loopback binding
is honored only when the application permits direct-model LAN access.

**Settings > Model > Loading another profile of the same model** defaults to
**Ask each time**. Individual interactive loads offer **Load alongside**,
**Replace existing profiles**, and **Cancel**. Replacement stops only the other
running profiles of that model. Select alongside or replacement in Settings to
use that action without asking. Group loads, startup loads, gateway requests and
control commands retain their own policies. Admission checks and user decisions
complete before replacement stops any session; cancelling or declining a warning
keeps existing sessions running. Replacement stops old profiles
before starting the new one; if its launch later fails, the stopped profiles
remain stopped and the Overview reflects that state.

The **Alias** additional runtime option (`--alias`) also names the saved profile
in gateway discovery. Profiles sharing an alias receive readable `:2`, `:3`,
and later suffixes. Save the profile after editing its alias; use distinct
aliases for stable routing to specific profiles. See
[GATEWAY_AND_NETWORKING.md](GATEWAY_AND_NETWORKING.md) for naming and compatibility.

**Settings → Load profiles on startup** accepts any number of saved
model/profile pairs through a searchable selector. The same state can be toggled
with **Load on startup** on a Saved Launch Profiles row. Startup recovers matching
managed sessions first, then attempts the remaining profiles independently
through normal readiness, port, and memory checks.

Groups contain profiles, not model records. A group load preflights duplicates,
ports, runtimes, and aggregate VRAM before starting members. Retention controls
automatic idle unload; it does not schedule inference.

Vision projectors, draft models, and MTP heads are discovered automatically only
in the main model's exact folder. Explicit compatible paths may be elsewhere.
See [LAUNCH_SETTINGS_SCHEMA.md](LAUNCH_SETTINGS_SCHEMA.md).

For Vulkan runtimes, **Advanced > Performance & Memory > Vulkan allocation block
size (MiB)** accepts an optional allocator block size. **Runtime default**, blank,
or `0` keeps the runtime's inherited behavior; `4096` requests a 4 GiB block.
This changes allocation granularity, not the GPU's memory capacity or VRAM limit.
The runtime can cap it to the device's maximum allocation size. Save it in the
profile; it applies on the next load and to fitting and profile benchmarks.

Use **Fit to available VRAM** with a runtime providing `llama-fit-params` to
propose context/offload changes, then review its estimates and the runtime log.
Benchmark memory peaks provide additional evidence; lower token speed alone does
not establish that context spilled into system RAM.
