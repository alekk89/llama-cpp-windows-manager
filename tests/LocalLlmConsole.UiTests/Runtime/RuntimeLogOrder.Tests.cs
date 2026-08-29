using System.Reflection;
using System.Windows.Controls;

namespace LocalLlmConsole.UiTests;

public sealed class WpfRuntimeLogOrderTests : WpfUiTestBase
{
    [Fact]
    public async Task RuntimeLogOrderCanForceEitherLiveViewportEdge()
    {
        await RunStaAsync(() =>
        {
            var box = new TextBox();
            var state = new LocalLlmConsole.RuntimeDashboardPageState();
            typeof(LocalLlmConsole.RuntimeDashboardPageState)
                .GetProperty(nameof(LocalLlmConsole.RuntimeDashboardPageState.RuntimeLogBox))!
                .SetValue(state, box);

            state.SetRuntimeLogText("newest\nolder", followTail: false, forceTop: true);
            Assert.Equal(0, box.CaretIndex);

            state.SetRuntimeLogText("oldest\nnewest", followTail: true, forceFollowTail: true);
            Assert.Equal(box.Text.Length, box.CaretIndex);
        });
    }
}
