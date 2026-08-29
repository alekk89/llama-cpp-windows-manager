using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfBinding = System.Windows.Data.Binding;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfProgressBar = System.Windows.Controls.ProgressBar;
using WpfTextBox = System.Windows.Controls.TextBox;
namespace LocalLlmConsole;

public partial class MainWindow
{
    private static void SetModelGridColumnSizing(DataGrid grid)
    {
        if (grid.Columns.Count < 5) return;
        grid.Columns[0].MinWidth = 110;
        grid.Columns[0].Width = new DataGridLength(2.5, DataGridLengthUnitType.Star);
        grid.Columns[1].MinWidth = 72;
        grid.Columns[1].Width = new DataGridLength(.6, DataGridLengthUnitType.Star);
        grid.Columns[2].MinWidth = 98;
        grid.Columns[2].Width = new DataGridLength(118);
        grid.Columns[3].MinWidth = 86;
        grid.Columns[3].Width = new DataGridLength(.75, DataGridLengthUnitType.Star);
        grid.Columns[4].MinWidth = 72;
        grid.Columns[4].Width = new DataGridLength(86);
    }

    private static void SetRuntimeGridColumnSizing(DataGrid grid)
    {
        if (grid.Columns.Count < 5) return;
        grid.Columns[0].MinWidth = 90;
        grid.Columns[0].Width = new DataGridLength(1.35, DataGridLengthUnitType.Star);
        grid.Columns[1].MinWidth = 64;
        grid.Columns[1].Width = new DataGridLength(.5, DataGridLengthUnitType.Star);
        grid.Columns[2].MinWidth = 58;
        grid.Columns[2].Width = new DataGridLength(.45, DataGridLengthUnitType.Star);
        grid.Columns[3].MinWidth = 140;
        grid.Columns[3].Width = new DataGridLength(3.25, DataGridLengthUnitType.Star);
        grid.Columns[4].MinWidth = 72;
        grid.Columns[4].Width = new DataGridLength(78);
    }

    private static void SetRuntimeBuildGridColumnSizing(DataGrid grid)
    {
        if (grid.Columns.Count < 9) return;
        grid.Columns[0].MinWidth = 110;
        grid.Columns[0].Width = new DataGridLength(1.25, DataGridLengthUnitType.Star);
        grid.Columns[1].MinWidth = 68;
        grid.Columns[1].Width = new DataGridLength(.55, DataGridLengthUnitType.Star);
        grid.Columns[2].MinWidth = 80;
        grid.Columns[2].Width = new DataGridLength(.65, DataGridLengthUnitType.Star);
        grid.Columns[3].MinWidth = 110;
        grid.Columns[3].Width = new DataGridLength(1.2, DataGridLengthUnitType.Star);
        grid.Columns[4].MinWidth = 120;
        grid.Columns[4].Width = new DataGridLength(2.35, DataGridLengthUnitType.Star);
        for (var column = 5; column <= 8; column++)
        {
            grid.Columns[column].MinWidth = 86;
            grid.Columns[column].Width = new DataGridLength(104);
        }
    }

    private static void SetRuntimeMetricsGridColumnSizing(DataGrid grid)
    {
        if (grid.Columns.Count < 5) return;
        grid.Columns[0].MinWidth = 150;
        grid.Columns[0].Width = new DataGridLength(1.6, DataGridLengthUnitType.Star);
        grid.Columns[1].MinWidth = 140;
        grid.Columns[1].Width = new DataGridLength(2.2, DataGridLengthUnitType.Star);
        grid.Columns[2].MinWidth = 90;
        grid.Columns[2].Width = new DataGridLength(.8, DataGridLengthUnitType.Star);
        grid.Columns[3].MinWidth = 76;
        grid.Columns[3].Width = new DataGridLength(.55, DataGridLengthUnitType.Star);
        grid.Columns[4].MinWidth = 180;
        grid.Columns[4].Width = new DataGridLength(2.8, DataGridLengthUnitType.Star);
    }

    private static void SetHfSearchGridColumnSizing(DataGrid grid)
    {
        if (grid.Columns.Count < 8) return;
        grid.Columns[0].MinWidth = 88;
        grid.Columns[0].Width = new DataGridLength(.95, DataGridLengthUnitType.Star);
        grid.Columns[1].MinWidth = 132;
        grid.Columns[1].Width = new DataGridLength(1.85, DataGridLengthUnitType.Star);
        grid.Columns[2].MinWidth = 56;
        grid.Columns[2].Width = new DataGridLength(64);
        grid.Columns[3].MinWidth = 64;
        grid.Columns[3].Width = new DataGridLength(76);
        grid.Columns[4].MinWidth = 72;
        grid.Columns[4].Width = new DataGridLength(82);
        grid.Columns[5].MinWidth = 96;
        grid.Columns[5].Width = new DataGridLength(1.05, DataGridLengthUnitType.Star);
        grid.Columns[6].MinWidth = 96;
        grid.Columns[6].Width = new DataGridLength(104);
        grid.Columns[7].MinWidth = 66;
        grid.Columns[7].Width = new DataGridLength(74);
    }

    private static void SetDownloadHistoryGridColumnSizing(DataGrid grid)
    {
        if (grid.Columns.Count < 10) return;
        grid.Columns[0].MinWidth = 76;
        grid.Columns[0].Width = new DataGridLength(90);
        grid.Columns[1].MinWidth = 110;
        grid.Columns[1].Width = new DataGridLength(1.55, DataGridLengthUnitType.Star);
        grid.Columns[2].MinWidth = 88;
        grid.Columns[2].Width = new DataGridLength(112);
        grid.Columns[3].MinWidth = 64;
        grid.Columns[3].Width = new DataGridLength(76);
        grid.Columns[4].MinWidth = 90;
        grid.Columns[4].Width = new DataGridLength(112);
        grid.Columns[5].MinWidth = 82;
        grid.Columns[5].Width = new DataGridLength(1.2, DataGridLengthUnitType.Star);
        grid.Columns[6].MinWidth = 70;
        grid.Columns[6].Width = new DataGridLength(76);
        grid.Columns[7].MinWidth = 70;
        grid.Columns[7].Width = new DataGridLength(76);
        grid.Columns[8].MinWidth = 70;
        grid.Columns[8].Width = new DataGridLength(76);
        grid.Columns[9].MinWidth = 72;
        grid.Columns[9].Width = new DataGridLength(82);
    }
}
