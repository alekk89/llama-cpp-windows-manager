# Runtime management

The Manager supports native Windows and Ubuntu/WSL `llama.cpp` runtimes using
CPU, CUDA, Vulkan, or Intel Arc SYCL. Prefer managed packages or source builds
because the Manager records their source identity and a local installed-file
hash baseline. **Verify** detects changed, missing, and unexpected files against
that baseline; it does not authenticate the runtime publisher. A manually
registered runtime has no Manager-recorded baseline and is clearly marked
unverified.

Package downloads are streamed through the configured expected-size boundary,
then validated against release metadata and a required SHA-256 value before
installation. A mismatched, oversized, or unverifiable asset is deleted and is
not registered.

The curated repository list includes upstream `llama.cpp` plus compatible
Atomic TurboQuant, `ik_llama.cpp`, and TheTom TurboQuant `llama-server` forks.
TheTom release packages are offered only for its published CUDA Windows,
Vulkan WSL, and CPU WSL assets. Provider, repository, release, and asset details
remain visible after installation; local checksum verification does not imply
that a third-party publisher is endorsed or code-signed.

Use **Runtimes** interactively and `llwmctl runtimes list` for automation. Source
builds follow check, download, then build; they are never started as unmanaged
`llama-server` processes. Every loaded process remains under the canonical
supervisor and verified-stop path.

Exact operations and live schemas are authoritative in [CONTROL_API.md](CONTROL_API.md).
