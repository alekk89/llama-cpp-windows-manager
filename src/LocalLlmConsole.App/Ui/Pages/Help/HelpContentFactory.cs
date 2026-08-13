using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;

namespace LocalLlmConsole;

public static class HelpContentFactory
{
    public const int FirstStepsCount = 4;

    public static readonly string[] SupportedSections =
    [
        "first-steps",
        "overview",
        "models",
        "runtimes",
        "settings",
        "maintenance"
    ];

    public static void AddSection(StackPanel panel, string sectionKey, Action<string> navigate)
    {
        switch (sectionKey)
        {
            case "overview":
                AddOverviewHelp(panel, navigate);
                break;
            case "models":
                AddModelsHelp(panel, navigate);
                break;
            case "runtimes":
                AddRuntimesHelp(panel, navigate);
                break;
            case "settings":
                AddSettingsHelp(panel, navigate);
                break;
            case "maintenance":
                AddMaintenanceHelp(panel, navigate);
                break;
            default:
                AddFirstStepsHelp(panel, navigate);
                break;
        }
    }

    private static void AddFirstStepsHelp(StackPanel panel, Action<string> navigate)
    {
        panel.Children.Add(Text("Start with a prebuilt runtime. Runtime Downloads includes the official prebuilt llama.cpp runtime packages plus selected fork builds such as Atomic TurboQuant CUDA; source builds are advanced options for custom forks, patches, or release targets that do not have a package.", 13, muted: true));

        panel.Children.Add(HelpCard(
            "Step 1",
            "Install an official runtime",
            "Open Runtimes and install a prebuilt llama.cpp runtime for your target: Windows or WSL, then CUDA, CPU, Vulkan, or Intel Arc SYCL. Atomic TurboQuant CUDA Windows/WSL entries are listed beside the official packages.",
            navigate,
            ("Open Runtimes", "runtime-download")));

        panel.Children.Add(HelpCard(
            "Step 2",
            "Download a model",
            "Open Models, search Hugging Face, select the model file you want, and click Download on the row.",
            navigate,
            ("Open Models", "model-download")));

        panel.Children.Add(HelpCard(
            "Step 3",
            "Save a launch profile",
            "Downloaded models are registered automatically. Use Scan Models Folder only if you copied a model manually or it does not appear. Select the model file, adjust its runtime and launch settings, enter a profile name, then click Save New Profile.",
            navigate,
            ("Open Models", "launch-settings")));

        panel.Children.Add(HelpCard(
            "Step 4",
            "Load the model",
            "Open Overview, choose the model and one of its launch profiles, then click Load. To stop a running model, use Unload on its row in Loaded Model Sessions. Multiple models can stay available on their profile ports when the hardware has room.",
            navigate,
            ("Open Overview", "overview-load")));

    }

    private static void AddOverviewHelp(StackPanel panel, Action<string> navigate)
    {
        AddHelpArticle(panel, "What Overview is for", "Overview is the live operations page. It shows the selected model status, loaded sessions, gateway routing status, direct endpoints, runtime metrics, and the active runtime log.");
        AddHelpDefinitionList(panel,
            ("Loaded Model Sessions", "Every running model appears here. Click a model row to switch Model Status to that model. The gateway row shows shared routing status when Overview refreshes."),
            ("Gateway endpoint", "The shared /v1 address used by auto-load clients. It can route a request to whichever configured model was requested."),
            ("Direct endpoint", "The normal per-model llama.cpp address. It stays available on the model's saved port while that model is loaded."),
            ("Runtime metrics", "Tokens and Speculative Tokens aggregate every active slot and plot the latest 60 throughput samples. KV Cache plots total occupancy across slots. Slots shows active capacity, queued requests, and busy decode slots."),
            ("Model status", "Loading, warm, loaded, stopped, or failed. Loading Model or Loaded Model appears separately from Loading Time, and the completed load duration remains visible after readiness. If a model stalls, inspect the runtime log below the metrics."));
        AddHelpBullets(panel,
            "The gateway row shows the shared router endpoint, policy, LAN exposure, and how many direct model sessions are currently loaded.",
            "Use unique direct ports when you want several models loaded at the same time.",
            "Single active gateway policy may unload other sessions before loading the requested model.");
        AddHelpActions(panel, navigate, ("Open Overview", "overview-load"));
    }

    private static void AddModelsHelp(StackPanel panel, Action<string> navigate)
    {
        AddHelpArticle(panel, "How model discovery works", "Scan Models Folder recursively registers GGUF files under the configured models folder. It skips symlinks and junctions, and treats obvious helper files such as mmproj, projector, clip, vision-head, mtp-, draft-, or spec- GGUFs as companions rather than main models.");
        AddHelpDefinitionList(panel,
            ("Downloaded models", "Saved under the app models folder and treated as app-owned. Removing them from the app can remove their downloaded folder."),
            ("Manually copied models", "Put the .gguf anywhere under the models folder, then scan. External imports keep the file where it is."),
            ("Nearby companions", "Nearby means the main model folder, direct child folders, or the parent folder."),
            ("Vision heads", "Auto-detect matches names containing mmproj, projector, clip, vision-head, visual-head, image-head, head-vision, head-visual, head-image, mtp-vision, or vision-mtp."),
            ("MTP and draft heads", "Auto-detect matches names starting with mtp-, draft-, or spec-, or containing -mtp-head, mtp-head, -draft-, or -spec-. Underscores and dots are treated like hyphens."),
            ("Draft model vs MTP head", "Use Draft model with upstream draft-* modes such as draft-mtp. Use MTP head only with Spec type atomic-mtp for compatible Atomic forks that accept --mtp-head."));
        AddHelpBullets(panel,
            "Names that only say assistant are not auto-detected as draft/MTP companions.",
            "For Gemma 4 draft-mtp assistant files, use a name like mtp-gemma-4-31b-it-qat-q4_0-assistant.gguf.",
            "MTP assistant GGUFs are separate from Vision head / --mmproj projector files.");

        AddHelpArticle(panel, "Why some settings appear for every model", "GGUF metadata is useful but incomplete. It often cannot prove whether a model supports vision, a reasoning tag format, RoPE overrides, speculative decoding, flash attention, or a runtime-specific cache mode before llama.cpp actually starts.");
        AddHelpBullets(panel,
            "Use auto when available. It lets llama.cpp and the model template decide.",
            "If a model fails after changing a setting, open Logs or the runtime log in Overview and set the option back to auto, off, none, 0, -1, or blank as appropriate.",
            "Vision needs a model/runtime combination that can process images. That can be embedded in the main GGUF or provided by a matching external projector.",
            "Use the Vision head button only for real mmproj/projector files or embedded/model-bundled vision. MTP assistant heads belong in MTP head, not Vision head.",
            "Embedding, reranker, FIM, MoE, and reasoning models may expose different behavior through the same OpenAI-compatible endpoint.");

        AddHelpArticle(panel, "Saved launch variants", "Saved variants let one model keep several named launch profiles, such as a low-memory profile, a long-context profile, or a vision profile with an explicit projector. Each profile is exposed as a separate model id through the shared gateway.");

        AddHelpArticle(panel, "Launch setting edge cases", "Most launch settings map directly to llama-server flags. Unsupported combinations fail at runtime, so save changes per model and restart the model after editing.");
        AddHelpDefinitionList(panel,
            ("Context size", "0 uses the model GGUF default. Large values need more KV cache memory. Short forms like 196k are normalized by the app."),
            ("GPU layers", "Higher values offload more layers to GPU. If loading fails with memory errors, reduce this first."),
            ("Batch and micro batch", "Higher batch can be faster but uses more memory. Lower micro batch when CUDA or Vulkan runs out of memory."),
            ("K cache and V cache", "Lower precision saves memory but can reduce quality or fail on some runtimes."),
            ("Max tokens", "-1 leaves llama.cpp unlimited for direct serving."),
            ("Reasoning", "auto is safest. Use a specific reasoning format only when a model family needs it."),
            ("RoPE scaling", "Long-context experiments can reduce quality or fail. Leave auto unless the model card recommends a setting."),
            ("Spec type", "Use none unless you have a known-compatible draft, Atomic MTP, or n-gram mode for that runtime. Spec type atomic-mtp emits --mtp-head and is intended for compatible Atomic forks."),
            ("MTP head", "Assistant/head GGUF used by compatible MTP forks. It is separate from Vision head and is not passed as --mmproj."),
            ("Custom params", "Advanced raw llama-server flags appended after the app-generated launch args. Quote values with spaces, for example --n-cpu-moe 999 or --model-draft \"D:\\Models\\draft model.gguf\"."),
            ("Port", "This is the model's direct API port. It must not equal the gateway port."));
        AddHelpActions(panel, navigate, ("Open Models", "models"));
    }

    private static void AddRuntimesHelp(StackPanel panel, Action<string> navigate)
    {
        AddHelpArticle(panel, "Runtime install failures", "Official runtime downloads and source builds run as jobs. If a download, install, or build fails, open Runtime Jobs first and inspect the log before retrying.");
        AddHelpBullets(panel,
            "Network failures usually show as download or GitHub errors. Retry after checking connectivity, proxy, VPN, and antivirus quarantine.",
            "CUDA failures often mean the runtime package does not match the installed driver. Try the Compatibility CUDA package preference when Latest fails.",
            "Build failures usually belong on Windows or WSL Linux setup pages. Check that MSVC, CUDA, Vulkan, SYCL, Ubuntu, and Linux packages are marked ready for the target you are building.",
            "Delete a failed job only after reading its log. The log is the best clue for the missing tool or blocked download.");

        AddHelpArticle(panel, "Adding runtimes manually", "A manual runtime is just a folder that contains llama-server or llama-server.exe. Put the folder under the app runtimes folder, or choose/register that folder if you keep runtimes elsewhere.");
        AddHelpDefinitionList(panel,
            ("Default runtime root", "The app default is <workspace>\\runtimes."),
            ("Windows layout", "Use runtime-name\\llama-server.exe or runtime-name\\bin\\llama-server.exe."),
            ("WSL/Linux layout", "Use runtime-name\\llama-server or runtime-name\\bin\\llama-server."),
            ("Libraries", "Keep companion DLLs, shared libraries, and lib folders beside the executable exactly as the runtime package provides them."),
            ("Metadata marker", "local-llm-runtime.json is optional. It helps name the runtime, infer the backend, and mark app-managed package folders as safe to delete."));

        AddHelpArticle(panel, "How detection classifies a runtime", "The scanner searches for llama-server executables under the runtime root. If the executable is inside a bin folder, the parent folder becomes the runtime folder.");
        AddHelpBullets(panel,
            "Native mode is used for .exe files or metadata with runtime set to native.",
            "WSL mode is used for Linux llama-server files without .exe.",
            "CUDA, Vulkan, and SYCL are inferred from metadata or nearby file names in the runtime folder, bin, or lib. If no marker is found, the runtime is treated as CPU.",
            "A runtime outside the app runtimes folder can be registered, but delete protection is stricter unless it has an app-managed metadata marker.");
        AddHelpActions(panel, navigate, ("Open Runtimes", "runtime-download"), ("Runtime Jobs", "runtime-jobs"));
    }

    private static void AddSettingsHelp(StackPanel panel, Action<string> navigate)
    {
        AddHelpArticle(panel, "Settings values", "Settings are grouped by category and cover app-level defaults and safety preferences. Direct model ports live in each model's launch settings on Models; Settings only exposes the shared gateway port.");
        AddHelpDefinitionList(panel,
            ("Theme", "system, light, or dark. System follows the Windows app theme."),
            ("Cache", "Read-only size display. Clear removes disposable cache files only when no download or runtime build is using them."),
            ("Minimize behavior", "Taskbar only, Tray only, or Tray + taskbar."),
            ("Start with Windows", "Yes registers the app for the current user's Windows startup apps. Fresh installer setups offer this checked by default."),
            ("Auto unload idle min", "Whole number from 0 to 10080. 0 disables idle auto-unload."),
            ("Delete source after build", "Yes or No. Yes removes runtime source folders after successful source builds."),
            ("LAN exposure", "Local only keeps everything on 127.0.0.1. Gateway LAN only exposes just the shared router. Direct models LAN only exposes per-model ports. Gateway + direct LAN exposes both."),
            ("Auto-load gateway", "Yes or No. Yes starts one shared OpenAI-compatible /v1 endpoint for clients that request models by id."),
            ("Gateway port", "Whole number from 1 to 65535. It must be different from any loaded model's direct port."),
            ("Gateway policy", "Prefer keeping loaded models keeps existing sessions running when possible. Single active model unloads other sessions before loading the requested model."),
            ("API key", "Bearer secret for local OpenAI-compatible endpoints. It must be at least 32 non-whitespace characters; blank generates a new key on save."),
            ("Max log file MB", "Whole number clamped from 1 to 4096. Larger values keep more history per log file."));
        AddHelpBullets(panel,
            "Changing gateway settings restarts the gateway after Save Settings.",
            "LAN exposure opens the selected serving endpoints beyond localhost. Use it only on networks you trust.");
        AddHelpActions(panel, navigate, ("Open Settings", "settings"));
    }

    private static void AddMaintenanceHelp(StackPanel panel, Action<string> navigate)
    {
        AddHelpArticle(panel, "Where to look when something breaks", "Use Logs for app, runtime, and job logs. Use Runtime Jobs when a package download or source build fails. Use Overview when a model starts but does not become ready.");
        AddHelpDefinitionList(panel,
            ("App logs", "General app errors, settings saves, config sync problems, and background refresh failures."),
            ("Runtime logs", "llama-server startup output, unsupported flags, memory failures, missing DLLs, and request errors."),
            ("Job logs", "Runtime package downloads, source downloads, git output, compiler output, and setup command output."),
            ("Lifetime", "Persisted prompt and generation token counters by model session."),
            ("Updates", "GitHub release checks and portable app update staging."));
        AddHelpBullets(panel,
            "Active runtime logs are protected from deletion while the model is running.",
            "If a model load fails, the last 20 to 40 lines of the runtime log usually matter most.",
            "If an update fails, keep the current exe and inspect the app log before deleting the staged update files.");
        AddHelpActions(panel, navigate, ("Open Logs", "logs"), ("Open Lifetime", "lifetime"), ("Check Updates", "updates"));
    }

    private static Border HelpCard(string step, string title, string body, Action<string> navigate, params (string Text, string Target)[] actions)
    {
        var container = new Border
        {
            Background = (WpfBrush)WpfApplication.Current.Resources["PanelBackAlt"],
            BorderBrush = (WpfBrush)WpfApplication.Current.Resources["PanelBorder"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var badge = new TextBlock
        {
            Text = step,
            FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["Accent"],
            Margin = new Thickness(0, 2, 12, 0)
        };
        grid.Children.Add(badge);

        var stack = new StackPanel();
        var heading = Text(title, 15, true);
        heading.Margin = new Thickness(0, 0, 0, 4);
        stack.Children.Add(heading);
        var description = Text(body, 13, muted: true);
        description.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(description);

        if (actions.Length > 0)
        {
            var buttons = Bar();
            buttons.Margin = new Thickness(0);
            foreach (var action in actions)
                buttons.Children.Add(Button(action.Text, () => navigate(action.Target)));
            stack.Children.Add(buttons);
        }

        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);
        container.Child = grid;
        return container;
    }

    private static void AddHelpArticle(StackPanel panel, string title, string body)
    {
        var heading = Text(title, 16, true);
        heading.Margin = new Thickness(0, 10, 0, 4);
        panel.Children.Add(heading);

        var paragraph = Text(body, 13, muted: true);
        paragraph.Margin = new Thickness(0, 0, 0, 10);
        panel.Children.Add(paragraph);
    }

    private static void AddHelpBullets(StackPanel panel, params string[] items)
    {
        var list = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var item in items)
        {
            var line = Text("- " + item, 13, muted: true);
            line.Margin = new Thickness(12, 0, 0, 5);
            list.Children.Add(line);
        }
        panel.Children.Add(list);
    }

    private static void AddHelpDefinitionList(StackPanel panel, params (string Term, string Description)[] rows)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        for (var i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var term = Text(rows[i].Term, 13, true);
            term.Margin = new Thickness(0, 0, 12, 7);
            Grid.SetRow(term, i);
            Grid.SetColumn(term, 0);
            grid.Children.Add(term);

            var description = Text(rows[i].Description, 13, muted: true);
            description.Margin = new Thickness(0, 0, 0, 7);
            Grid.SetRow(description, i);
            Grid.SetColumn(description, 1);
            grid.Children.Add(description);
        }

        panel.Children.Add(grid);
    }

    private static void AddHelpActions(StackPanel panel, Action<string> navigate, params (string Text, string Target)[] actions)
    {
        if (actions.Length == 0) return;
        var buttons = Bar();
        foreach (var action in actions)
            buttons.Children.Add(Button(action.Text, () => navigate(action.Target)));
        panel.Children.Add(buttons);
    }

    private static WrapPanel Bar()
        => new() { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };

    private static WpfButton Button(string text, Action click)
    {
        var button = new WpfButton { Content = text };
        button.ToolTip = ButtonToolTip(text);
        ToolTipService.SetShowOnDisabled(button, true);
        button.Click += (_, _) => click();
        return button;
    }

    private static string ButtonToolTip(string text)
        => (text ?? "").Trim() switch
        {
            "Open Runtimes" => "Open runtime source download and build actions.",
            "Open Models" => "Open model search, download, and launch settings.",
            "Open Overview" => "Open the model loading dashboard.",
            "Open Settings" => "Open app preferences.",
            "Runtime Jobs" => "Open Runtimes and focus runtime jobs.",
            "Open Logs" => "Open log inspection.",
            "Open Lifetime" => "Open lifetime token counters.",
            "Check Updates" => "Open app update checks.",
            var label => string.IsNullOrWhiteSpace(label) ? "" : $"Run {label}."
        };

    private static TextBlock Text(string text, int size = 13, bool bold = false, bool muted = false) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = muted ? (WpfBrush)WpfApplication.Current.Resources["TextMuted"] : (WpfBrush)WpfApplication.Current.Resources["TextMain"],
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, size >= 18 ? 10 : 0, 0, size >= 18 ? 10 : 8)
    };
}
