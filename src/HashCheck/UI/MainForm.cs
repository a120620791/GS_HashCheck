using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using HashCheck.Hashing;
using HashCheck.Services;

namespace HashCheck.UI;

public sealed class MainForm : Form
{
    private readonly AppArgs _args;
    private readonly FilePanel _left = new FilePanel("文件 1");
    private readonly FilePanel _right = new FilePanel("文件 2");

    private readonly ProgressBar _progress = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Continuous };
    private readonly Label _status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Text = "就绪" };
    private readonly Label _verdict = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Text = "选择两个文件后可点“对比两个文件”" };
    private readonly Button _compareButton = new Button { Text = "对比两个文件", AutoSize = true };
    private readonly Button _hideCompareButton = new Button { Text = "收起对比表", AutoSize = true };
    private readonly Button _cancelButton = new Button { Text = "取消", AutoSize = true, Enabled = false };
    private readonly CheckBox _shellCheckBox = new CheckBox { Text = "资源管理器右键菜单", AutoSize = true, Enabled = false };

    private readonly DataGridView _compareGrid = new DataGridView();
    private readonly RowStyle _compareRowStyle = new RowStyle(SizeType.Absolute, 0f);

    private CancellationTokenSource? _cts;
    private bool _suppressShellToggle;
    private bool _busy;

    public MainForm(AppArgs args)
    {
        _args = args;
        BuildLayout();
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        _suppressShellToggle = true;
        try
        {
            _shellCheckBox.Checked = ShellIntegration.IsRegistered();
        }
        catch
        {
            // 读注册表失败时保持未勾选
        }
        _suppressShellToggle = false;
        _shellCheckBox.Enabled = true;

        if (_args.Files.Count > 0)
            _left.SetFilePath(_args.Files[0]);

        if (_args.Files.Count > 1)
            _right.SetFilePath(_args.Files[1]);

        if (!string.IsNullOrWhiteSpace(_left.FilePath))
            await ComputeAsync(_left);

        if (!string.IsNullOrWhiteSpace(_right.FilePath))
            await ComputeAsync(_right);

        if (_left.Result is not null && _right.Result is not null)
            ShowComparison(_left.Result, _right.Result);

        if (_args.ExportAfterCompute)
        {
            if (!string.IsNullOrWhiteSpace(_left.FilePath))
                await ExportAsync(_left);

            if (_args.Files.Count > 1 && !string.IsNullOrWhiteSpace(_right.FilePath))
                await ExportAsync(_right);
        }
    }

    private void BuildLayout()
    {
        Text = "Hash 校验工具 — MD5 / SHA-1 / SHA-256 / SHA-512 / SHA-3 / BLAKE3";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 720);
        MinimumSize = new Size(920, 600);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(_compareRowStyle);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildFilesArea(), 0, 0);
        root.Controls.Add(BuildProgressArea(), 0, 1);
        root.Controls.Add(BuildCompareArea(), 0, 2);
        root.Controls.Add(BuildBottomArea(), 0, 3);

        Controls.Add(root);

        _left.FileChosen += async (_, _) => await ComputeAsync(_left);
        _right.FileChosen += async (_, _) => await ComputeAsync(_right);
        _left.ComputeRequested += async (_, _) => await ComputeAsync(_left);
        _right.ComputeRequested += async (_, _) => await ComputeAsync(_right);
        _left.ExportRequested += async (_, _) => await ExportAsync(_left);
        _right.ExportRequested += async (_, _) => await ExportAsync(_right);

        _compareButton.Click += async (_, _) => await CompareAsync();
        _hideCompareButton.Click += (_, _) => _compareRowStyle.Height = 0f;
        _cancelButton.Click += (_, _) => _cts?.Cancel();
        _shellCheckBox.CheckedChanged += (_, _) =>
        {
            if (!_suppressShellToggle)
                ToggleShellIntegration();
        };
    }

    private Control BuildFilesArea()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        table.Controls.Add(WrapInGroup(_left, "文件 1"), 0, 0);
        table.Controls.Add(WrapInGroup(_right, "文件 2"), 1, 0);
        return table;
    }

    private static GroupBox WrapInGroup(Control content, string title)
    {
        var box = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 8) };
        content.Dock = DockStyle.Fill;
        box.Controls.Add(content);
        return box;
    }

    private Control BuildProgressArea()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Height = 26,
            Margin = new Padding(0, 6, 0, 0),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));

        _progress.Margin = new Padding(0, 5, 8, 5);
        table.Controls.Add(_progress, 0, 0);
        table.Controls.Add(_status, 1, 0);
        return table;
    }

    private Control BuildCompareArea()
    {
        _compareGrid.Dock = DockStyle.Fill;
        _compareGrid.ReadOnly = true;
        _compareGrid.AllowUserToAddRows = false;
        _compareGrid.AllowUserToDeleteRows = false;
        _compareGrid.AllowUserToResizeRows = false;
        _compareGrid.RowHeadersVisible = false;
        _compareGrid.MultiSelect = false;
        _compareGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _compareGrid.BackgroundColor = Color.White;
        _compareGrid.BorderStyle = BorderStyle.FixedSingle;
        _compareGrid.Font = new Font("Consolas", 9.75f);
        _compareGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;

        _compareGrid.Columns.Add(MakeColumn("算法", 90, DataGridViewAutoSizeColumnMode.None));
        _compareGrid.Columns.Add(MakeColumn("文件 1", 100, DataGridViewAutoSizeColumnMode.Fill));
        _compareGrid.Columns.Add(MakeColumn("文件 2", 100, DataGridViewAutoSizeColumnMode.Fill));
        _compareGrid.Columns.Add(MakeColumn("结果", 100, DataGridViewAutoSizeColumnMode.None));
        _compareGrid.Columns[1].FillWeight = 50f;
        _compareGrid.Columns[2].FillWeight = 50f;

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var caption = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "对比结果（逐算法）",
        };
        header.Controls.Add(caption, 0, 0);
        header.Controls.Add(_hideCompareButton, 1, 0);

        var area = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 6, 0, 0),
        };
        area.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        area.Controls.Add(header, 0, 0);
        area.Controls.Add(_compareGrid, 0, 1);
        return area;
    }

    private Control BuildBottomArea()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 6, 0, 0),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0),
        };
        buttons.Controls.Add(_compareButton);
        buttons.Controls.Add(_cancelButton);

        _verdict.Height = 26;
        _shellCheckBox.Margin = new Padding(8, 4, 0, 0);

        table.Controls.Add(buttons, 0, 0);
        table.Controls.Add(_verdict, 1, 0);
        table.Controls.Add(_shellCheckBox, 2, 0);
        return table;
    }

    private static DataGridViewTextBoxColumn MakeColumn(string header, int width, DataGridViewAutoSizeColumnMode mode)
    {
        var column = new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            AutoSizeMode = mode,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ReadOnly = true,
        };

        if (width > 0)
            column.Width = width;

        return column;
    }

    private async Task<HashResult?> ComputeAsync(FilePanel panel)
    {
        if (panel.IsBusy || _busy)
            return panel.Result;

        var path = panel.FilePath;
        if (string.IsNullOrWhiteSpace(path))
            return panel.Result;

        _busy = true;
        panel.SetBusy(true);
        _compareButton.Enabled = false;
        _cancelButton.Enabled = true;
        _cts = new CancellationTokenSource();

        var stopwatch = Stopwatch.StartNew();
        var progress = new Progress<HashProgress>(p => ShowProgress(panel, p));

        try
        {
            var computation = await HashEngine.ComputeAsync(path, progress, _cts.Token);
            panel.SetResult(computation.Result);
            _progress.Value = 100;
            _status.Text = panel.Title + "：" + Path.GetFileName(path) + " 计算完成，用时 " +
                           stopwatch.Elapsed.TotalSeconds.ToString("0.0") + " 秒";

            if (computation.FileChangedDuringHashing)
                MessageBox.Show(this, "文件在计算过程中被修改过，哈希值可能无效，建议重新计算。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);

            return computation.Result;
        }
        catch (OperationCanceledException)
        {
            _status.Text = "已取消";
            return null;
        }
        catch (Exception ex)
        {
            _status.Text = "计算失败";
            MessageBox.Show(this, ex.GetType().Name + "：" + ex.Message, "计算失败",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        finally
        {
            panel.SetBusy(false);
            _busy = false;
            _compareButton.Enabled = true;
            _cancelButton.Enabled = false;
            _progress.Value = 0;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task<HashResult?> GetOrComputeAsync(FilePanel panel)
    {
        var path = panel.FilePath;
        if (panel.Result is { } cached && !string.IsNullOrWhiteSpace(path))
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length == cached.Length && info.LastWriteTimeUtc == cached.LastWriteTimeUtc)
                return cached;
        }

        return await ComputeAsync(panel);
    }

    private async Task CompareAsync()
    {
        if (string.IsNullOrWhiteSpace(_left.FilePath) || string.IsNullOrWhiteSpace(_right.FilePath))
        {
            MessageBox.Show(this, "请先在左右两侧各选择一个文件。", "对比",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var left = await GetOrComputeAsync(_left);
        var right = await GetOrComputeAsync(_right);

        if (left is null || right is null)
            return;

        ShowComparison(left, right);
    }

    private void ShowComparison(HashResult left, HashResult right)
    {
        _compareGrid.Rows.Clear();
        _compareGrid.Columns[1].HeaderText = left.FileName;
        _compareGrid.Columns[2].HeaderText = right.FileName;
        _compareRowStyle.Height = 212f;

        if (left.Length != right.Length)
        {
            _verdict.ForeColor = Color.Firebrick;
            _verdict.Text = "✘ 文件大小不同（" + FormatSize(left.Length) + " / " + FormatSize(right.Length) +
                            "），内容必然不同";
            return;
        }

        var sameCount = 0;
        var different = new List<string>();
        var skipped = new List<string>();

        foreach (var spec in HashCatalog.All)
        {
            var leftHex = left.Find(spec.Label)?.Hex;
            var rightHex = right.Find(spec.Label)?.Hex;

            string status;
            Color color;

            if (leftHex is null || rightHex is null)
            {
                status = "— 未比对";
                color = Color.DimGray;
                skipped.Add(spec.Label);
            }
            else if (string.Equals(leftHex, rightHex, StringComparison.OrdinalIgnoreCase))
            {
                status = "✔ 一致";
                color = Color.SeaGreen;
                sameCount++;
            }
            else
            {
                status = "✘ 不一致";
                color = Color.Firebrick;
                different.Add(spec.Label);
            }

            var index = _compareGrid.Rows.Add(spec.Label, leftHex ?? "不可用", rightHex ?? "不可用", status);
            _compareGrid.Rows[index].Cells[3].Style.ForeColor = color;
        }

        var verdict = different.Count == 0
            ? "✔ 已比对的 " + sameCount + " 个算法全部一致"
            : "✘ 不一致的算法：" + string.Join("、", different);

        if (skipped.Count > 0)
            verdict += "；未参与比对：" + string.Join("、", skipped);

        _verdict.ForeColor = different.Count == 0 ? Color.SeaGreen : Color.Firebrick;
        _verdict.Text = verdict;
    }

    private async Task ExportAsync(FilePanel panel)
    {
        var sourcePath = panel.FilePath;
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            MessageBox.Show(this, "请先选择要导出的文件。", "导出结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var result = panel.Result ?? await ComputeAsync(panel);
        if (result is null)
            return;

        string target;
        try
        {
            target = ExportService.BuildTargetPath(sourcePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导出结果", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (File.Exists(target))
        {
            var choice = MessageBox.Show(this,
                "目标文件已存在：\r\n" + target + "\r\n\r\n“是”覆盖它，“否”自动换一个名字，“取消”放弃导出。",
                "导出结果", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (choice == DialogResult.Cancel)
                return;

            if (choice == DialogResult.No)
            {
                try
                {
                    target = ExportService.MakeUnique(target);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "导出结果", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
        }

        try
        {
            ExportService.Write(target, result.ToDisplayText());
        }
        catch (Exception ex)
        {
            var choice = MessageBox.Show(this,
                "无法写入原目录：\r\n" + ex.Message + "\r\n\r\n是否另存到其他位置？",
                "导出结果", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (choice != DialogResult.Yes)
                return;

            using var dialog = new SaveFileDialog
            {
                Title = "导出 Hash 校验结果",
                FileName = Path.GetFileName(target),
                InitialDirectory = Path.GetDirectoryName(target),
                Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                DefaultExt = "txt",
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                ExportService.Write(dialog.FileName, result.ToDisplayText());
                target = dialog.FileName;
            }
            catch (Exception ex2)
            {
                MessageBox.Show(this, ex2.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        // 导出后不弹任何窗口、也不打开资源管理器，只在状态栏提示导出路径
        _status.Text = "已导出：" + target;
    }

    private void ToggleShellIntegration()
    {
        try
        {
            if (_shellCheckBox.Checked)
            {
                ShellIntegration.Register();
                _status.Text = "已添加资源管理器右键菜单（选中文件后右键，Win11 在“显示更多选项”里）";
            }
            else
            {
                ShellIntegration.Unregister();
                _status.Text = "已移除资源管理器右键菜单";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "右键菜单设置失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _suppressShellToggle = true;
            _shellCheckBox.Checked = !_shellCheckBox.Checked;
            _suppressShellToggle = false;
        }
    }

    private void ShowProgress(FilePanel panel, HashProgress progress)
    {
        _progress.Value = (int)Math.Min(100d, Math.Max(0d, progress.Percent));
        _status.Text = panel.Title + " 计算中 " + progress.Percent.ToString("0.0") + "% · " +
                       FormatSize(progress.BytesDone) + " / " + FormatSize(progress.TotalBytes) + " · " +
                       (progress.BytesPerSecond / 1024d / 1024d).ToString("0.0") + " MB/s";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return bytes + " B";
        if (bytes < 1024L * 1024)
            return (bytes / 1024d).ToString("0.0") + " KB";
        if (bytes < 1024L * 1024 * 1024)
            return (bytes / 1024d / 1024d).ToString("0.0") + " MB";
        return (bytes / 1024d / 1024d / 1024d).ToString("0.00") + " GB";
    }


}