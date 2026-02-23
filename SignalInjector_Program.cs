using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Win32;

namespace SignalInjector;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (s, e) =>
            MessageBox.Show(e.Exception.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            MessageBox.Show(e.ExceptionObject?.ToString() ?? "Unknown error", "Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new MainForm());
    }
}

class MainForm : Form
{
    const string DLL_URL = "https://github.com/mojhehh/Signal-Menu/releases/latest/download/SignalSafetyMenu.dll";
    const string AUTOUPDATER_URL = "https://github.com/mojhehh/Signal-Menu/releases/latest/download/SignalAutoUpdater.dll";
    const string BEPINEX_API = "https://api.github.com/repos/BepInEx/BepInEx/releases/latest";
    const string DLL_NAME = "SignalSafetyMenu.dll";
    const string AUTOUPDATER_NAME = "SignalAutoUpdater.dll";
    const string GAME_NAME = "Gorilla Tag";

    static readonly string[] SteamPaths =
    [
        @"C:\Program Files (x86)\Steam",
        @"C:\Program Files\Steam",
        @"D:\Steam",
        @"D:\SteamLibrary",
        @"E:\Steam",
        @"E:\SteamLibrary",
    ];

    static readonly Color BgDark       = Color.FromArgb(12, 12, 20);
    static readonly Color BgPanel      = Color.FromArgb(18, 18, 32);
    static readonly Color BorderBlue   = Color.FromArgb(30, 60, 140);
    static readonly Color AccentBlue   = Color.FromArgb(60, 120, 220);
    static readonly Color AccentCyan   = Color.FromArgb(80, 200, 255);
    static readonly Color TextWhite    = Color.FromArgb(220, 225, 235);
    static readonly Color TextDim      = Color.FromArgb(100, 110, 130);
    static readonly Color SuccessGreen = Color.FromArgb(80, 220, 160);
    static readonly Color ErrorRed     = Color.FromArgb(255, 80, 80);
    static readonly Color WarnYellow   = Color.FromArgb(255, 200, 80);
    static readonly Color BtnHover     = Color.FromArgb(40, 90, 200);
    static readonly Color BtnIdle      = Color.FromArgb(25, 60, 160);
    static readonly Color BtnPress     = Color.FromArgb(20, 45, 120);

    readonly RichTextBox _log;
    readonly Button _installBtn;
    readonly Button _closeBtn;
    readonly Label _titleLabel;
    readonly Label _subtitleLabel;
    readonly Label _versionLabel;
    readonly Panel _headerPanel;
    readonly Panel _bodyPanel;
    readonly ProgressBar _progress;

    bool _running;
    int _stepCount;

    public MainForm()
    {
        Text = "Signal Injector";
        Size = new Size(500, 540);
        MinimumSize = new Size(480, 520);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = BgDark;
        DoubleBuffered = true;

        try
        {
            string icoPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(icoPath))
                Icon = new Icon(icoPath);
        }
        catch { }

        _headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 100,
            BackColor = Color.Transparent,
        };
        _headerPanel.Paint += HeaderPaint;

        _titleLabel = new Label
        {
            Text = "SIGNAL SAFETY MENU",
            Font = new Font("Segoe UI", 18f, FontStyle.Bold),
            ForeColor = AccentCyan,
            BackColor = Color.Transparent,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 50,
            Padding = new Padding(0, 14, 0, 0),
        };

        _subtitleLabel = new Label
        {
            Text = "INJECTOR",
            Font = new Font("Segoe UI", 10f, FontStyle.Regular),
            ForeColor = TextDim,
            BackColor = Color.Transparent,
            AutoSize = false,
            TextAlign = ContentAlignment.TopCenter,
            Dock = DockStyle.Top,
            Height = 24,
        };

        _versionLabel = new Label
        {
            Text = "v1.0",
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = Color.FromArgb(50, 60, 80),
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(450, 8),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };

        _headerPanel.Controls.Add(_versionLabel);
        _headerPanel.Controls.Add(_subtitleLabel);
        _headerPanel.Controls.Add(_titleLabel);

        _bodyPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 8, 24, 16),
            BackColor = Color.Transparent,
        };

        _log = new RichTextBox
        {
            ReadOnly = true,
            BackColor = BgPanel,
            ForeColor = TextWhite,
            Font = new Font("Cascadia Code", 9f, FontStyle.Regular, GraphicsUnit.Point),
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            ScrollBars = RichTextBoxScrollBars.Vertical,
        };

        if (_log.Font.Name != "Cascadia Code")
            _log.Font = new Font("Consolas", 9f);

        _progress = new ProgressBar
        {
            Dock = DockStyle.Bottom,
            Height = 3,
            Style = ProgressBarStyle.Continuous,
            ForeColor = AccentBlue,
            BackColor = BgPanel,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
        };

        var btnPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            BackColor = Color.Transparent,
        };

        _installBtn = new Button
        {
            Text = "INSTALL",
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = TextWhite,
            FlatStyle = FlatStyle.Flat,
            BackColor = BtnIdle,
            Size = new Size(200, 42),
            Cursor = Cursors.Hand,
        };
        _installBtn.FlatAppearance.BorderColor = AccentBlue;
        _installBtn.FlatAppearance.BorderSize = 1;
        _installBtn.FlatAppearance.MouseOverBackColor = BtnHover;
        _installBtn.FlatAppearance.MouseDownBackColor = BtnPress;
        _installBtn.Click += async (s, e) => await RunInstall();

        _closeBtn = new Button
        {
            Text = "CLOSE",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            ForeColor = TextDim,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            Size = new Size(90, 42),
            Cursor = Cursors.Hand,
        };
        _closeBtn.FlatAppearance.BorderColor = Color.FromArgb(40, 45, 60);
        _closeBtn.FlatAppearance.BorderSize = 1;
        _closeBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 30, 45);
        _closeBtn.FlatAppearance.MouseDownBackColor = Color.FromArgb(20, 20, 35);
        _closeBtn.Click += (s, e) => Close();

        btnPanel.Resize += (s, e) =>
        {
            int totalW = _installBtn.Width + 12 + _closeBtn.Width;
            int startX = (btnPanel.Width - totalW) / 2;
            _installBtn.Location = new Point(startX, 8);
            _closeBtn.Location = new Point(startX + _installBtn.Width + 12, 8);
        };

        btnPanel.Controls.Add(_installBtn);
        btnPanel.Controls.Add(_closeBtn);

        var logWrapper = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = BorderBlue };
        var logInner = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 6, 8, 6), BackColor = BgPanel };
        logInner.Controls.Add(_log);
        logInner.Controls.Add(_progress);
        logWrapper.Controls.Add(logInner);

        _bodyPanel.Controls.Add(logWrapper);
        _bodyPanel.Controls.Add(btnPanel);

        Controls.Add(_bodyPanel);
        Controls.Add(_headerPanel);

        LogLine("Ready. Click INSTALL to set up Signal Safety Menu.", TextDim);
    }

    async Task RunInstall()
    {
        if (_running) return;
        _running = true;
        _installBtn.Enabled = false;
        _installBtn.Text = "WORKING...";
        _log.Clear();
        _stepCount = 0;
        SetProgress(0);

        try
        {
            await DoInstall();
        }
        catch (Exception ex)
        {
            LogLine($"Error: {ex.Message}", ErrorRed);
        }

        _installBtn.Enabled = true;
        _installBtn.Text = "INSTALL";
        _running = false;
    }

    async Task DoInstall()
    {
        LogStep("Finding Gorilla Tag...");
        await Task.Delay(200);
        string? gamePath = FindGorillaTag();

        if (gamePath == null)
        {
            LogLine("Could not find Gorilla Tag.", ErrorRed);
            LogLine("Make sure the game is installed through Steam.", TextDim);
            return;
        }
        LogSuccess($"Found: {gamePath}");
        SetProgress(10);

        LogStep("Checking BepInEx...");
        string bepInExPath = Path.Combine(gamePath, "BepInEx");
        string pluginsPath = Path.Combine(bepInExPath, "plugins");

        if (!Directory.Exists(bepInExPath))
        {
            LogLine("  BepInEx not found, installing automatically...", WarnYellow);
            bool installed = await InstallBepInEx(gamePath);
            if (!installed) return;
        }
        else
        {
            LogSuccess("BepInEx found.");
        }
        SetProgress(35);

        if (!Directory.Exists(pluginsPath))
        {
            try { Directory.CreateDirectory(pluginsPath); }
            catch
            {
                LogLine("Could not create plugins folder.", ErrorRed);
                return;
            }
        }

        LogStep("Downloading Signal Safety Menu...");
        byte[]? data = await DownloadFile(DLL_URL, "Signal Safety Menu");
        if (data == null || data.Length < 1024) { LogLine("Download failed.", ErrorRed); return; }
        LogSuccess($"Downloaded ({data.Length / 1024} KB)");
        SetProgress(55);

        byte[]? autoUpdData = await DownloadFile(AUTOUPDATER_URL, "Auto Updater");
        if (autoUpdData != null && autoUpdData.Length > 1024)
            LogSuccess($"Auto Updater ({autoUpdData.Length / 1024} KB)");
        SetProgress(65);

        LogStep("Installing...");
        string dest = Path.Combine(pluginsPath, DLL_NAME);
        string destUpd = Path.Combine(pluginsPath, AUTOUPDATER_NAME);
        bool usedUpdate = false;

        try
        {
            await File.WriteAllBytesAsync(dest, data);
            if (autoUpdData != null) await File.WriteAllBytesAsync(destUpd, autoUpdData);
        }
        catch (IOException)
        {
            LogLine("  DLL is locked (game is using it), writing update file...", WarnYellow);
            try
            {
                await File.WriteAllBytesAsync(dest + ".update", data);
                if (autoUpdData != null) await File.WriteAllBytesAsync(destUpd + ".update", autoUpdData);
                usedUpdate = true;
            }
            catch (Exception ex)
            {
                LogLine($"Write failed: {ex.Message}", ErrorRed);
                return;
            }
        }

        SetProgress(100);
        if (usedUpdate)
            ShowSuccessPending(dest);
        else
            ShowSuccess(dest);
    }

    void ShowSuccess(string dest)
    {
        LogLine("", TextDim);
        LogLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━", BorderBlue);
        LogSuccess("Signal Safety Menu installed!");
        LogLine($"  {dest}", TextDim);
        LogLine("  Launch/restart Gorilla Tag to load the mod.", TextDim);
        LogLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━", BorderBlue);
    }

    void ShowSuccessPending(string dest)
    {
        LogLine("", TextDim);
        LogLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━", BorderBlue);
        LogSuccess("Update downloaded!");
        LogLine($"  Saved to: {dest}.update", TextDim);
        LogLine("", TextDim);
        LogLine("  The DLL is locked because the game is running.", WarnYellow);
        LogLine("  Close and relaunch Gorilla Tag to apply.", TextDim);
        LogLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━", BorderBlue);
    }

    async Task<byte[]?> DownloadFile(string url, string label)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SignalInjector/1.0");
            http.Timeout = TimeSpan.FromSeconds(60);
            return await http.GetByteArrayAsync(url);
        }
        catch (TaskCanceledException)
        {
            LogLine($"{label} download timed out.", ErrorRed);
            return null;
        }
        catch (Exception ex)
        {
            LogLine($"{label} download error: {ex.Message}", ErrorRed);
            return null;
        }
    }

    async Task<bool> InstallBepInEx(string gamePath)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SignalInjector/1.0");
            http.Timeout = TimeSpan.FromSeconds(120);

            LogLine("  Fetching latest BepInEx release...", TextDim);
            string json = await http.GetStringAsync(BEPINEX_API);

            string? downloadUrl = null;
            using (var doc = JsonDocument.Parse(json))
            {
                var assets = doc.RootElement.GetProperty("assets");
                foreach (var asset in assets.EnumerateArray())
                {
                    string name = asset.GetProperty("name").GetString() ?? "";
                    if (name.StartsWith("BepInEx_win_x64", StringComparison.OrdinalIgnoreCase)
                        && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        LogLine($"  Found: {name}", TextDim);
                        break;
                    }
                }
            }

            if (downloadUrl == null)
            {
                LogLine("Could not find BepInEx download.", ErrorRed);
                return false;
            }

            LogLine("  Downloading BepInEx...", TextDim);
            byte[] zipData = await http.GetByteArrayAsync(downloadUrl);
            LogLine($"  Downloaded ({zipData.Length / 1024} KB)", TextDim);

            LogLine("  Extracting...", TextDim);
            int fileCount = 0;
            using (var ms = new MemoryStream(zipData))
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    string destPath = Path.Combine(gamePath, entry.FullName.Replace('/', Path.DirectorySeparatorChar));

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        if (!Directory.Exists(destPath))
                            Directory.CreateDirectory(destPath);
                        continue;
                    }

                    string? dir = Path.GetDirectoryName(destPath);
                    if (dir != null && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    using (var entryStream = entry.Open())
                    using (var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await entryStream.CopyToAsync(fileStream);
                    }
                    fileCount++;
                }
            }
            LogLine($"  Extracted {fileCount} files.", TextDim);

            if (Directory.Exists(Path.Combine(gamePath, "BepInEx")))
            {
                LogSuccess("BepInEx installed!");
                return true;
            }

            LogLine("BepInEx extraction failed.", ErrorRed);
            return false;
        }
        catch (Exception ex)
        {
            LogLine($"BepInEx install failed: {ex.Message}", ErrorRed);
            return false;
        }
    }

    string? FindGorillaTag()
    {
        string? steamRoot = GetSteamPath();
        if (steamRoot != null)
        {
            string? found = SearchSteamRoot(steamRoot);
            if (found != null) return found;

            string libFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libFile))
            {
                foreach (string line in File.ReadAllLines(libFile))
                {
                    if (!line.Contains("\"path\"")) continue;
                    string[] parts = line.Split('"');
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if ((parts[i].Contains('\\') || parts[i].Contains('/')) && parts[i].Length > 3)
                        {
                            found = SearchSteamRoot(parts[i]);
                            if (found != null) return found;
                        }
                    }
                }
            }
        }

        foreach (string path in SteamPaths)
        {
            string? found = SearchSteamRoot(path);
            if (found != null) return found;
        }

        return null;
    }

    static string? SearchSteamRoot(string steamRoot)
    {
        string candidate = Path.Combine(steamRoot, "steamapps", "common", GAME_NAME);
        if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(candidate, $"{GAME_NAME}_Data")))
            return candidate;
        return null;
    }

    static string? GetSteamPath()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")
                         ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
            return key?.GetValue("InstallPath") as string;
        }
        catch { return null; }
    }

    void LogLine(string text, Color color)
    {
        if (InvokeRequired) { Invoke(() => LogLine(text, color)); return; }
        _log.SelectionStart = _log.TextLength;
        _log.SelectionLength = 0;
        _log.SelectionColor = color;
        _log.AppendText(text + "\n");
        _log.ScrollToCaret();
    }

    void LogStep(string text)
    {
        _stepCount++;
        LogLine($"  [{_stepCount}]  {text}", AccentBlue);
    }

    void LogSuccess(string text)
    {
        LogLine($"  +  {text}", SuccessGreen);
    }

    void SetProgress(int val)
    {
        if (InvokeRequired) { Invoke(() => SetProgress(val)); return; }
        _progress.Value = Math.Clamp(val, 0, 100);
    }

    void HeaderPaint(object? sender, PaintEventArgs e)
    {
        if (_headerPanel.Width <= 1 || _headerPanel.Height <= 1) return;
        try
        {
            var g = e.Graphics;
            int y = _headerPanel.Height - 1;
            using var pen = new Pen(AccentBlue, 1f);
            g.DrawLine(pen, 40, y, _headerPanel.Width - 40, y);
            using var sideBrush = new SolidBrush(Color.FromArgb(18, AccentCyan));
            g.FillRectangle(sideBrush, 0, 0, 2, _headerPanel.Height);
            g.FillRectangle(sideBrush, _headerPanel.Width - 2, 0, 2, _headerPanel.Height);
        }
        catch { }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        try
        {
            base.OnPaint(e);
            using var dotBrush = new SolidBrush(Color.FromArgb(30, AccentBlue));
            e.Graphics.FillEllipse(dotBrush, 4, 4, 4, 4);
            e.Graphics.FillEllipse(dotBrush, Width - 12, 4, 4, 4);
        }
        catch { }
    }
}
