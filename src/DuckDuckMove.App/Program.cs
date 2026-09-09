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
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) => { MessageBox.Show("程序遇到错误：" + e.Exception.Message, "DuckDuckMove"); e.Handled = true; };
        if (args.Length >= 2 && args[0] == "--render")
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            var window = new MainWindow(true);
            bool startupDefault = window.ApplyButton.IsEnabled && window.FileLabel.Text == "内置小鸭 · 默认动图";
            if (args.Length >= 3) window.LoadGif(Path.GetFullPath(args[2]));
            Directory.CreateDirectory(args[1]);
            var visual = (System.Windows.Controls.Grid)window.Content;
            window.Content = null;
            visual.Resources = window.Resources;
            using var host = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("DuckDuckMove hidden UI verification") { Width = 804, Height = 710, WindowStyle = unchecked((int)0x80000000) });
            host.RootVisual = visual;
            Pump(250);
            if (args.Length >= 2)
            {
                string first = PixelHash(window.PreviewImage.Source);
                Pump(110);
                string second = PixelHash(window.PreviewImage.Source);
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
                window.RefreshStartButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Pump(100);
                bool refreshDemo = window.StatusLabel.Text.Contains("未刷新实际开始菜单") && window.RefreshStartButton.IsEnabled && window.AutoRefreshStart.IsChecked == true;
                File.WriteAllText(Path.Combine(args[1], "ui-checks.json"), System.Text.Json.JsonSerializer.Serialize(new { StartupDefault = startupDefault, GifPlayback = gifPlayback, ApplyEnabled = applyEnabled, DefaultPreview = defaultPreview, ApplyPreview = applyPreview, ReturnToBuiltIn = returnToBuiltIn, RefreshDemo = refreshDemo }));
                if (!startupDefault || !gifPlayback || !applyEnabled || !defaultPreview || !applyPreview || !returnToBuiltIn || !refreshDemo) return 5;
            }
            foreach (var (name, theme, start) in new[] { ("light-login", 1, false), ("dark-login", 2, false), ("light-start", 1, true) })
            {
                window.ApplyTheme(theme); window.SetScene(start);
                visual.Background = (Brush)window.Resources["Page"];
                visual.Measure(new Size(804, 710)); visual.Arrange(new Rect(0, 0, 804, 710)); visual.UpdateLayout();
                Pump(250);
                var bitmap = new RenderTargetBitmap(804, 710, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(args[1], name + ".png")); png.Save(output);
            }
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
