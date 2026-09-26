using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace XDM.WinForms.IntegrationUI
{
    internal sealed class ExtensionGuideForm : Form
    {
        private static readonly Color WindowColor = Color.FromArgb(11, 17, 25);
        private static readonly Color PanelColor = Color.FromArgb(19, 28, 41);
        private static readonly Color BorderColor = Color.FromArgb(48, 68, 94);
        private static readonly Color TextColor = Color.FromArgb(237, 244, 255);
        private static readonly Color MutedColor = Color.FromArgb(143, 161, 184);
        private static readonly Color AccentColor = Color.FromArgb(36, 75, 117);

        private readonly BrowserProfile browser;
        private readonly IReadOnlyList<ExtensionPackage> extensions;

        public ExtensionGuideForm(BrowserProfile browser)
        {
            this.browser = browser;
            extensions = new[]
            {
                new ExtensionPackage(
                    "1. XDM Integration Module",
                    "Captures browser downloads and detected media for XDM.",
                    "chrome-extension"),
                new ExtensionPackage(
                    "2. XDM Download Controller",
                    "Shows active downloads, completion status, and download controls.",
                    "download-controller-extension")
            };

            InitializeWindow();
            Controls.Add(BuildLayout());
        }

        private void InitializeWindow()
        {
            Text = $"XDM 9 · Extensions for {browser.Name}";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(720, 540);
            ClientSize = new Size(760, 570);
            BackColor = WindowColor;
            ForeColor = TextColor;
            Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }

        private Control BuildLayout()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24),
                ColumnCount = 1,
                RowCount = 6,
                AutoScroll = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            layout.Controls.Add(CreateLabel("Install XDM browser extensions", 18F, FontStyle.Bold, TextColor), 0, 0);
            layout.Controls.Add(CreateLabel(
                $"{browser.Name}: enable Developer mode, then use Load unpacked once for each folder below.",
                10F, FontStyle.Regular, MutedColor), 0, 1);
            layout.Controls.Add(BuildBrowserRow(), 0, 2);

            var cards = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0, 14, 0, 0)
            };
            foreach (var extension in extensions) cards.Controls.Add(BuildExtensionCard(extension));
            layout.Controls.Add(cards, 0, 3);

            layout.Controls.Add(CreateLabel(
                "Both extensions are independent. Install both folders and pin both toolbar icons. " +
                "The Integration Module is blue; the Download Controller is purple.",
                9F, FontStyle.Regular, MutedColor), 0, 4);

            return layout;
        }

        private Control BuildBrowserRow()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 3,
                Margin = new Padding(0, 14, 0, 0)
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var address = CreatePathBox(browser.ExtensionPage);
            var copy = CreateButton("Copy address", (_, __) => CopyText(browser.ExtensionPage));
            var open = CreateButton("Open extensions page", (_, __) => OpenBrowserExtensions());
            row.Controls.Add(address, 0, 0);
            row.Controls.Add(copy, 1, 0);
            row.Controls.Add(open, 2, 0);
            return row;
        }

        private Control BuildExtensionCard(ExtensionPackage extension)
        {
            var card = new TableLayoutPanel
            {
                Width = 690,
                Height = 142,
                ColumnCount = 3,
                RowCount = 3,
                BackColor = PanelColor,
                Margin = new Padding(0, 0, 0, 10),
                Padding = new Padding(14),
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
            };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var title = CreateLabel(extension.Name, 11F, FontStyle.Bold, TextColor);
            card.SetColumnSpan(title, 3);
            card.Controls.Add(title, 0, 0);

            var description = CreateLabel(extension.Description, 9F, FontStyle.Regular, MutedColor);
            description.Text += extension.IsAvailable ? "  Ready to install." : "  Package is missing.";
            description.ForeColor = extension.IsAvailable ? MutedColor : Color.FromArgb(255, 133, 133);
            card.SetColumnSpan(description, 3);
            card.Controls.Add(description, 0, 1);

            var path = CreatePathBox(extension.FolderPath);
            var copy = CreateButton("Copy folder", (_, __) => CopyText(extension.FolderPath));
            var open = CreateButton("Open folder", (_, __) => OpenFolder(extension));
            copy.Enabled = open.Enabled = extension.IsAvailable;
            card.Controls.Add(path, 0, 2);
            card.Controls.Add(copy, 1, 2);
            card.Controls.Add(open, 2, 2);
            return card;
        }

        private static Label CreateLabel(string text, float size, FontStyle style, Color color)
        {
            return new Label
            {
                AutoSize = true,
                MaximumSize = new Size(690, 0),
                Margin = new Padding(0, 0, 0, 6),
                Text = text,
                Font = new Font("Segoe UI", size, style),
                ForeColor = color
            };
        }

        private static TextBox CreatePathBox(string text)
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Text = text,
                BackColor = Color.FromArgb(16, 26, 40),
                ForeColor = TextColor,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 5, 8, 0)
            };
        }

        private static Button CreateButton(string text, EventHandler click)
        {
            var button = new Button
            {
                AutoSize = true,
                Height = 30,
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentColor,
                ForeColor = TextColor,
                Margin = new Padding(0, 3, 8, 0),
                Padding = new Padding(8, 2, 8, 2),
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderColor = BorderColor;
            button.Click += click;
            return button;
        }

        private void OpenBrowserExtensions()
        {
            var executable = browser.FindExecutable();
            if (executable == null)
            {
                CopyText(browser.ExtensionPage);
                MessageBox.Show(
                    $"XDM could not locate {browser.Name}. The extensions-page address was copied instead.",
                    "XDM Extension Guide", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = browser.ExtensionPage,
                UseShellExecute = true
            });
        }

        private static void OpenFolder(ExtensionPackage extension)
        {
            if (!extension.IsAvailable) return;
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{extension.FolderPath}\"",
                UseShellExecute = true
            });
        }

        private static void CopyText(string text)
        {
            Clipboard.SetText(text);
        }
    }
}
