using System.Drawing;
using System.Security;
using FolderIconChanger.Core;
using ImageMagick;

namespace FolderIconChanger.App;

internal sealed class MainForm : Form
{
    private static readonly Color PageColor = Color.FromArgb(245, 247, 251);
    private static readonly Color InkColor = Color.FromArgb(27, 39, 61);
    private static readonly Color MutedColor = Color.FromArgb(91, 105, 127);
    private static readonly Color AccentColor = Color.FromArgb(37, 99, 235);
    private static readonly Color SuccessColor = Color.FromArgb(20, 116, 77);

    private readonly FolderIconService _folderIconService = new();
    private readonly FolderIconWorkflow _folderIconWorkflow;
    private readonly bool _openedFromExplorer;
    private readonly TextBox _folderPath = new();
    private readonly TextBox _imagePath = new();
    private readonly PictureBox _preview = new();
    private readonly Label _status = new();
    private readonly Button _applyButton = new();
    private readonly Button _restoreButton = new();
    private readonly Button _thumbnailCacheButton = new();
    private readonly WindowsThumbnailCacheCleaner _thumbnailCacheCleaner = new();
    private readonly WindowsExplorerRestarter _explorerRestarter = new();

    public MainForm(string? initialFolder)
    {
        _folderIconWorkflow = new FolderIconWorkflow(_folderIconService);
        _openedFromExplorer = !string.IsNullOrWhiteSpace(initialFolder);
        InitializeWindow();
        BuildInterface();
        if (_openedFromExplorer)
        {
            _folderPath.Text = initialFolder!;
        }

        Shown += OnShown;
        FormClosed += (_, _) => _preview.Image?.Dispose();
    }

    private void InitializeWindow()
    {
        Text = "Folder Icon Changer";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
            ?? throw new InvalidOperationException("The application icon could not be loaded.");
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 850);
        Size = new Size(1100, 1000);
        BackColor = PageColor;
        ForeColor = InkColor;
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(28, 22, 28, 24),
            BackColor = PageColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var heading = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        heading.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        heading.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        heading.Controls.Add(new Label
        {
            Text = "Folder Icon Changer",
            Font = new Font("Segoe UI Semibold", 21F, FontStyle.Bold),
            ForeColor = InkColor,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill
        }, 0, 0);
        heading.Controls.Add(new Label
        {
            Text = "Choose a picture and give any folder a personal touch.",
            Font = new Font("Segoe UI", 10F),
            ForeColor = MutedColor,
            AutoSize = true,
            MaximumSize = new Size(880, 0),
            Dock = DockStyle.Fill
        }, 0, 1);
        root.Controls.Add(heading, 0, 0);

        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(22),
            BackColor = Color.White,
            Margin = new Padding(0, 10, 0, 0)
        };
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(card, 0, 1);

        card.Controls.Add(CreateFieldLabel("Folder to customize"), 0, 0);
        card.Controls.Add(CreatePathRow(_folderPath, "Browse folder", BrowseFolder), 0, 1);
        card.Controls.Add(CreateFieldLabel("Picture for the icon"), 0, 2);
        card.Controls.Add(CreatePathRow(_imagePath, "Browse image", BrowseImage), 0, 3);

        _preview.SizeMode = PictureBoxSizeMode.Zoom;
        _preview.BackColor = Color.FromArgb(248, 250, 253);
        _preview.BorderStyle = BorderStyle.FixedSingle;
        _preview.Dock = DockStyle.Fill;
        _preview.Margin = new Padding(0, 7, 0, 12);
        card.Controls.Add(_preview, 0, 4);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 4, 0, 0),
            Margin = Padding.Empty,
            AutoSize = true
        };
        _applyButton.Text = "Apply Folder Icon";
        _applyButton.AutoSize = true;
        _applyButton.MinimumSize = new Size(180, 40);
        _applyButton.BackColor = AccentColor;
        _applyButton.ForeColor = Color.White;
        _applyButton.FlatStyle = FlatStyle.Flat;
        _applyButton.FlatAppearance.BorderSize = 0;
        _applyButton.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
        _applyButton.Click += async (_, _) => await ApplySelectedIconAsync();
        buttons.Controls.Add(_applyButton);

        _restoreButton.Text = "Restore default icon";
        _restoreButton.AutoSize = true;
        _restoreButton.MinimumSize = new Size(178, 40);
        _restoreButton.Margin = new Padding(10, 0, 0, 0);
        _restoreButton.BackColor = Color.White;
        _restoreButton.ForeColor = InkColor;
        _restoreButton.FlatStyle = FlatStyle.Flat;
        _restoreButton.FlatAppearance.BorderColor = Color.FromArgb(208, 216, 228);
        _restoreButton.Click += async (_, _) => await RestoreIconAsync();
        buttons.Controls.Add(_restoreButton);

        _thumbnailCacheButton.Text = "Rebuild";
        _thumbnailCacheButton.AutoSize = true;
        _thumbnailCacheButton.MinimumSize = new Size(154, 40);
        _thumbnailCacheButton.Margin = new Padding(10, 0, 0, 0);
        _thumbnailCacheButton.BackColor = Color.White;
        _thumbnailCacheButton.ForeColor = InkColor;
        _thumbnailCacheButton.FlatStyle = FlatStyle.Flat;
        _thumbnailCacheButton.FlatAppearance.BorderColor = Color.FromArgb(208, 216, 228);
        _thumbnailCacheButton.AccessibleDescription =
            "Deletes this user's Windows thumbnail cache files and restarts Windows Explorer.";
        _thumbnailCacheButton.Click += async (_, _) => await RebuildThumbnailsAsync();
        buttons.Controls.Add(_thumbnailCacheButton);
        card.Controls.Add(buttons, 0, 5);

        _status.Text = "Your original folder settings are kept safe.";
        _status.ForeColor = MutedColor;
        _status.AutoSize = true;
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.AutoEllipsis = true;
        card.Controls.Add(_status, 0, 6);
    }

    private static Label CreateFieldLabel(string text)
    {
        return new Label
        {
            Text = text,
            ForeColor = InkColor,
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
            AutoSize = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        };
    }

    private Control CreatePathRow(TextBox textBox, string buttonText, EventHandler onClick)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = Size.Empty
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        textBox.ReadOnly = true;
        textBox.Dock = DockStyle.Fill;
        textBox.BackColor = Color.FromArgb(250, 251, 253);
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.Margin = new Padding(0, 3, 10, 3);
        textBox.AccessibleName = buttonText == "Browse folder" ? "Selected folder" : "Selected image";
        row.Controls.Add(textBox, 0, 0);

        var browseButton = new Button
        {
            Text = buttonText,
            Dock = DockStyle.Fill,
            AutoSize = true,
            MinimumSize = new Size(96, 0),
            Margin = new Padding(0, 2, 0, 2),
            BackColor = Color.White,
            ForeColor = InkColor,
            FlatStyle = FlatStyle.Flat
        };
        browseButton.FlatAppearance.BorderColor = Color.FromArgb(208, 216, 228);
        browseButton.Click += onClick;
        row.Controls.Add(browseButton, 1, 0);
        return row;
    }

    private void OnShown(object? sender, EventArgs e)
    {
        if (_openedFromExplorer)
        {
            BeginInvoke(new Action(async () => await SelectImageAndApplyAsync()));
        }
    }

    private void BrowseFolder(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the folder whose icon you want to change.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(_folderPath.Text) ? _folderPath.Text : string.Empty
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _folderPath.Text = dialog.SelectedPath;
            SetStatus("Folder selected.", MutedColor);
        }
    }

    private async void BrowseImage(object? sender, EventArgs e)
    {
        using var dialog = CreateImageDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await SetImageAsync(dialog.FileName);
    }

    private async Task SelectImageAndApplyAsync()
    {
        using var dialog = CreateImageDialog();
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            if (!await SetImageAsync(dialog.FileName))
            {
                return;
            }

            await ApplySelectedIconAsync();
        }
    }

    private static OpenFileDialog CreateImageDialog()
    {
        return new OpenFileDialog
        {
            Title = "Choose a picture for the folder icon",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff;*.ico|All files|*.*",
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true
        };
    }

    private async Task<bool> SetImageAsync(string imagePath)
    {
        try
        {
            var preview = await Task.Run(() => CreatePreview(imagePath));
            var previousPreview = _preview.Image;
            _preview.Image = preview;
            previousPreview?.Dispose();
            _imagePath.Text = imagePath;
            SetStatus("Picture ready.", MutedColor);
            return true;
        }
        catch (Exception exception)
        {
            ShowError(exception);
            return false;
        }
    }

    private static Bitmap CreatePreview(string imagePath)
    {
        using var image = new MagickImage(imagePath);
        image.AutoOrient();
        image.FilterType = FilterType.Lanczos;
        image.Resize(new MagickGeometry(256, 256) { FillArea = true });
        image.Extent(256, 256, Gravity.Center);
        image.Format = MagickFormat.Png;

        using var stream = new MemoryStream();
        image.Write(stream);
        stream.Position = 0;
        using var decoded = Image.FromStream(stream);
        return new Bitmap(decoded);
    }

    private async Task ApplySelectedIconAsync()
    {
        if (!Directory.Exists(_folderPath.Text))
        {
            ShowError(new FolderIconOperationException("Choose a folder that still exists."));
            return;
        }

        if (string.IsNullOrWhiteSpace(_imagePath.Text))
        {
            ShowError(new ImageProcessingException(
                "Choose a picture before applying a folder icon.",
                new InvalidOperationException()));
            return;
        }

        SetBusy(true, "Creating your folder icon…");
        try
        {
            await _folderIconWorkflow.ApplyAsync(
                _folderPath.Text,
                _imagePath.Text,
                _ => SetStatus("Folder icon changed successfully!", SuccessColor));
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RestoreIconAsync()
    {
        if (!Directory.Exists(_folderPath.Text))
        {
            ShowError(new FolderIconOperationException("Choose a folder that still exists."));
            return;
        }

        SetBusy(true, "Restoring the folder icon…");
        try
        {
            var restored = await _folderIconService.RestoreAsync(_folderPath.Text);
            SetStatus(
                restored ? "The folder's previous icon has been restored." : "This folder has no custom icon.",
                restored ? SuccessColor : MutedColor);
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RebuildThumbnailsAsync()
    {
        var confirmation = MessageBox.Show(
            this,
            "Rebuilding thumbnails clears this user's thumbnail cache and restarts Windows Explorer. " +
            "Open Explorer windows may close. Save work and finish file operations before continuing.",
            "Rebuild thumbnails",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        SetBusy(true, "Clearing this user's thumbnail cache…");
        try
        {
            var deletedCount = await _thumbnailCacheCleaner.ClearAsync();
            SetStatus("Restarting Windows Explorer…", MutedColor);
            await _explorerRestarter.RestartAsync();
            SetStatus(
                deletedCount == 0
                    ? "No thumbnail cache files were found. Windows Explorer was restarted."
                    : $"Deleted {deletedCount} thumbnail cache file(s) and restarted Windows Explorer.",
                SuccessColor);
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _applyButton.Enabled = !busy;
        _restoreButton.Enabled = !busy;
        _thumbnailCacheButton.Enabled = !busy;
        if (message is not null)
        {
            SetStatus(message, MutedColor);
        }
    }

    private void SetStatus(string message, Color color)
    {
        _status.Text = message;
        _status.ForeColor = color;
    }

    private void ShowError(Exception exception)
    {
        var message = exception switch
        {
            ImageProcessingException => exception.Message,
            FolderIconOperationException => exception.Message,
            UnauthorizedAccessException or SecurityException =>
                "Unable to access this folder. You may not have permission to change it.",
            DirectoryNotFoundException =>
                "Unable to access this folder. It may have been moved or removed.",
            IOException =>
                "Windows could not apply the new icon. Check that the folder is writable and try again.",
            MagickException =>
                "The selected image could not be processed. Choose another picture and try again.",
            _ =>
                "Windows could not apply the new icon. Check the folder and picture, then try again."
        };

        SetStatus(message, Color.FromArgb(171, 49, 49));
        MessageBox.Show(this, message, "Folder Icon Changer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
