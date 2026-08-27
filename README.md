# llama.cpp Windows Manager

A Windows desktop app I built to make `llama.cpp` easier to manage. It handles
runtimes, GGUF models, launch profiles, and supervised OpenAI compatible model
endpoints on native Windows or Ubuntu/WSL.

[![Latest release](https://img.shields.io/github/v/release/alekk89/llama-cpp-windows-manager?display_name=tag&sort=semver)](https://github.com/alekk89/llama-cpp-windows-manager/releases/latest)
[![Build/test/publish](https://github.com/alekk89/llama-cpp-windows-manager/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/alekk89/llama-cpp-windows-manager/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Platform: Windows x64](https://img.shields.io/badge/platform-Windows%20x64-0078D4?logo=windows)](#install)

> This is an unofficial community project. It is not affiliated with or endorsed
> by the llama.cpp project.

[Download](https://github.com/alekk89/llama-cpp-windows-manager/releases/latest)
· [User guide](docs/USER_GUIDE.md)
· [Getting started](docs/USER_GUIDE.md#first-model)
· [Documentation](#documentation-and-support)
· [Buy me a coffee](https://buymeacoffee.com/alekkson)

![llama.cpp Windows Manager product tour](docs/images/llama-cpp-windows-manager-demo.gif)

## What it does

* Installs or registers Windows and WSL `llama.cpp` runtimes.
* Downloads, imports, scans, and organizes GGUF models.
* Saves multiple launch profiles for each model.
* Starts, stops, and switches favourite saved profiles from a themed tray menu.
* Runs and supervises several model servers on separate ports.
* Provides direct endpoints and an optional shared gateway.
* Tracks runtime health, logs, token usage, hardware metrics, and GPU energy.
* Supports CPU, CUDA, Vulkan, and Intel Arc SYCL backends.
* Provides authenticated local automation through `llwmctl`.

## Why use it?

I made this for people who want the control of `llama.cpp` without having to
manage every command, process, model, and port by hand.

If you only want a simple chat interface, a chat focused app may be a better fit.
If you want control over runtimes, profiles, multiple model servers, networking,
and monitoring, this is what the Manager is built for.

## Install

Download the Windows x64 installer or portable ZIP from
[GitHub Releases](https://github.com/alekk89/llama-cpp-windows-manager/releases/latest).
Both versions include everything needed to run the app. You do not need to
install .NET separately.

* **Installer:** adds Start Menu integration, app updates, and optional startup
  with Windows.
* **Portable ZIP:** runs without an installer and normally keeps its data in
  `data` beside `LlamaCppWindowsManager.exe`.
* **Requirements:** Windows 10 or 11 x64. GPU and WSL runtimes also need the
  matching drivers and environment.

Verify the matching `.sha256` file before running a download. See
[Signing releases](docs/SIGNING.md) for trust and signature details.

## Quick start

1. Install or register a runtime in **Runtimes**.
2. Download or import a GGUF in **Models**.
3. Choose a runtime and save a launch profile.
4. Select the model and profile in **Overview**, then choose **Load**.
5. Connect an OpenAI compatible client to the displayed `/v1` endpoint.

The [User guide](docs/USER_GUIDE.md) explains how the app fits together and how
to use every page. It also covers profiles, groups, networking, metrics,
accessibility, automation, and troubleshooting.

## Security defaults

* The Manager control API is authenticated and available only on loopback.
* Model serving starts on loopback with API key authentication enabled.
* LAN access must be enabled manually and always requires a strong API key.
* Runtime processes are supervised and checked during shutdown.
* Managed downloads and stable updates stop when required integrity checks fail.
* Installer repair, updates, and uninstall preserve application data by default.

More detail is available in the [User guide](docs/USER_GUIDE.md) and the
[Security policy](SECURITY.md).

## Documentation and support

* [User guide](docs/USER_GUIDE.md): how the app works and how to use each page.
* [`llwmctl` and Control API](docs/CONTROL_API.md): local automation.
* [Support](SUPPORT.md): troubleshooting and bug reports.
* [Development](docs/DEVELOPMENT.md) and [Architecture](docs/ARCHITECTURE.md):
  contributor workflow and internal code boundaries.
* [`build-installer.ps1`](scripts/build-installer.ps1): builds the Windows
  installer after publishing the application.

If something is not working, open a
[bug report](https://github.com/alekk89/llama-cpp-windows-manager/issues/new/choose).
Please report security problems privately as explained in
[SECURITY.md](SECURITY.md).

Released under the [MIT License](LICENSE). Bundled dependencies keep their own
licenses. See [third party notices](THIRD-PARTY-NOTICES.md).
