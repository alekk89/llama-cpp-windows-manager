# Models and profiles

Models are GGUF inventory records. Launch profiles own runtime choice, endpoint,
context, GPU allocation, sampling, multimodal, and speculative settings. One-shot
overrides do not modify a saved profile unless explicitly saved.

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
