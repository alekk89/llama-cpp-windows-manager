# Getting started

Install the current Windows x64 installer or extract the portable ZIP. Verify the
download first using [release verification](UPDATES_AND_RELEASE_VERIFICATION.md).

1. In **Runtimes**, install a recommended package or register a folder containing
   `llama-server.exe`. WSL runtimes require a working Ubuntu distribution.
2. In **Models**, download from Hugging Face, scan the configured folder, or use
   **Add model file…** for an external GGUF.
3. Select the model and create a launch profile. Start with automatic context and
   GPU allocation unless the runtime requires explicit values.
4. In **Overview**, select that profile and choose **Load**. Wait for **Loaded**.
5. Copy the displayed endpoint and configured API key into an OpenAI-compatible client.

```text
Base URL: http://127.0.0.1:8081/v1
API key:  the key shown in Settings
Model:    the loaded model or saved gateway route ID
```

Use the shared gateway when one stable client URL should route among profiles.
See [Gateway and networking](GATEWAY_AND_NETWORKING.md).

Use the stars in model, profile, and runtime lists to keep frequent choices at
the top of equivalent selectors. To restore one or more saved profiles whenever
the Manager starts, add them under **Settings → Load profiles on startup** or
use **Load on startup** from a saved profile's context menu.

For the application architecture and a walkthrough of every page, continue with
the [User guide](USER_GUIDE.md).
