using System.Net;
using GameDock.Core.Config;
using GameDock.Core.Loaders;
using GameDock.Core.Mirrors;
using GameDock.Core.Models;
using GameDock.Core.Services;
using GameDock.Core.Util;

namespace GameDock.App;

/// <summary>GameDock 主窗口（纯代码布局，无 Designer 依赖）。</summary>
public sealed class MainForm : Form
{
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    })
    { Timeout = TimeSpan.FromSeconds(30) };

    private readonly AppConfig _config;
    private readonly MirrorRegistry _mirrors;
    private CancellationTokenSource? _cts;
    private List<ManifestVersion> _allVersions = new();

    // 控件
    private ComboBox cboMirror = null!;
    private TextBox txtMcDir = null!;
    private ListView lvVersions = null!;
    private RadioButton radAll = null!, radRelease = null!, radSnapshot = null!;
    private TextBox txtSearch = null!;
    private CheckBox chkClient = null!, chkServer = null!, chkAssets = null!, chkNatives = null!;
    private Button btnDownload = null!, btnCancel = null!;
    private ProgressBar prgMain = null!;
    private TextBox txtLog = null!;
    private ComboBox cboLoaderMc = null!, cboLoaderType = null!, cboLoaderVersion = null!;
    private Button btnLoadVersions = null!, btnLoaderDownload = null!;
    private CheckBox chkAutoRun = null!;
    private Label lblStatus = null!;

    public MainForm()
    {
        _config = ConfigStore.Load();
        _mirrors = new MirrorRegistry(Path.Combine(Path.GetDirectoryName(ConfigStore.DefaultPath)!, "mirrors.json"));
        BuildUi();
        LoadInitialState();
    }

    // ---------- UI 构建 ----------

    private void BuildUi()
    {
        Text = "GameDock · 游戏坞 —— Minecraft 下载即导入启动器";
        Width = 1080;
        Height = 720;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 600);
        Font = new Font("Microsoft YaHei UI", 9F);

        // 顶部：镜像 + 目录
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 6, 8, 0) };
        top.Controls.Add(MakeLabel("镜像源："));
        cboMirror = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
        cboMirror.DataSource = _mirrors.All.Select(m => $"{m.DisplayName}（{m.Key}）").ToList();
        cboMirror.SelectedIndex = Math.Max(0, _mirrors.All.ToList().FindIndex(m => m.Key == _config.Mirror));
        top.Controls.Add(cboMirror);

        var btnHealth = new Button { Text = "测速", Width = 56, AutoSize = false };
        btnHealth.Click += async (_, _) => await HealthCheckAsync();
        top.Controls.Add(btnHealth);

        top.Controls.Add(MakeLabel("启动器目录："));
        txtMcDir = new TextBox { Width = 300, Text = _config.GetMcDir() };
        top.Controls.Add(txtMcDir);
        var btnBrowse = new Button { Text = "浏览…", Width = 64 };
        btnBrowse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "选择 .minecraft 目录", SelectedPath = txtMcDir.Text };
            if (dlg.ShowDialog(this) == DialogResult.OK) txtMcDir.Text = dlg.SelectedPath;
        };
        top.Controls.Add(btnBrowse);
        Controls.Add(top);

        // 底部：日志 + 进度
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 190 };
        lblStatus = new Label { Dock = DockStyle.Top, Height = 22, Text = "就绪。", ForeColor = Color.DimGray };
        bottom.Controls.Add(lblStatus);
        prgMain = new ProgressBar { Dock = DockStyle.Top, Height = 20, Minimum = 0, Maximum = 100 };
        bottom.Controls.Add(prgMain);
        txtLog = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(20, 20, 20),
            ForeColor = Color.White,
            Font = new Font("Consolas", 9F),
        };
        bottom.Controls.Add(txtLog);
        Controls.Add(bottom);

        // 中部：左版本列表 / 右操作区
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 420, Panel1MinSize = 300 };
        split.Panel1.Controls.Add(BuildVersionPanel());
        split.Panel2.Controls.Add(BuildActionPanel());
        Controls.Add(split);

        Log.Message += OnLog;
        FormClosed += (_, _) => Log.Message -= OnLog;
    }

    private Control BuildVersionPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36 };
        radAll = new RadioButton { Text = "全部", Checked = true, AutoSize = true };
        radRelease = new RadioButton { Text = "正式版", AutoSize = true };
        radSnapshot = new RadioButton { Text = "快照", AutoSize = true };
        radAll.CheckedChanged += (_, _) => ApplyVersionFilter();
        radRelease.CheckedChanged += (_, _) => ApplyVersionFilter();
        radSnapshot.CheckedChanged += (_, _) => ApplyVersionFilter();
        bar.Controls.Add(radAll);
        bar.Controls.Add(radRelease);
        bar.Controls.Add(radSnapshot);
        bar.Controls.Add(MakeLabel("搜索："));
        txtSearch = new TextBox { Width = 90 };
        txtSearch.TextChanged += (_, _) => ApplyVersionFilter();
        bar.Controls.Add(txtSearch);
        var btnRefresh = new Button { Text = "刷新", Width = 56 };
        btnRefresh.Click += async (_, _) => await LoadManifestAsync();
        bar.Controls.Add(btnRefresh);
        panel.Controls.Add(bar);

        lvVersions = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
        };
        lvVersions.Columns.Add("版本", 200);
        lvVersions.Columns.Add("类型", 70);
        lvVersions.Columns.Add("发布", 90);
        panel.Controls.Add(lvVersions);
        return panel;
    }

    private Control BuildActionPanel()
    {
        var tab = new TabControl { Dock = DockStyle.Fill };
        tab.TabPages.Add(BuildVanillaPage());
        tab.TabPages.Add(BuildLoaderPage());
        return tab;
    }

    private TabPage BuildVanillaPage()
    {
        var page = new TabPage("原版下载");
        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(10),
            AutoScroll = true,
        };

        root.Controls.Add(new Label { Text = "在左侧选择版本后点击下载：", AutoSize = true });

        var opts = new FlowLayoutPanel { AutoSize = true };
        chkClient = new CheckBox { Text = "客户端", Checked = true, AutoSize = true };
        chkServer = new CheckBox { Text = "服务端", Checked = _config.DownloadServer, AutoSize = true };
        chkAssets = new CheckBox { Text = "资源(assets)", Checked = _config.DownloadAssets, AutoSize = true };
        chkNatives = new CheckBox { Text = "natives", Checked = true, AutoSize = true };
        opts.Controls.Add(chkClient);
        opts.Controls.Add(chkServer);
        opts.Controls.Add(chkAssets);
        opts.Controls.Add(chkNatives);
        root.Controls.Add(opts);

        var row = new FlowLayoutPanel { AutoSize = true };
        btnDownload = new Button { Text = "下载并导入", Width = 110, BackColor = Color.FromArgb(56, 132, 255), ForeColor = Color.White };
        btnDownload.Click += async (_, _) => await DownloadSelectedVersionAsync();
        btnCancel = new Button { Text = "取消", Width = 70, Enabled = false };
        btnCancel.Click += (_, _) => _cts?.Cancel();
        row.Controls.Add(btnDownload);
        row.Controls.Add(btnCancel);
        root.Controls.Add(row);

        root.Controls.Add(new Label
        {
            Text = "提示：下载文件自动写入 versions / libraries / assets 目录，\r\nPCL2、HMCL、FCL 启动后即可识别该版本；镜像被“封杀”时\r\n会自动回退到列表中的下一个镜像。",
            AutoSize = true,
            ForeColor = Color.DimGray,
        });

        page.Controls.Add(root);
        return page;
    }

    private TabPage BuildLoaderPage()
    {
        var page = new TabPage("加载器");
        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(10),
            AutoScroll = true,
        };

        var row1 = new FlowLayoutPanel { AutoSize = true };
        row1.Controls.Add(MakeLabel("MC 版本："));
        cboLoaderMc = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
        row1.Controls.Add(cboLoaderMc);
        row1.Controls.Add(MakeLabel("类型："));
        cboLoaderType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        cboLoaderType.Items.AddRange(new object[] { "Fabric", "Forge", "NeoForge", "OptiFine" });
        cboLoaderType.SelectedIndex = 0;
        row1.Controls.Add(cboLoaderType);
        root.Controls.Add(row1);

        var row2 = new FlowLayoutPanel { AutoSize = true };
        btnLoadVersions = new Button { Text = "获取加载器版本", Width = 110 };
        btnLoadVersions.Click += async (_, _) => await LoadLoaderVersionsAsync();
        row2.Controls.Add(btnLoadVersions);
        cboLoaderVersion = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        row2.Controls.Add(cboLoaderVersion);
        root.Controls.Add(row2);

        var row3 = new FlowLayoutPanel { AutoSize = true };
        chkAutoRun = new CheckBox { Text = "下载后自动运行安装器（需要本机 Java 8+）", Checked = _config.AutoRunInstaller, AutoSize = true };
        row3.Controls.Add(chkAutoRun);
        root.Controls.Add(row3);

        var row4 = new FlowLayoutPanel { AutoSize = true };
        btnLoaderDownload = new Button { Text = "下载加载器", Width = 110, BackColor = Color.FromArgb(56, 132, 255), ForeColor = Color.White };
        btnLoaderDownload.Click += async (_, _) => await DownloadLoaderAsync();
        row4.Controls.Add(btnLoaderDownload);
        root.Controls.Add(row4);

        root.Controls.Add(new Label
        {
            Text = "Fabric：通过 meta API 生成完整版本并直接导入，无需 Java。\r\nForge / NeoForge / OptiFine：下载安装器，勾选“自动运行”可用本机 Java 安装。",
            AutoSize = true,
            ForeColor = Color.DimGray,
        });

        page.Controls.Add(root);
        return page;
    }

    private static Label MakeLabel(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 6, 3, 6) };

    // ---------- 初始化与数据 ----------

    private void LoadInitialState()
    {
        var known = LauncherLocator.Detect().FirstOrDefault(d => d.Exists);
        if (known is not null && string.IsNullOrEmpty(_config.McDir))
            txtMcDir.Text = known.McDir;
        _ = LoadManifestAsync();
    }

    private async Task LoadManifestAsync()
    {
        try
        {
            SetBusy(true, "正在获取版本清单…");
            var vdl = new VersionDownloader(_http, _mirrors.Ordered(SelectedMirrorKey()), 4, 3);
            var manifest = await vdl.GetManifestAsync(CancellationToken.None);
            _allVersions = manifest.Versions;

            // 加载器页 MC 版本下拉（最近 60 个正式版 + 快照）。
            cboLoaderMc.Items.Clear();
            cboLoaderMc.Items.AddRange(manifest.Versions
                .Where(v => v.Type is "release" or "snapshot")
                .OrderByDescending(v => v.ReleaseTime)
                .Take(60).Select(v => v.Id).ToArray());
            if (cboLoaderMc.Items.Count > 0) cboLoaderMc.SelectedIndex = 0;

            ApplyVersionFilter();
            lblStatus.Text = $"版本清单已更新：{_allVersions.Count} 个版本。";
        }
        catch (Exception ex)
        {
            Log.Error($"获取版本清单失败：{ex.Message}");
            lblStatus.Text = "获取版本清单失败，请检查网络或换镜像。";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyVersionFilter()
    {
        var type = radRelease.Checked ? "release" : radSnapshot.Checked ? "snapshot" : null;
        var keyword = txtSearch.Text.Trim();
        var list = _allVersions
            .Where(v => type is null || v.Type == type)
            .Where(v => keyword.Length == 0 || v.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.ReleaseTime)
            .ToList();

        lvVersions.BeginUpdate();
        lvVersions.Items.Clear();
        foreach (var v in list)
        {
            var item = new ListViewItem(new[] { v.Id, v.Type ?? "", v.ReleaseTime is { Length: >= 10 } ? v.ReleaseTime[..10] : "" });
            item.Tag = v;
            lvVersions.Items.Add(item);
        }
        lvVersions.EndUpdate();
    }

    private string SelectedMirrorKey()
    {
        var idx = cboMirror.SelectedIndex;
        return idx >= 0 && idx < _mirrors.All.Count ? _mirrors.All[idx].Key : "bmclapi";
    }

    private async Task HealthCheckAsync()
    {
        try
        {
            lblStatus.Text = "正在测速…";
            var health = new MirrorHealth(_http);
            var sb = new System.Text.StringBuilder();
            foreach (var m in _mirrors.All)
            {
                var ms = await health.PingAsync(m, CancellationToken.None);
                sb.AppendLine($"{m.DisplayName}：{(ms >= 0 ? ms + " ms" : "不可达")}");
            }
            Log.Info(sb.ToString().TrimEnd());
            lblStatus.Text = "测速完成，结果见日志。";
        }
        catch (Exception ex)
        {
            Log.Error($"测速失败：{ex.Message}");
        }
    }

    // ---------- 下载 ----------

    private async Task DownloadSelectedVersionAsync()
    {
        if (lvVersions.SelectedItems.Count == 0)
        {
            Log.Warn("请先在左侧选择一个版本。");
            return;
        }
        var entry = (ManifestVersion)lvVersions.SelectedItems[0].Tag!;
        var mcDir = txtMcDir.Text.Trim();
        if (mcDir.Length == 0)
        {
            Log.Warn("请先填写启动器目录。");
            return;
        }

        var options = new DownloadOptions
        {
            McDir = mcDir,
            DownloadClient = chkClient.Checked,
            DownloadServer = chkServer.Checked,
            DownloadAssets = chkAssets.Checked,
            ExtractNatives = chkNatives.Checked,
        };

        _cts = new CancellationTokenSource();
        btnDownload.Enabled = false;
        btnCancel.Enabled = true;
        prgMain.Value = 0;

        var progress = new Progress<DownloadProgress>(p =>
        {
            prgMain.Value = (int)Math.Round(p.Percent);
            lblStatus.Text = $"下载中：{p.FilesDone}/{p.FilesTotal} 文件，{p.Percent:F1}%  —— {p.CurrentFile}";
        });

        try
        {
            var vdl = new VersionDownloader(_http, _mirrors.Ordered(SelectedMirrorKey()), _config.Threads, _config.RetryCount);
            var report = await vdl.DownloadVersionAsync(entry, options, progress, null, _cts.Token);
            Log.Info($"完成：{report.VersionId} → {report.McDir}（客户端 {report.ClientWritten}，服务端 {report.ServerWritten}，库 {report.LibrariesWritten}，资源 {report.AssetsWritten}）");
            lblStatus.Text = $"导入完成：{report.VersionId}";
        }
        catch (OperationCanceledException)
        {
            Log.Warn("已取消下载。");
            lblStatus.Text = "已取消。";
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            lblStatus.Text = "下载失败，详见日志。";
        }
        finally
        {
            btnDownload.Enabled = true;
            btnCancel.Enabled = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private async Task LoadLoaderVersionsAsync()
    {
        try
        {
            if (cboLoaderMc.SelectedItem is not string mc || cboLoaderType.SelectedItem is not string type)
                return;
            cboLoaderVersion.Items.Clear();
            btnLoadVersions.Enabled = false;
            lblStatus.Text = $"正在获取 {type} 版本…";

            switch (type)
            {
                case "Fabric":
                {
                    var svc = new FabricService(_http, _mirrors.Ordered(SelectedMirrorKey()), _config.Threads, _config.RetryCount);
                    var loaders = await svc.GetLoaderVersionsAsync(mc, CancellationToken.None);
                    cboLoaderVersion.Items.AddRange(loaders.Take(60).ToArray());
                    break;
                }
                case "Forge":
                {
                    var svc = new ForgeService(_http, _mirrors.Ordered(SelectedMirrorKey()), _config.Threads, _config.RetryCount);
                    var versions = await svc.GetForgeVersionsAsync(mc, CancellationToken.None);
                    cboLoaderVersion.Items.AddRange(versions.ToArray());
                    break;
                }
                case "NeoForge":
                {
                    var svc = new NeoForgeService(_http, _mirrors.Ordered(SelectedMirrorKey()), _config.Threads, _config.RetryCount);
                    var versions = await svc.GetVersionsAsync(CancellationToken.None);
                    var filtered = svc.FilterByMc(mc, versions);
                    var shown = filtered.Count > 0 ? filtered : versions.Take(30).ToList();
                    cboLoaderVersion.Items.AddRange(shown.Take(60).ToArray());
                    break;
                }
                case "OptiFine":
                {
                    var svc = new OptifineService(_http, _mirrors.Ordered(SelectedMirrorKey()), _config.Threads, _config.RetryCount);
                    var list = await svc.GetReleasesAsync(mc, CancellationToken.None);
                    cboLoaderVersion.Items.AddRange(list.Select(x => $"{x.Filename}（{x.Type}）").ToArray());
                    break;
                }
            }

            if (cboLoaderVersion.Items.Count > 0) cboLoaderVersion.SelectedIndex = 0;
            lblStatus.Text = $"共 {cboLoaderVersion.Items.Count} 个 {type} 版本。";
        }
        catch (Exception ex)
        {
            Log.Error($"获取加载器版本失败：{ex.Message}");
        }
        finally
        {
            btnLoadVersions.Enabled = true;
        }
    }

    private async Task DownloadLoaderAsync()
    {
        if (cboLoaderMc.SelectedItem is not string mc || cboLoaderType.SelectedItem is not string type)
        {
            Log.Warn("请先选择 MC 版本和加载器类型。");
            return;
        }
        if (cboLoaderVersion.SelectedItem is not string selected)
        {
            Log.Warn("请先点击“获取加载器版本”并选择版本。");
            return;
        }

        var mcDir = txtMcDir.Text.Trim();
        if (mcDir.Length == 0)
        {
            Log.Warn("请先填写启动器目录。");
            return;
        }

        var options = new DownloadOptions
        {
            McDir = mcDir,
            DownloadClient = true,
            DownloadAssets = chkAssets.Checked,
            ExtractNatives = chkNatives.Checked,
        };

        _cts = new CancellationTokenSource();
        btnLoaderDownload.Enabled = false;
        prgMain.Value = 0;
        var progress = new Progress<DownloadProgress>(p =>
        {
            prgMain.Value = (int)Math.Round(p.Percent);
            lblStatus.Text = $"下载中：{p.Percent:F1}%  —— {p.CurrentFile}";
        });

        try
        {
            var mirrors = _mirrors.Ordered(SelectedMirrorKey());
            switch (type)
            {
                case "Fabric":
                {
                    var svc = new FabricService(_http, mirrors, _config.Threads, _config.RetryCount);
                    var result = await svc.InstallAsync(mc, selected, options, progress, null, _cts.Token);
                    Log.Info(result.Message);
                    break;
                }
                case "Forge":
                {
                    var svc = new ForgeService(_http, mirrors, _config.Threads, _config.RetryCount);
                    var result = await svc.DownloadInstallerAsync(mc, selected, Path.Combine(mcDir, "downloads"), chkAutoRun.Checked, progress, null, _cts.Token);
                    Log.Info(result.Message);
                    break;
                }
                case "NeoForge":
                {
                    var svc = new NeoForgeService(_http, mirrors, _config.Threads, _config.RetryCount);
                    var result = await svc.DownloadInstallerAsync(selected, Path.Combine(mcDir, "downloads"), chkAutoRun.Checked, progress, null, _cts.Token);
                    Log.Info(result.Message);
                    break;
                }
                case "OptiFine":
                {
                    var svc = new OptifineService(_http, mirrors, _config.Threads, _config.RetryCount);
                    var list = await svc.GetReleasesAsync(mc, _cts.Token);
                    var idx = cboLoaderVersion.SelectedIndex;
                    if (idx < 0 || idx >= list.Count)
                    {
                        Log.Warn("无法定位所选 OptiFine 版本。");
                        break;
                    }
                    var result = await svc.DownloadInstallerAsync(list[idx], Path.Combine(mcDir, "downloads"), chkAutoRun.Checked, progress, null, _cts.Token);
                    Log.Info(result.Message);
                    break;
                }
            }
            lblStatus.Text = "加载器任务结束，详见日志。";
        }
        catch (OperationCanceledException)
        {
            Log.Warn("已取消。");
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
        }
        finally
        {
            btnLoaderDownload.Enabled = true;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        btnDownload.Enabled = !busy;
        if (status is not null) lblStatus.Text = status;
        Application.DoEvents();
    }

    private void OnLog(object? sender, Log.LogEventArgs e)
    {
        if (IsDisposed || txtLog.IsDisposed) return;
        var line = $"[{e.Time:HH:mm:ss}] {e.Message}{Environment.NewLine}";
        if (txtLog.InvokeRequired)
        {
            txtLog.BeginInvoke(new Action(() => AppendLog(line)));
        }
        else
        {
            AppendLog(line);
        }
    }

    private void AppendLog(string line)
    {
        txtLog.AppendText(line);
        if (txtLog.TextLength > 200_000) txtLog.Text = txtLog.Text[^100_000..];
        txtLog.SelectionStart = txtLog.TextLength;
        txtLog.ScrollToCaret();
    }
}
