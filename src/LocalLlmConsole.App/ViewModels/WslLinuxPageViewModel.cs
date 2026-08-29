using System.Collections.ObjectModel;

namespace LocalLlmConsole.ViewModels;

public sealed class WslLinuxPageViewModel
{
    public ObservableCollection<WslDistroRow> Rows { get; } = new();

    public void ReplaceDistroRows(WslEnvironmentReport report, string selectedDistroName)
    {
        Rows.Clear();
        foreach (var distro in report.Distros.OrderByDescending(distro => distro.IsUbuntu).ThenBy(distro => distro.Name, StringComparer.OrdinalIgnoreCase))
        {
            var selected = distro.Name.Equals(selectedDistroName, StringComparison.OrdinalIgnoreCase);
            var notes = distro.IsUbuntu
                ? Loc.T("Wsl.Distro.RecommendedNotes")
                : Loc.T("Wsl.Distro.SelectableNotes");
            Rows.Add(new WslDistroRow
            {
                IsDefault = distro.IsDefault,
                Name = distro.Name,
                State = distro.State,
                WslVersion = string.IsNullOrWhiteSpace(distro.Version) ? "" : Loc.T("Wsl.Distro.Version", distro.Version),
                Notes = notes,
                ActionLabel = selected ? Loc.T("Wsl.Distro.Selected") : Loc.T("Wsl.Distro.Use"),
                ActionToolTip = selected ? Loc.T("Wsl.Distro.SelectedTooltip") : Loc.T("Wsl.Distro.UseTooltip"),
                CanSelect = !selected,
                IsUbuntu = distro.IsUbuntu
            });
        }

        if (report.Distros.Count == 0)
        {
            Rows.Add(new WslDistroRow
            {
                Name = Loc.T("Wsl.Distro.NoneDetected"),
                State = report.WslExeFound ? Loc.T("Wsl.Distro.Missing") : Loc.T("Wsl.Distro.WslMissing"),
                Notes = report.RecommendedAction,
                ActionToolTip = Loc.T("Wsl.Distro.InstallTooltip")
            });
        }
    }
}
