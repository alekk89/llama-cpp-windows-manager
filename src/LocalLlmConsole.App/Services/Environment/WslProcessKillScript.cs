namespace LocalLlmConsole.Services;

/// <summary>
/// Builds the one-line bash scripts that stop WSL processes by scanning <c>/proc</c>.
/// The scripts run as <c>bash -lc &lt;script&gt;</c>, so their own command line contains
/// the marker they search for. The stopping shell, its ancestors, and subshells that
/// share its command line are therefore excluded before matching.
/// </summary>
public static class WslProcessKillScript
{
    /// <summary>The scanned process-table root. Tests may substitute a fake directory.</summary>
    public const string ProcRootAssignment = "proc_root=/proc;";

    public enum ScanSource
    {
        CommandLine,
        Environment
    }

    public static string Build(
        IEnumerable<string> assignments,
        ScanSource source,
        string casePattern,
        bool verify)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentException.ThrowIfNullOrWhiteSpace(casePattern);
        var file = source == ScanSource.Environment ? "environ" : "cmdline";
        var separator = source == ScanSource.Environment ? "'\\n'" : "' '";
        var parts = new List<string>(assignments)
        {
            ProcRootAssignment,
            "self_cmd=$(tr '\\0' ' ' < \"$proc_root/$$/cmdline\" 2>/dev/null || true);",
            "self_pids=' ';",
            "p=$$;",
            "while [ -n \"$p\" ] && [ \"$p\" != 0 ]; do",
            "case \"$self_pids\" in *\" $p \"*) break;; esac;",
            "self_pids=\"$self_pids$p \";",
            "[ \"$p\" = 1 ] && break;",
            "p=$(sed -n 's/^PPid:[[:space:]]*//p' \"$proc_root/$p/status\" 2>/dev/null);",
            "done;",
            "llwm_scan() {",
            "remaining=0;",
            $"for entry in \"$proc_root\"/[0-9]*/{file}; do",
            "test -r \"$entry\" || continue;",
            "pid=${entry#\"$proc_root\"/}; pid=${pid%%/*};",
            "case \"$self_pids\" in *\" $pid \"*) continue;; esac;",
            "cmd=$(tr '\\0' ' ' < \"$proc_root/$pid/cmdline\" 2>/dev/null || true);",
            "[ -n \"$self_cmd\" ] && [ \"$cmd\" = \"$self_cmd\" ] && continue;",
            $"text=$(tr '\\0' {separator} < \"$entry\" 2>/dev/null || true);",
            $"case \"$text\" in {casePattern}) remaining=1; if [ \"$1\" = kill ]; then kill \"$pid\" 2>/dev/null || true; fi;; esac;",
            "done;",
            "};",
            "llwm_scan kill;"
        };
        parts.AddRange(verify
            ? ["sleep 0.2;", "llwm_scan check;", "exit \"$remaining\""]
            : ["exit 0"]);
        return string.Join(" ", parts);
    }
}
