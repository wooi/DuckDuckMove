using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DuckDuckMove.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--broker") return PrivilegeBroker.Run(args);
        if (args.Length == 2 && args[0] == "--service")
        {
            try { using var service = new AvatarService(Guid.ParseExact(args[1], "N").ToString("N")); service.Run(); return 0; } catch { return 1; }
        }
        Localization.Initialize(args.Contains("--render") || args.Contains("--demo"));
        if (args.Contains("--render")) Localization.Select("zh-CN", false);
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) => { MessageBox.Show(Localization.T("程序遇到错误：" + e.Exception.Message), "DuckDuckMove"); e.Handled = true; };
        if (args.Length >= 2 && args[0] == "--render")
        {
            Localization.Validate();
            foreach (var (culture, expected) in new[] { ("zh-CN", "zh-CN"), ("zh-SG", "zh-CN"), ("zh-Hant-HK", "zh-TW"), ("zh-HK", "zh-TW"), ("zh-TW", "zh-TW"), ("en-GB", "en"), ("ja-JP", "ja"), ("es-MX", "es"), ("pt-BR", "pt"), ("pt-PT", "pt"), ("fr-FR", "en") })
                if (Localization.Match(culture) != expected) throw new InvalidDataException("Language fallback failed: " + culture);
            Directory.CreateDirectory(args[1]);
            Localization.UseTestSettings(Path.Combine(args[1], "test-preferences.json"));
            if (!Localization.Select("ja", true)) return 6;
            Localization.Initialize();
            if (Localization.Language != "ja" || Localization.Preference != "ja") return 6;
            File.WriteAllText(Path.Combine(args[1], "test-preferences.json"), "invalid json");
            Localization.Initialize();
            if (Localization.Preference != "system") return 6;
            Localization.Select("zh-CN", false);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            var window = new MainWindow(true);
            bool startupDefault = window.ApplyButton.IsEnabled && window.FileLabel.Text == "内置小鸭 · 默认动图";
            if (args.Length >= 3) window.LoadGif(Path.GetFullPath(args[2]));
            Directory.CreateDirectory(args[1]);
            var visual = (System.Windows.Controls.Grid)window.Content;
            window.Content = null;
            visual.Resources = window.Resources;
            using var host = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("DuckDuckMove hidden UI verification") { Width = 844, Height = 690, WindowStyle = unchecked((int)0x80000000) });
            host.RootVisual = visual;
            Pump(250);
            if (args.Length >= 2)
            {
                string first = PixelHash(window.PreviewImage.Source);
                string second = first;
                for (int attempt = 0; attempt < 8 && second == first; attempt++)
                {
                    Pump(110);
                    second = PixelHash(window.PreviewImage.Source);
                }
                bool gifPlayback = first != "" && second != first;
                bool applyEnabled = window.ApplyButton.IsEnabled;
                window.DefaultButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Pump(500);
                bool defaultPreview = PixelHash(window.PreviewImage.Source) != second && window.StatusLabel.Text.Contains("演示完成");
                window.ApplyButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Pump(500);
                bool applyPreview = window.StatusLabel.Text.Contains("演示完成") && window.ApplyButton.IsEnabled;
                if (window.BuiltInButton.IsEnabled) window.BuiltInButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Pump(250);
                bool returnToBuiltIn = window.ApplyButton.IsEnabled && !window.BuiltInButton.IsEnabled && window.FileLabel.Text == "内置小鸭 · 默认动图";
                File.WriteAllText(Path.Combine(args[1], "ui-checks.json"), System.Text.Json.JsonSerializer.Serialize(new { StartupDefault = startupDefault, GifPlayback = gifPlayback, ApplyEnabled = applyEnabled, DefaultPreview = defaultPreview, ApplyPreview = applyPreview, ReturnToBuiltIn = returnToBuiltIn }));
                if (!startupDefault || !gifPlayback || !applyEnabled || !defaultPreview || !applyPreview || !returnToBuiltIn) return 5;
            }
            window.BuiltInButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (window.StatusLabel.Visibility != Visibility.Collapsed) return 8;
            foreach (var (name, theme, start) in new[] { ("light-login", 1, false), ("dark-login", 2, false), ("light-start", 1, true) })
            {
                window.ApplyTheme(theme); window.SetScene(start);
                visual.Background = (Brush)window.Resources["Page"];
                visual.Measure(new Size(844, 690)); visual.Arrange(new Rect(0, 0, 844, 690)); visual.UpdateLayout();
                Pump(250);
                var bitmap = new RenderTargetBitmap(844, 690, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(args[1], name + ".png")); png.Save(output);
            }
            foreach (string language in Localization.Codes)
            {
                var menu = window.CreateLanguageMenu();
                int languageIndex = Array.IndexOf(Localization.Codes, language);
                ((System.Windows.Controls.MenuItem)menu.Items[languageIndex + 2]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
                if (Localization.Preference != language || menu.Items.Count != 8) return 7;
                window.ApplyTheme(1); window.SetScene(false);
                visual.Background = (Brush)window.Resources["Page"];
                visual.Measure(new Size(844, 690)); visual.Arrange(new Rect(0, 0, 844, 690)); visual.UpdateLayout();
                Pump(100);
                if (window.FileLabel.Text != Localization.T("内置小鸭 · 默认动图") || (window.ApplyButton.Content as System.Windows.Controls.TextBlock)?.Text != Localization.T("应用动图头像")) return 7;
                if (window.PreviewImage.Source == null || !window.ApplyButton.IsEnabled) return 7;
                var bitmap = new RenderTargetBitmap(844, 690, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(args[1], language + ".png")); png.Save(output);
                string languageFolder = Path.Combine(args[1], language);
                Directory.CreateDirectory(languageFolder);
                foreach (var (scene, theme, start) in new[] { ("light-login", 1, false), ("dark-login", 2, false), ("light-start", 1, true) })
                {
                    window.ApplyTheme(theme); window.SetScene(start);
                    visual.Background = (Brush)window.Resources["Page"];
                    visual.Measure(new Size(844, 690)); visual.Arrange(new Rect(0, 0, 844, 690)); visual.UpdateLayout();
                    Pump(250);
                    var sceneBitmap = new RenderTargetBitmap(844, 690, 96, 96, PixelFormats.Pbgra32); sceneBitmap.Render(visual);
                    var scenePng = new PngBitmapEncoder(); scenePng.Frames.Add(BitmapFrame.Create(sceneBitmap));
                    using var sceneOutput = File.Create(Path.Combine(languageFolder, scene + ".png")); scenePng.Save(sceneOutput);
                }
            }
            var systemMenu = window.CreateLanguageMenu();
            ((System.Windows.Controls.MenuItem)systemMenu.Items[0]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            if (Localization.Preference != "system") return 7;
            File.WriteAllText(Path.Combine(args[1], "language-checks.json"), System.Text.Json.JsonSerializer.Serialize(new { Languages = Localization.Codes, Catalog = "complete", Matching = "passed", Persistence = "passed", CorruptPreferences = "passed", LiveSwitch = "passed" }));
            window.Close(); return 0;
        }
        return app.Run(new MainWindow(args.Contains("--demo")));
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static string PixelHash(ImageSource? image)
    {
        if (image is not BitmapSource bitmap) return "";
        int stride = (bitmap.PixelWidth * bitmap.Format.BitsPerPixel + 7) / 8;
        var bytes = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(bytes, stride, 0);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }
}
