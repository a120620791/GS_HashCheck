using System.Drawing;
using System.Windows.Forms;
using HashCheck.Hashing;

namespace HashCheck.UI;

/// <summary>单个文件的界面：文件路径 + 结果文本框 + 操作按钮。</summary>
public sealed class FilePanel : UserControl
{
    private static readonly Font MonoFont = new Font("Consolas", 9.75f);

    private readonly TextBox _pathBox = new TextBox();
    private readonly TextBox _resultBox = new TextBox();
    private readonly Button _browseButton = new Button { Text = "浏览…", AutoSize = true };
    private readonly Button _calcButton = new Button { Text = "计算Hash值", AutoSize = true };
    private readonly Button _exportButton = new Button { Text = "导出结果", AutoSize = true };
    private readonly Button _copyButton = new Button { Text = "复制哈希", AutoSize = true };
    private readonly Button _clearButton = new Button { Text = "清空", AutoSize = true };
    private readonly ContextMenuStrip _resultMenu = new ContextMenuStrip();

    public FilePanel(string title)
    {
        Title = title;
        Dock = DockStyle.Fill;
        AllowDrop = true;
        BuildLayout();
        WireEvents();
    }

    public string Title { get; }

    public string? FilePath { get; private set; }

    public HashResult? Result { get; private set; }

    public bool IsBusy { get; private set; }

    /// <summary>用户通过“浏览…”或拖放选好了文件，需要计算。</summary>
    public event EventHandler? FileChosen;

    /// <summary>点了“计算Hash值”。</summary>
    public event EventHandler? ComputeRequested;

    /// <summary>点了“导出结果”。</summary>
    public event EventHandler? ExportRequested;

    public void SetFilePath(string? path, bool raiseEvent = false)
    {
        FilePath = path;
        _pathBox.Text = path ?? string.Empty;
        SetResult(null);

        if (raiseEvent && !string.IsNullOrWhiteSpace(path))
            FileChosen?.Invoke(this, EventArgs.Empty);
    }

    public void SetResult(HashResult? result)
    {
        Result = result;
        _resultBox.Text = result?.ToDisplayText() ?? string.Empty;
        _resultBox.SelectionStart = 0;
        _resultBox.SelectionLength = 0;
    }

    public void SetBusy(bool busy)
    {
        IsBusy = busy;
        _browseButton.Enabled = !busy;
        _calcButton.Enabled = !busy;
        _exportButton.Enabled = !busy;
        _clearButton.Enabled = !busy;
        _copyButton.Enabled = !busy;
        Cursor = busy ? Cursors.AppStarting : Cursors.Default;
    }

    private void BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(6),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var pathRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 4),
        };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _pathBox.Dock = DockStyle.Fill;
        _pathBox.ReadOnly = true;
        _pathBox.PlaceholderText = "选择文件，或直接把文件拖到这里";
        _pathBox.Margin = new Padding(0, 3, 6, 3);

        pathRow.Controls.Add(_pathBox, 0, 0);
        pathRow.Controls.Add(_browseButton, 1, 0);

        _resultBox.Dock = DockStyle.Fill;
        _resultBox.Multiline = true;
        _resultBox.ReadOnly = true;
        _resultBox.WordWrap = false;               // 与导出 TXT 内容严格一致：不折行，横向滚动
        _resultBox.ScrollBars = ScrollBars.Both;
        _resultBox.Font = MonoFont;
        _resultBox.BackColor = Color.White;
        _resultBox.Margin = new Padding(0);

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, 4, 0, 0),
        };
        buttonRow.Controls.Add(_calcButton);
        buttonRow.Controls.Add(_exportButton);
        buttonRow.Controls.Add(_copyButton);
        buttonRow.Controls.Add(_clearButton);

        layout.Controls.Add(pathRow, 0, 0);
        layout.Controls.Add(_resultBox, 0, 1);
        layout.Controls.Add(buttonRow, 0, 2);

        Controls.Add(layout);
    }

    private void WireEvents()
    {
        _browseButton.Click += (_, _) => Browse();
        _calcButton.Click += (_, _) => ComputeRequested?.Invoke(this, EventArgs.Empty);
        _exportButton.Click += (_, _) => ExportRequested?.Invoke(this, EventArgs.Empty);
        _copyButton.Click += (_, _) => CopyToClipboard();
        _clearButton.Click += (_, _) => SetFilePath(null);
        _resultBox.MouseUp += OnResultMouseUp;

        foreach (var control in new Control[] { this, _pathBox, _resultBox })
        {
            control.AllowDrop = true;
            control.DragEnter += OnDragEnter;
            control.DragDrop += OnDragDrop;
        }
    }

    private void Browse()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择" + Title + "的文件",
            Filter = "所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };

        if (!string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(FilePath);
            dialog.FileName = Path.GetFileName(FilePath);
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
            SetFilePath(dialog.FileName, true);
    }

    private void CopyToClipboard()
    {
        if (string.IsNullOrEmpty(_resultBox.Text))
            return;

        try
        {
            Clipboard.SetText(_resultBox.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "复制失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>右键结果文本框：可只复制某一个算法的值（核对官网公布值时最常用）。</summary>
    private void OnResultMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || Result is null)
            return;

        _resultMenu.Items.Clear();

        var copyAll = new ToolStripMenuItem("复制全部内容");
        copyAll.Click += (_, _) => CopyToClipboard();
        _resultMenu.Items.Add(copyAll);
        _resultMenu.Items.Add(new ToolStripSeparator());

        foreach (var value in Result.Values)
        {
            var hex = value.Hex;
            var item = new ToolStripMenuItem(hex is null ? value.Label + "（不可用）" : "复制 " + value.Label + " 的值")
            {
                Enabled = hex is not null,
            };
            item.Click += (_, _) =>
            {
                if (hex is null)
                    return;

                try
                {
                    Clipboard.SetText(hex);
                }
                catch
                {
                    // 剪贴板被占用时忽略
                }
            };
            _resultMenu.Items.Add(item);
        }

        _resultMenu.Show(_resultBox, e.Location);
    }

    private static void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data is not null && e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            SetFilePath(files[0], true);
    }
}