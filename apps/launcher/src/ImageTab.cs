using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace BioshockVrLauncher
{
    internal sealed partial class MainForm
    {
        private const int ImageQualityPixelStep = 100;
        private ComboBox _imageQuality;
        private TextBox _imageInternalResolution;
        private Label _imageHint;
        private bool _refreshingSimpleImage;
        private bool _loadingGraphics;
        private readonly Dictionary<string, ComboBox> _graphicsOptions = new Dictionary<string, ComboBox>();
        // Nine tested settings; defaults disable the two largest observed costs.
        private static readonly string[][] ImageGraphics = new string[][] {
            new string[] { "HighDetailShaders", "Shaders de alto detalle", "" },
            new string[] { "Shadows", "Sombras", "Moderado impacto en rendimiento" },
            new string[] { "RealTimeReflection", "Reflejos", "Alto impacto en rendimiento" },
            new string[] { "PostProcessing", "Posprocesado", "" },
            new string[] { "UseRippleSystem", "Ondulaciones del agua", "Moderado impacto en rendimiento" },
            new string[] { "UseHighDetailSoftParticles", "Partículas de alta calidad", "" },
            new string[] { "UseDistortion", "Distorsión", "" },
            new string[] { "UseHighDetailPostProcEffects", "Posprocesado de alta calidad", "" },
            new string[] { "FluidSurfaceDetail", "Detalle de fluidos", "" }
        };

        private static int GraphicsDefault(string key)
        {
            return key == "RealTimeReflection" || key == "UseRippleSystem" ? 0 : 1;
        }

        private void ApplyGraphicsDefaults()
        {
            foreach (KeyValuePair<string, ComboBox> pair in _graphicsOptions)
                if (pair.Value.Enabled) pair.Value.SelectedIndex = GraphicsDefault(pair.Key);
        }

        private sealed class PixelQuality
        {
            internal int Pixels;
            internal int Output;
            public override string ToString()
            {
                return (100m * Pixels / Output).ToString("0.0", CultureInfo.CurrentCulture) + " %";
            }
        }

        private void AddResolutionTab()
        {
            InitializeImageBackingControls();
            TabPage page = _tabs.TabPages[_tabs.TabPages.Count - 1];
            // Keep backing controls owned/disposed by the form, but not visible or tabbable.
            Panel backing = new Panel();
            backing.Visible = false;
            backing.TabStop = false;
            while (page.Controls.Count > 0) backing.Controls.Add(page.Controls[0]);
            page.Controls.Add(backing);
            page.Padding = new Padding(6);

            TableLayoutPanel content = new TableLayoutPanel();
            content.Dock = DockStyle.Top;
            content.AutoSize = true;
            content.ColumnCount = 1;
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            page.Controls.Add(content);

            GroupBox image = new GroupBox();
            image.Text = "Imagen del visor";
            image.Dock = DockStyle.Fill;
            image.AutoSize = true;
            image.Padding = new Padding(8, 3, 8, 6);
            image.Margin = new Padding(0, 0, 0, 6);
            content.Controls.Add(image, 0, 0);
            TableLayoutPanel fields = new TableLayoutPanel();
            fields.AutoSize = true;
            fields.Dock = DockStyle.Top;
            fields.ColumnCount = 4;
            for (int i = 0; i < 4; i++) fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            image.Controls.Add(fields);
            string[] labels = { "Resolución del visor", "Modo de renderizado", "Calidad DLSS", "Resolución interna" };
            for (int i = 0; i < labels.Length; i++)
            {
                Label label = MakeCompactLabel(labels[i]);
                label.Padding = new Padding(0, 0, 4, 2);
                label.Margin = new Padding(0);
                fields.Controls.Add(label, i, 0);
            }

            _dlssOutputWidth.Increment = 100;
            _dlssOutputWidth.ThousandsSeparator = false;
            _dlssOutputWidth.Dock = DockStyle.Fill;
            _dlssOutputWidth.Margin = new Padding(0, 0, 8, 0);
            fields.Controls.Add(_dlssOutputWidth, 0, 1);
            _toolTip.SetToolTip(_dlssOutputWidth, "Píxeles por lado y por ojo. Se aplica el mismo valor a la anchura y la altura.");

            // Keep existing mode indices so the proven persistence logic is unchanged.
            _dlssMode.Items.Clear();
            _dlssMode.Items.AddRange(new object[] { "NORMAL", "DLAA", "DLSS" });
            _dlssMode.Dock = DockStyle.Fill;
            _dlssMode.Margin = new Padding(0, 0, 8, 0);
            fields.Controls.Add(_dlssMode, 1, 1);

            _imageQuality = new ComboBox();
            _imageQuality.DropDownStyle = ComboBoxStyle.DropDownList;
            _imageQuality.Dock = DockStyle.Fill;
            _imageQuality.Margin = new Padding(0, 0, 8, 0);
            _imageQuality.SelectedIndexChanged += ImageQualityChanged;
            fields.Controls.Add(_imageQuality, 2, 1);
            _toolTip.SetToolTip(_imageQuality, "Cada tramo cambia 100 píxeles de resolución interna por lado. El porcentaje se calcula respecto a la salida del visor.");

            _imageInternalResolution = new TextBox();
            _imageInternalResolution.ReadOnly = true;
            _imageInternalResolution.TabStop = false;
            _imageInternalResolution.Dock = DockStyle.Fill;
            _imageInternalResolution.Margin = new Padding(0);
            fields.Controls.Add(_imageInternalResolution, 3, 1);
            _imageHint = new Label();
            _imageHint.AutoSize = true;
            _imageHint.ForeColor = Muted;
            _imageHint.Margin = new Padding(0, 4, 0, 0);
            fields.SizeChanged += delegate { _imageHint.MaximumSize = new Size(Math.Max(200, fields.ClientSize.Width), 0); };
            fields.SetColumnSpan(_imageHint, 4);
            Label inGame = new Label();
            inGame.Text = GameProfile.IsBioShock2
                ? "F1: menú del visor · F2: anterior / − · F3: siguiente / +\nGráficos del juego: cámbialos aquí, guarda y reinicia."
                : "F1 abre el menú en el visor y navega por las opciones disponibles.\n" +
                  "F2: anterior / − · F3: siguiente / + · En opciones gráficas, F4 cambia el valor.";
            inGame.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            inGame.AutoSize = true;
            inGame.Margin = new Padding(0, 5, 0, 0);
            fields.SetColumnSpan(inGame, 4);
            fields.Controls.Add(inGame, 0, 2);
            fields.Controls.Add(_imageHint, 0, 3);

            GroupBox effects = new GroupBox();
            effects.Text = "Opciones gráficas del juego";
            effects.Dock = DockStyle.Fill;
            effects.AutoSize = true;
            effects.Padding = new Padding(8, 3, 8, 5);
            effects.Margin = new Padding(0);
            content.Controls.Add(effects, 0, 1);
            TableLayoutPanel options = new TableLayoutPanel();
            options.AutoSize = true;
            options.Dock = DockStyle.Top;
            options.ColumnCount = 4;
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            effects.Controls.Add(options);
            for (int row = 0; row < ImageGraphics.Length; row++)
            {
                string[] definition = ImageGraphics[row];
                Label name = new Label();
                name.Text = definition[1];
                name.AutoSize = true;
                name.Anchor = AnchorStyles.Left;
                name.Margin = new Padding(0, 1, 4, 1);
                name.ForeColor = SystemColors.ControlText;
                int column = row < 5 ? 0 : 2;
                int displayRow = row < 5 ? row : row - 5;
                FlowLayoutPanel caption = new FlowLayoutPanel();
                caption.AutoSize = true;
                caption.WrapContents = false;
                caption.Anchor = AnchorStyles.Left;
                caption.Margin = new Padding(0);
                caption.Controls.Add(name);
                if (definition[2].Length > 0)
                {
                    Label star = new Label();
                    star.Text = "*";
                    star.ForeColor = Color.Red;
                    star.AutoSize = true;
                    star.Margin = new Padding(0, 1, 0, 0);
                    caption.Controls.Add(star);
                }
                options.Controls.Add(caption, column, displayRow);
                ComboBox choice = new ComboBox();
                choice.Tag = definition[0];
                choice.DropDownStyle = ComboBoxStyle.DropDownList;
                choice.Dock = DockStyle.Fill;
                choice.Margin = new Padding(0, 1, column == 0 ? 14 : 0, 1);
                choice.Items.AddRange(definition[0] == "FluidSurfaceDetail"
                    ? new object[] { "Bajo", "Alto" } : new object[] { "Desactivado", "Activado" });
                choice.SelectedIndex = GraphicsDefault(definition[0]);
                choice.SelectedIndexChanged += GraphicsOptionChanged;
                _graphicsOptions.Add(definition[0], choice);
                options.Controls.Add(choice, column + 1, displayRow);
            }
            Label impact = new Label();
            impact.Text = "* Alto impacto en el Rendimiento";
            impact.AutoSize = true;
            impact.ForeColor = Color.Red;
            impact.Margin = new Padding(0, 4, 0, 0);
            options.Controls.Add(impact, 0, 5);
            options.SetColumnSpan(impact, 4);
            Button defaults = new Button();
            defaults.Text = "Valores predeterminados";
            defaults.AutoSize = true;
            defaults.UseVisualStyleBackColor = true;
            defaults.Margin = new Padding(0, 5, 0, 0);
            defaults.Click += delegate { ApplyGraphicsDefaults(); };
            options.Controls.Add(defaults, 0, 6);
            Label defaultsHint = new Label();
            defaultsHint.Text = "Reflejos y ondulaciones desactivados; resto activado.";
            defaultsHint.AutoSize = true;
            defaultsHint.ForeColor = Muted;
            defaultsHint.Anchor = AnchorStyles.Left;
            defaultsHint.Margin = new Padding(0, 5, 0, 0);
            options.Controls.Add(defaultsHint, 1, 6);
            options.SetColumnSpan(defaultsHint, 3);
        }

        private static List<PixelQuality> QualityPixelSteps(int output, int current)
        {
            List<PixelQuality> steps = new List<PixelQuality>();
            if (output <= 1024 || current < 1024 || current >= output) return steps;
            // Anchor to the current exact setting: opening the UI never rounds an
            // existing in-game/canonical/custom ratio to a different quality.
            int first = current;
            while (first - ImageQualityPixelStep >= 1024) first -= ImageQualityPixelStep;
            for (int pixels = first; pixels < output; pixels += ImageQualityPixelStep)
                steps.Add(new PixelQuality { Pixels = pixels, Output = output });
            return steps;
        }

        private bool PrepareSquareImage()
        {
            int width, height;
            if (_loading || !_dlssMode.Enabled || !_resolutionWidth.Enabled ||
                !_resolutionHeight.Enabled || !string.IsNullOrEmpty(_dlssLoadWarning) ||
                !TryGetRenderDimensions(out width, out height)) return false;
            bool entriesMatch = true;
            foreach (string key in new string[] { "WindowedViewportX", "WindowedViewportY", "FullscreenViewportX", "FullscreenViewportY" })
            {
                IniEntry entry = FindIniEntry("WinDrv.WindowsClient", key);
                int stored;
                if (entry == null || !int.TryParse(entry.Value, out stored) || stored != width) entriesMatch = false;
            }
            if (width == height && _dlssOutputWidth.Value == _dlssOutputHeight.Value && entriesMatch) return false;
            // A flat-screen INI can be rectangular. Prepare a square in memory,
            // using the displayed width; the normal Save path still owns all I/O.
            DlssChanged(_dlssOutputWidth, EventArgs.Empty);
            return _dlssOutputWidth.Value == _dlssOutputHeight.Value &&
                _resolutionWidth.Value == _resolutionHeight.Value;
        }

        private void RefreshSimpleImage()
        {
            if (_imageQuality == null || _refreshingSimpleImage) return;
            _refreshingSimpleImage = true;
            try
            {
                int renderWidth, renderHeight;
                bool hasRender = TryGetRenderDimensions(out renderWidth, out renderHeight);
                int output = (int)_dlssOutputWidth.Value;
                bool sr = _dlssMode.SelectedIndex == 2;
                int preferred, ignored;
                if (sr && hasRender) preferred = renderWidth;
                else if (!DlssQualityPolicy.TryCalculateRender(output, output,
                    _srScaleNumerator, _srScaleDenominator, out preferred, out ignored)) preferred = 0;
                _imageQuality.Items.Clear();
                foreach (PixelQuality step in QualityPixelSteps(output, preferred))
                {
                    int index = _imageQuality.Items.Add(step);
                    if (step.Pixels == preferred) _imageQuality.SelectedIndex = index;
                }
                _imageQuality.Enabled = hasRender && sr && _dlssMode.Enabled;
                _dlssOutputWidth.Enabled = hasRender && _dlssMode.Enabled;
                _imageInternalResolution.Text = hasRender
                    ? renderWidth + " × " + renderHeight : "Pendiente";
                _imageHint.Text = !hasRender
                    ? "Abre el juego una primera vez sin el mod y ciérralo; después pulsa Recargar."
                    : !string.IsNullOrEmpty(_dlssLoadWarning)
                        ? "Revisa la configuración de imagen: " + _dlssLoadWarning
                        : "Resolución cuadrada por ojo. Guarda los cambios con el juego cerrado.";
                _imageHint.Visible = !hasRender || !string.IsNullOrEmpty(_dlssLoadWarning);
            }
            finally { _refreshingSimpleImage = false; }
        }

        private void ImageQualityChanged(object sender, EventArgs e)
        {
            if (_loading || _refreshingSimpleImage || _dlssMode.SelectedIndex != 2) return;
            PixelQuality selected = _imageQuality.SelectedItem as PixelQuality;
            if (selected == null) return;
            int width, height;
            if (!TryApplyOutputAndScaleToRender(selected.Pixels, selected.Output, out width, out height)) return;
            _srScaleNumerator = selected.Pixels;
            _srScaleDenominator = selected.Output;
            SynchronizeDlssQualityFromResolution();
            _dlssDirty = true;
            _dlssLoadWarning = null;
            UpdateDlssSummary();
            UpdateDirtyState();
        }

        private void LoadGraphicsOptions()
        {
            _loadingGraphics = true;
            try
            {
                foreach (KeyValuePair<string, ComboBox> pair in _graphicsOptions)
                {
                    IniEntry entry = FindIniEntry("Engine.RenderConfig", pair.Key);
                    bool enabled;
                    bool valid = entry != null;
                    int selected = GraphicsDefault(pair.Key);
                    if (valid && pair.Key == "FluidSurfaceDetail")
                    {
                        string value = entry.Value.Trim().TrimEnd(';');
                        valid = string.Equals(value, "Low", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(value, "High", StringComparison.OrdinalIgnoreCase);
                        selected = string.Equals(value, "Low", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                    }
                    else if (valid)
                    {
                        valid = TryParseIniSwitch(entry.Value, out enabled);
                        selected = enabled ? 1 : 0;
                    }
                    pair.Value.SelectedIndex = selected;
                    pair.Value.Enabled = valid;
                    _toolTip.SetToolTip(pair.Value, valid ? string.Empty :
                        "Ajuste no disponible en la configuración actual; no se modifica automáticamente.");
                }
            }
            finally { _loadingGraphics = false; }
        }

        private void GraphicsOptionChanged(object sender, EventArgs e)
        {
            if (_loading || _loadingGraphics) return;
            ComboBox option = sender as ComboBox;
            if (option == null || !option.Enabled) return;
            string key = (string)option.Tag;
            IniEntry entry = FindIniEntry("Engine.RenderConfig", key);
            if (entry == null) return;
            entry.Value = key == "FluidSurfaceDetail" ? (option.SelectedIndex == 0 ? "Low" : "High")
                : FormatIniSwitch(entry.OriginalValue, option.SelectedIndex == 1);
            RecalculateGameIniDirty();
            RebuildIniGrid();
        }

        private void InitializeImageFixture()
        {
            _loading = true;
            string ini = "[WinDrv.WindowsClient]\r\nWindowedViewportX=2150\r\n" +
                "WindowedViewportY=2150\r\nFullscreenViewportX=2150\r\nFullscreenViewportY=2150\r\n" +
                "[Engine.RenderConfig]\r\nUseFxaa=0\r\n";
            foreach (string[] setting in ImageGraphics)
                ini += setting[0] + "=" + (setting[0] == "FluidSurfaceDetail" ? "High" : GraphicsDefault(setting[0]) == 1 ? "True" : "False") + "\r\n";
            ini += "[Other.Section]\r\nKeepMe=123\r\n";
            _gameIniLoadedContent = ini;
            _gameIniEntries = GameIniDocument.Parse(ini);
            _sharedIniEntries = GameIniDocument.Parse("[SharedOptions]\r\nViewportX=2150\r\nViewportY=2150\r\nStartupFullscreen=False\r\n");
            _dlssMode.SelectedIndex = 2;
            _previousDlssModeIndex = 2;
            _dlssOutputWidth.Value = _dlssOutputHeight.Value = 3072;
            _previousDlssOutputWidth = _previousDlssOutputHeight = 3072;
            _srScaleNumerator = 7;
            _srScaleDenominator = 10;
            _dlssSharpness.Value = 35;
            _dlssNearPlane.Value = 10;
            _dlssPreset.SelectedIndex = 0;
            LoadResolutionControls();
            SynchronizeDlssQualityFromResolution();
            LoadGraphicsOptions();
            PopulateEditors(new Dictionary<string, string>());
            _loading = false;
            UpdateDlssControlState();
            RefreshSimpleImage();
            _dirty = _vrDirty = _dlssDirty = _gameIniDirty = false;
        }

        internal static bool SimpleImageSelfTest()
        {
            using (MainForm form = new MainForm(true))
            {
                form.InitializeImageFixture();
                if (form._tabs.TabPages.Count != 7 || form._tabs.TabPages[0].Text != "Imagen" ||
                    form._graphicsOptions.Count != 9 || !form._imageInternalResolution.ReadOnly) return false;
                if (form._imageQuality.SelectedItem == null ||
                    ((PixelQuality)form._imageQuality.SelectedItem).Pixels != 2150 ||
                    form._srScaleNumerator != 7 || form._srScaleDenominator != 10) return false;
                for (int i = 1; i < form._imageQuality.Items.Count; i++)
                    if (((PixelQuality)form._imageQuality.Items[i]).Pixels -
                        ((PixelQuality)form._imageQuality.Items[i - 1]).Pixels != 100) return false;

                form._imageQuality.SelectedIndex++;
                if (form._resolutionWidth.Value != 2250 || form._resolutionHeight.Value != 2250 ||
                    form._dlssOutputWidth.Value != 3072 || form._dlssOutputHeight.Value != 3072 ||
                    form._srScaleNumerator != 2250 || form._srScaleDenominator != 3072) return false;
                form._dlssMode.SelectedIndex = 1;
                if (form._imageQuality.Enabled || form._resolutionWidth.Value != 3072) return false;
                form._dlssMode.SelectedIndex = 0;
                if (form._imageQuality.Enabled || form._resolutionWidth.Value != 3072) return false;
                form._dlssMode.SelectedIndex = 2;
                if (!form._imageQuality.Enabled || form._resolutionWidth.Value != 2250 ||
                    form._dlssNearPlane.Value != 10 || form._dlssSharpness.Value != 35) return false;
                form._dlssOutputWidth.Value = 3328;
                if (form._dlssOutputHeight.Value != 3328 ||
                    form._resolutionWidth.Value != form._resolutionHeight.Value) return false;
                string problem;
                if (!form.TryValidateDlssSettings(form.CollectDlssSettings(), out problem)) return false;

                form._loading = true;
                form._dlssOutputHeight.Value = 2400;
                form._resolutionHeight.Value = 1600;
                form._loading = false;
                if (!form.PrepareSquareImage() || form._dlssOutputHeight.Value != 3328 ||
                    form._resolutionWidth.Value != form._resolutionHeight.Value ||
                    form._dlssNearPlane.Value != 10 || form._dlssSharpness.Value != 35) return false;

                form._graphicsOptions["RealTimeReflection"].SelectedIndex = 0;
                form._graphicsOptions["FluidSurfaceDetail"].SelectedIndex = 0;
                string rendered = GameIniDocument.Render(form._gameIniLoadedContent, form._gameIniEntries);
                if (!rendered.Contains("RealTimeReflection=False") || !rendered.Contains("FluidSurfaceDetail=Low") ||
                    !rendered.Contains("Shadows=True") || !rendered.Contains("KeepMe=123") ||
                    !rendered.Contains("UseFxaa=0")) return false;
                form.LoadGraphicsOptions();
                if (form._graphicsOptions["RealTimeReflection"].SelectedIndex != 0) return false;
                form.ApplyGraphicsDefaults();
                if (form._dlssOutputWidth.Increment != 100 ||
                    form._graphicsOptions["RealTimeReflection"].SelectedIndex != 0 ||
                    form._graphicsOptions["UseRippleSystem"].SelectedIndex != 0 ||
                    form._graphicsOptions["Shadows"].SelectedIndex != 1 ||
                    form._graphicsOptions["FluidSurfaceDetail"].SelectedIndex != 1) return false;

                form._gameIniEntries = GameIniDocument.Parse("[Engine.RenderConfig]\r\nShadows=0;\r\n" +
                    "RealTimeReflection=True\r\nRealTimeReflection=False\r\n");
                form.LoadGraphicsOptions();
                if (form._graphicsOptions["RealTimeReflection"].Enabled ||
                    form._graphicsOptions["UseRippleSystem"].Enabled) return false;
                form._graphicsOptions["Shadows"].SelectedIndex = 1;
                if (form.FindIniEntry("Engine.RenderConfig", "Shadows").Value != "1;") return false;
                form._loading = true;
                form._gameIniEntries = GameIniDocument.Parse("[WinDrv.WindowsClient]\r\n" +
                    "WindowedViewportX=1280\r\nWindowedViewportY=720\r\n" +
                    "FullscreenViewportX=1280\r\nFullscreenViewportY=720\r\n");
                form._sharedIniEntries = GameIniDocument.Parse("[SharedOptions]\r\nViewportX=1280\r\nViewportY=720\r\n");
                form.LoadResolutionControls();
                form._dlssMode.SelectedIndex = 0;
                form._dlssOutputWidth.Value = 1280;
                form._dlssOutputHeight.Value = 1024;
                form._loading = false;
                if (!form.PrepareSquareImage() || form._resolutionWidth.Value != 1280 ||
                    form._resolutionHeight.Value != 1280 ||
                    form.FindIniEntry("WinDrv.WindowsClient", "WindowedViewportY").Value != "1280") return false;
                form._dirty = false;
                return QualityPixelSteps(1024, 1024).Count == 0 &&
                    QualityPixelSteps(3072, 2150).Count > 1;
            }
        }

        internal static void WriteImagePreview(string path, int tabIndex)
        {
            using (MainForm form = new MainForm(true))
            {
                form.InitializeImageFixture();
                form._configStateLabel.Text = "Configuración de ejemplo · no modifica archivos del juego";
                form._gameStateLabel.Text = GameProfile.DisplayName + " Remastered";
                form.SetStatus("Vista previa · 0.2.13 · sin modificar archivos del juego", SystemColors.ControlText);
                form.ShowInTaskbar = false;
                form.Opacity = 0;
                form.Show();
                form._tabs.SelectedIndex = tabIndex;
                Application.DoEvents();
                form.PerformLayout();
                using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(Path.GetFullPath(path), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
        }
    }
}
