using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.UiTests;

public sealed class ProfileImportLayoutTests : WpfUiTestBase
{
    [Theory]
    [InlineData("en", 850, 760, 100)]
    [InlineData("de", 680, 560, 125)]
    public async Task LongWarningsRemainScrollableAndActionsStayInsideTheResizedDialog(string culture, int width, int height, int scale)
    {
        await RunStaAsync(() =>
        {
            LocalLlmConsole.Localization.Loc.LoadLanguage(culture);
            LocalLlmConsole.ApplicationThemeService.Apply("dark");
            ApplicationUiScaleService.Apply(scale);
            var owner = new Window { Width = 200, Height = 100, ShowInTaskbar = false };
            owner.Show();
            var runtime = new RuntimeRecord("preview-runtime", "Official llama.cpp CUDA b11576", RuntimeMode.Native,
                RuntimeBackend.Cuda, Environment.ProcessPath!, "{}", DateTimeOffset.UtcNow);
            var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(TestWorkspace), runtime.Id);
            var model = new ModelRecord("target", "Target", "target.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
            var bundle = new ProfileBundle(1, new("Source", "source.gguf"), [new("Long context", ProfileBundleCodec.PortableSettings(settings))]);
            var warnings = Enumerable.Range(1, 30).Select(index => $"Warning {index}: review runtime, companion paths, GPU layout and port mappings before saving.").ToArray();
            var preview = new ProfileImportPreview(1, model.Id, [new(0, "Long context", null, settings, null, warnings, [])]);
            Exception? failure = null;
            owner.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = Application.Current.Windows.OfType<Window>().Single(window => window.Owner == owner);
                try
                {
                    dialog.Width = width; dialog.Height = height; dialog.UpdateLayout();
                    var visibleText = VisualDescendants<TextBlock>(dialog).Select(text => text.Text).ToArray();
                    Assert.Contains(runtime.Name, visibleText);
                    Assert.Contains(LocalLlmConsole.Localization.Loc.T("Profiles.Transfer.KeepBoth"), visibleText);
                    Assert.DoesNotContain(visibleText, text => text.StartsWith("RuntimeRecord {") || text.StartsWith("ConflictItem {"));
                    var summary = VisualDescendants<TextBlock>(dialog).Single(text => text.Text.Contains("Warning 30:"));
                    var scroller = VisualDescendants<ScrollViewer>(dialog).Single(scroll => ReferenceEquals(scroll.Content, summary));
                    Assert.True(scroller.ExtentHeight > scroller.ViewportHeight,
                        $"Warnings are clipped: extent {scroller.ExtentHeight}, viewport {scroller.ViewportHeight}.");
                    scroller.ScrollToEnd(); dialog.UpdateLayout();
                    Assert.True(scroller.VerticalOffset > 0);
                    foreach (var button in VisualDescendants<Button>(dialog).Where(button => Equals(button.Content, LocalLlmConsole.Localization.Loc.T("Profiles.Transfer.Apply")) || Equals(button.Content, LocalLlmConsole.Localization.Loc.T("Common.Cancel"))))
                    {
                        var bounds = button.TransformToAncestor(dialog).TransformBounds(new Rect(button.RenderSize));
                        Assert.True(bounds.Bottom <= dialog.ActualHeight && bounds.Right <= dialog.ActualWidth,
                            $"Action is outside the dialog: {bounds}, window {dialog.ActualWidth}×{dialog.ActualHeight}.");
                    }
                    if (Environment.GetEnvironmentVariable("LLWM_PROFILE_QA_VISUALS") is { Length: > 0 } output)
                    {
                        Directory.CreateDirectory(output);
                        var root = (FrameworkElement)dialog.Content;
                        var size = root.TransformToAncestor(dialog).TransformBounds(new Rect(root.RenderSize)).Size;
                        var drawing = new DrawingVisual();
                        using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(root), null, new Rect(size));
                        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(drawing);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var file = File.Create(Path.Combine(output, $"profile-import-{culture}-{scale}.png")); encoder.Save(file);
                    }
                }
                catch (Exception ex) { failure = ex; }
                finally { dialog.Close(); }
            }), DispatcherPriority.ApplicationIdle);
            LocalLlmConsole.ProfileImportDialog.Show(owner, model, bundle, preview, [runtime], _ => Task.FromResult(preview));
            owner.Close(); ApplicationUiScaleService.Apply(100); LocalLlmConsole.Localization.Loc.LoadLanguage("en");
            if (failure is not null) throw failure;
        });
    }
}
