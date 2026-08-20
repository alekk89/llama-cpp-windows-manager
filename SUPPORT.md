# Support

## Start here

- Read the [User guide](docs/USER_GUIDE.md) and use the searchable **Help** page
  inside the application.
- For automation, run `llwmctl status`, `llwmctl capabilities`, and
  `llwmctl operations list`, then read [Control API](docs/CONTROL_API.md).
- Download only from [GitHub Releases](https://github.com/alekk89/llama-cpp-windows-manager/releases/latest)
  and verify the matching `.sha256` file.

## Ask for help or report a bug

Open a GitHub issue using the relevant template. Include the application and
Windows versions, distribution type, runtime mode/backend/source, model
filename, reproduction steps, and actual behavior. The **Logs** page can create
a diagnostics bundle with safe inventory and sanitized log tails. Review the ZIP
before attaching it because log redaction is best effort. State whether
restarting the Manager changes the result.

Never post API keys, Manager control tokens, private URLs, or unredacted personal
paths. Use the private process in [SECURITY.md](SECURITY.md) for vulnerabilities.

This is a community project. Response times are best effort, and support cannot
guarantee compatibility with every llama.cpp fork, driver, GPU, WSL setup, or
third-party model package.
