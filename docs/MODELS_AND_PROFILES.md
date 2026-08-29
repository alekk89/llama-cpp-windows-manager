# Models and profiles

Models are GGUF inventory records. Launch profiles own runtime choice, endpoint,
context, GPU allocation, sampling, multimodal, and speculative settings. One-shot
overrides do not modify a saved profile unless explicitly saved.

Groups contain profiles, not model records. A group load preflights duplicates,
ports, runtimes, and aggregate VRAM before starting members. Retention controls
automatic idle unload; it does not schedule inference.

Vision projectors, draft models, and MTP heads are discovered automatically only
in the main model's exact folder. Explicit compatible paths may be elsewhere.
See [LAUNCH_SETTINGS_SCHEMA.md](LAUNCH_SETTINGS_SCHEMA.md).
