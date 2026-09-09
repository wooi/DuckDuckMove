using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DuckDuckMove.Core;
using Microsoft.Win32;
using XamlAnimatedGif;

namespace DuckDuckMove.App;

public partial class MainWindow : Window
{
    private readonly string sid = WindowsIdentity.GetCurrent().User!.Value;
    private string? selectedPath;
    private bool busy, startPreview;
    private bool builtIn;
    private int theme;
    private readonly bool demo;
    private string? previewCopy;
    private string statusSource = "";
    private GifInfo? selectedInfo;
    private void SetStatus(string source)
    {
        statusSource = source;
        StatusLabel.Text = Localization.T(source);
        StatusLabel.Visibility = source is "" or "内置小鸭已就绪，可直接应用，也可以选择自己的 GIF。" or "已准备好。应用时会先备份原头像。" or "预览已更新 · 当前为演示模式。"
            ? Visibility.Collapsed : Visibility.Visible;
    }
    public MainWindow() : this(false) { }
    internal MainWindow(bool demo)
    {
        this.demo = demo;
        InitializeComponent();
        Localization.Changed += ApplyLanguage;
        ApplyLanguage();
        AnimationBehavior.AddErrorHandler(SourceImage, (_, e) => { selectedPath = null; SetStatus("动图播放失败：" + e.Exception.Message); RefreshActions(); });
        AnimationBehavior.AddErrorHandler(PreviewImage, (_, e) => { SetStatus("预览失败：" + e.Exception.Message); });
        AccountLabel.Text = demo ? Localization.T("当前 Windows 账户") + " · DuckDuck" : WindowsIdentity.GetCurrent().Name;
        PreviewName.Text = demo ? "DuckDuck" : Environment.UserName;
        SystemLabel.Text = "Windows " + (Environment.OSVersion.Version.Build >= 22000 ? "11" : "10") + " · " + Environment.OSVersion.Version.Build;
        ApplyTheme(); SetScene(false); LoadBuiltInGif(); RefreshActions();
        if (demo) SetStatus("界面演示模式 · 不修改系统。");
        else if (!Environment.Is64BitOperatingSystem || Environment.OSVersion.Version.Build < 22000)
        {
            SetStatus("此版本仅支持 Windows 11 64 位系统。");
            ChooseButton.IsEnabled = DefaultButton.IsEnabled = ApplyButton.IsEnabled = false;
        }
        Closed += (_, _) => { Localization.Changed -= ApplyLanguage; AnimationBehavior.SetSourceUri(SourceImage, null); AnimationBehavior.SetSourceUri(PreviewImage, null); TryDeletePreview(); };
        Closing += (_, e) => { if (busy) { e.Cancel = true; SetStatus("正在完成头像操作，请稍候再关闭窗口。"); } };
    }
    private void TryDeletePreview() { if (previewCopy != null) { try { File.Delete(previewCopy); } catch (IOException) { } } }
    private void ChooseGif(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = Localization.T("GIF 动图 (*.gif)|*.gif"), CheckFileExists = true, Multiselect = false, Title = Localization.T("选择你的动图头像") };
        if (picker.ShowDialog(this) == true) LoadGif(picker.FileName);
    }
    private void DragGif(object sender, DragEventArgs e) { e.Effects = !busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void DropGif(object sender, DragEventArgs e)
    {
        if (!busy && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length == 1) LoadGif(files[0]);
    }
    internal void LoadGif(string path)
    {
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > GifInfo.MaxBytes) throw new InvalidDataException("GIF 不能超过 20 MB。");
            var data = new byte[checked((int)input.Length)]; input.ReadExactly(data);
            LoadGifData(data, Path.GetFileName(path), false);
        }
        catch (Exception ex) { SetStatus("无法载入：" + ex.Message); }
    }
    private void UseBuiltInGif(object sender, RoutedEventArgs e) => LoadBuiltInGif();
    private void LoadBuiltInGif()
    {
        try
        {
            using var source = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("DuckDuckMove.DefaultGif")
                ?? throw new FileNotFoundException("内置动图缺失。");
            using var buffer = new MemoryStream(); source.CopyTo(buffer);
            LoadGifData(buffer.ToArray(), "内置小鸭 · 默认动图", true);
        }
        catch (Exception ex) { SetStatus("无法载入内置动图：" + ex.Message); }
    }
    private void LoadGifData(byte[] data, string displayName, bool isBuiltIn)
    {
            var info = GifInfo.Inspect(data);
            // Ask Windows to decode as well; malformed image streams must fail before any elevation.
            using var check = new MemoryStream(data);
            var decoder = new GifBitmapDecoder(check, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            foreach (var frame in decoder.Frames) frame.Freeze();
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DuckDuckMove", "Preview");
            Directory.CreateDirectory(folder);
            string copy = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".gif");
            File.WriteAllBytes(copy, data);
            AnimationBehavior.SetSourceUri(SourceImage, null); AnimationBehavior.SetSourceUri(PreviewImage, null);
            TryDeletePreview(); previewCopy = copy; selectedPath = copy;
            AnimationBehavior.SetSourceUri(SourceImage, new Uri(copy));
            AnimationBehavior.SetSourceUri(PreviewImage, new Uri(copy));
            SourcePlaceholder.Visibility = PreviewPlaceholder.Visibility = Visibility.Collapsed;
            builtIn = isBuiltIn;
            FileLabel.Text = isBuiltIn ? Localization.T("内置小鸭 · 默认动图") : displayName;
            selectedInfo = info;
            UpdateFileDetails();
            SetStatus(demo ? "预览已更新 · 当前为演示模式。" : isBuiltIn ? "内置小鸭已就绪，可直接应用，也可以选择自己的 GIF。" : "已准备好。应用时会先备份原头像。");
            RefreshActions();
    }
    private void RefreshActions()
    {
        bool available = !busy && (demo || Environment.Is64BitOperatingSystem && Environment.OSVersion.Version.Build >= 22000);
        ApplyButton.IsEnabled = available && selectedPath != null;
        ChooseButton.IsEnabled = DefaultButton.IsEnabled = available;
        BuiltInButton.IsEnabled = available && !builtIn;

        RecoverButton.Visibility = !demo && File.Exists(Storage.At("Accounts", sid, "pending.json")) ? Visibility.Visible : Visibility.Collapsed;
        RecoverButton.IsEnabled = !busy;


    }
    private async void ApplyGif(object sender, RoutedEventArgs e) => await Perform("apply");
    private async void ResetDefault(object sender, RoutedEventArgs e) => await Perform("default");
    private async void Recover(object sender, RoutedEventArgs e) => await Perform("recover");
    private async Task Perform(string action)
    {
        if (busy || action == "apply" && selectedPath == null) return;
        busy = true; RefreshActions();
        SetStatus("正在等待 Windows 授权并处理头像…");
        try
        {
            if (demo)
            {
                await Task.Delay(350);
                if (action == "default") ShowStatic(WindowsAvatar.DefaultPath(192));
                if (action == "apply" && selectedPath != null) { AnimationBehavior.SetSourceUri(PreviewImage, new Uri(selectedPath)); PreviewPlaceholder.Visibility = Visibility.Collapsed; }
                SetStatus("演示完成，未修改系统。");
                return;
            }
            string id = Guid.NewGuid().ToString("N");
            using var current = Process.GetCurrentProcess();
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            foreach (string value in new[] { "--broker", id, action, current.Id.ToString(), current.StartTime.ToUniversalTime().Ticks.ToString() }) start.ArgumentList.Add(value);
            if (action == "apply") start.ArgumentList.Add(selectedPath!);
            using var process = Process.Start(start) ?? throw new IOException("无法启动权限辅助程序。");
            await process.WaitForExitAsync();
            var resultFile = PrivilegeBroker.ResultPath(id);
            var result = File.Exists(resultFile) ? Storage.Read<OperationResult>(resultFile) : new(false, "操作未完成。没有收到辅助程序结果，系统可能阻止了服务启动。");
            SetStatus(result.Message);
            if (result.Success)
            {
                SetStatus(result.Message + " " + await StartMenuRefresh.RunAsync());
                if (action == "apply" && selectedPath != null) { AnimationBehavior.SetSourceUri(PreviewImage, new Uri(selectedPath)); PreviewPlaceholder.Visibility = Visibility.Collapsed; }
                else
                {
                    var snapshot = new WindowsAvatar(sid).Capture();
                    string? path = snapshot.Values.GetValueOrDefault("Image192")?.Path ?? snapshot.Values.Values.FirstOrDefault()?.Path;
                    if (path != null) ShowStatic(Environment.ExpandEnvironmentVariables(path));
                }
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { SetStatus("已取消管理员授权，未修改头像。"); }
        catch (Exception ex) { SetStatus("操作未完成：" + PrivilegeBroker.Explain(ex)); }
        finally { busy = false; RefreshActions(); }
    }
    private void ShowStatic(string path)
    {
        AnimationBehavior.SetSourceUri(PreviewImage, null);
        if (Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase)) AnimationBehavior.SetSourceUri(PreviewImage, new Uri(path));
        else
        {
            using var input = File.OpenRead(path);
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = input; bitmap.EndInit(); bitmap.Freeze();
            PreviewImage.Source = bitmap;
        }
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
    }
    private void ShowLogin(object sender, RoutedEventArgs e) => SetScene(false);
    private void ShowStart(object sender, RoutedEventArgs e) => SetScene(true);
    internal void SetScene(bool start)
    {
        startPreview = start;
        PreviewScene.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, start ? "Panel" : "Scene");
        PreviewContent.VerticalAlignment = start ? VerticalAlignment.Bottom : VerticalAlignment.Center;
        double size = start ? 42 : 94;
        PreviewAvatar.Width = PreviewAvatar.Height = PreviewImage.Width = PreviewImage.Height = size;
        PreviewClip.Center = new(size / 2, size / 2); PreviewClip.RadiusX = PreviewClip.RadiusY = size / 2;
        PreviewPlaceholder.FontSize = start ? 24 : 48;
        PreviewName.FontSize = start ? 14 : 19;
        PinField.Visibility = start ? Visibility.Collapsed : Visibility.Visible;
        LoginTab.SetResourceReference(BackgroundProperty, start ? "Panel" : "Page");
        StartTab.SetResourceReference(BackgroundProperty, start ? "Page" : "Panel");
        LoginTab.SetResourceReference(ForegroundProperty, start ? "Muted" : "Accent");
        StartTab.SetResourceReference(ForegroundProperty, start ? "Accent" : "Muted");
    }

    private void UpdateFileDetails()
    {
        if (selectedInfo is { } info) FileDetails.Text = Localization.Format("{0} × {1} · {2} 帧 · {3:0.##} MB", info.Width, info.Height, info.Frames, info.Bytes / 1024d / 1024);
    }
    private void ApplyLanguage()
    {
        int index = 0;
        foreach (string key in Localization.Keys) Resources["loc" + index++] = Localization.T(key);
        LanguageButton.ToolTip = Localization.T("语言") + " / Language";
        System.Windows.Automation.AutomationProperties.SetName(LanguageButton, Localization.T("语言") + " / Language");
        if (builtIn) FileLabel.Text = Localization.T("内置小鸭 · 默认动图");
        if (demo) AccountLabel.Text = Localization.T("当前 Windows 账户") + " · DuckDuck";
        UpdateFileDetails();
        SetStatus(statusSource);
        ApplyTheme();
        // Refresh string presenters as well as their parents when resources change live.
        foreach (var (button, source) in new[] { (ChooseButton, "＋  选择 GIF"), (BuiltInButton, "用内置小鸭"), (DefaultButton, "恢复默认头像"), (ApplyButton, "应用动图头像"), (LoginTab, "登录界面"), (StartTab, "开始菜单"), (RecoverButton, "恢复上次未完成的操作") })
        {
            var label = new System.Windows.Controls.TextBlock { Text = Localization.T(source), TextWrapping = TextWrapping.NoWrap, FontFamily = button.FontFamily, FontSize = button.FontSize };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            button.Content = label;

            button.InvalidateMeasure();
        }
        InvalidateMeasure();
    }
    private void OpenLanguages(object sender, RoutedEventArgs e)
    {
        var menu = CreateLanguageMenu();
        LanguageButton.ContextMenu = menu;
        menu.IsOpen = true;
    }
    internal System.Windows.Controls.ContextMenu CreateLanguageMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = LanguageButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, Background = (Brush)Resources["Panel"], Foreground = (Brush)Resources["Ink"] };
        void Add(string code, string label)
        {
            var item = new System.Windows.Controls.MenuItem { Header = label, IsCheckable = true, IsChecked = Localization.Preference == code };
            item.Click += (_, _) => { if (!Localization.Select(code, !demo)) SetStatus("语言已切换，但无法保存偏好。"); };
            menu.Items.Add(item);
        }
        Add("system", Localization.T("跟随系统"));
        menu.Items.Add(new System.Windows.Controls.Separator());
        for (int i = 0; i < Localization.Codes.Length; i++) Add(Localization.Codes[i], Localization.Names[i]);
        return menu;
    }
    private void CycleTheme(object sender, RoutedEventArgs e) { theme = (theme + 1) % 3; ApplyTheme(); }
    internal void ApplyTheme(int? forced = null)
    {
        if (forced.HasValue) theme = forced.Value;
        bool dark = theme == 2;
        if (theme == 0) { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0; }
        string[] names = ["Page", "Panel", "Ink", "Muted", "Line", "Accent", "OnAccent", "Scene", "AvatarBg"];
        string[] colors = dark ? ["#202124", "#2B2C30", "#F2F3F5", "#B2B6C0", "#42444B", "#96BDFF", "#132743", "#28374E", "#344057"] : ["#F5F6F8", "#FFFFFF", "#202329", "#646B76", "#E1E5EC", "#2965CF", "#FFFFFF", "#DCE5F4", "#E9EFFB"];
        for (int i = 0; i < names.Length; i++) Resources[names[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        string themeName = Localization.T(theme == 0 ? "跟随系统" : dark ? "深色" : "浅色");
        ThemeButton.ToolTip = Localization.Format("外观：{0} · 点击切换", themeName);
        System.Windows.Automation.AutomationProperties.SetName(ThemeButton, Localization.Format("切换外观，当前{0}", themeName));
        ThemeGlyph.Data = Geometry.Parse(theme switch
        {
            1 => "M16,12 A4,4 0 1 1 8,12 A4,4 0 1 1 16,12 M12,2 L12,4 M12,20 L12,22 M2,12 L4,12 M20,12 L22,12 M4.9,4.9 L6.3,6.3 M17.7,17.7 L19.1,19.1 M4.9,19.1 L6.3,17.7 M17.7,6.3 L19.1,4.9",
            2 => "M20.5,14 A8.8,8.8 0 1 1 10,3.5 A7,7 0 0 0 20.5,14 Z",
            _ => "M5,4 L19,4 Q21,4 21,6 L21,15 Q21,17 19,17 L5,17 Q3,17 3,15 L3,6 Q3,4 5,4 Z M12,17 L12,21 M8,21 L16,21"
        });
        SetScene(startPreview);
    }
}
