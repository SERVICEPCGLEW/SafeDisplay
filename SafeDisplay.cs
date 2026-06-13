using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO;
using System.Collections.Generic;
using Microsoft.Win32;

namespace SafeDisplay
{
    public class Program
    {
        [DllImport("shcore.dll", EntryPoint = "SetProcessDpiAwareness")]
        private static extern int SetProcessDpiAwareness(int value);

        [DllImport("user32.dll", EntryPoint = "SetProcessDPIAware")]
        private static extern bool SetProcessDPIAware();

        private static void TrySetDpiAware()
        {
            try
            {
                // Try calling SetProcessDpiAwareness (Windows 8.1+)
                // Process_Per_Monitor_DPI_Aware = 2
                SetProcessDpiAwareness(2);
            }
            catch
            {
                try
                {
                    // Fallback for older Windows (Vista/7/8)
                    SetProcessDPIAware();
                }
                catch {}
            }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            Application.ThreadException += (s, e) => {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
                File.WriteAllText(logPath, "Thread Exception: " + e.Exception.ToString());
            };
            
            AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
                File.WriteAllText(logPath, "Unhandled Exception: " + e.ExceptionObject.ToString());
            };

            try
            {
                TrySetDpiAware();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                
                bool startMinimized = false;
                if (args.Length > 0 && (args[0] == "--startup" || args[0] == "/startup"))
                {
                    startMinimized = true;
                }
                
                Application.Run(new MainForm(startMinimized));
            }
            catch (Exception ex)
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
                File.WriteAllText(logPath, ex.ToString());
                throw;
            }
        }
    }

    public class MainForm : Form
    {
        // Custom styling colors
        private static readonly Color ColorBg = Color.FromArgb(30, 30, 36);
        private static readonly Color ColorHeader = Color.FromArgb(17, 17, 22);
        private static readonly Color ColorCard = Color.FromArgb(40, 40, 48);
        private static readonly Color ColorAccent = Color.FromArgb(0, 173, 181);
        private static readonly Color ColorTextLight = Color.FromArgb(238, 238, 238);
        private static readonly Color ColorTextDim = Color.FromArgb(170, 170, 180);
        private static readonly Color ColorRed = Color.FromArgb(231, 76, 60);

        // Win32 API functions for dragging window
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint RegisterWindowMessage(string lpString);

        // GUI controls
        private Panel headerPanel;
        private Label titleLabel;
        private Button closeButton;
        private Button minimizeButton;
        
        private ComboBox monitorComboBox;
        private ComboBox presetComboBox;
        private TrackBar topTrackBar;
        private TrackBar bottomTrackBar;
        private TrackBar leftTrackBar;
        private TrackBar rightTrackBar;
        
        private Label topValLabel;
        private Label bottomValLabel;
        private Label leftValLabel;
        private Label rightValLabel;
        
        private CheckBox startupCheckBox;
        private CheckBox closeToTrayCheckBox;
        private CheckBox blockCursorCheckBox;
        private CheckBox integrateTaskbarCheckBox;
        private Button applyButton;
        private Button resetButton;
        private Panel previewPanel;
        
        private NotifyIcon trayIcon;
        private ContextMenu trayMenu;

        // State variables
        private Screen selectedScreen;
        private int currentTopMargin = 0;
        private int currentBottomMargin = 0;
        private int currentLeftMargin = 0;
        private int currentRightMargin = 0;
        private int currentDimmerOpacity = 0;
        private bool isUpdatingSliders = false;
        
        private TrackBar dimmerTrackBar;
        private Label dimmerValLabel;
        private Dictionary<string, DimmerWindow> activeDimmers = new Dictionary<string, DimmerWindow>();
        
        private Dictionary<string, MarginConfig> configMap = new Dictionary<string, MarginConfig>();
        private string configPath;
        private bool startMinimized = false;
        private bool isExiting = false;
        private uint taskbarCreatedMsg;

        private List<AppBarWindow> activeAppBars = new List<AppBarWindow>();
        private System.Windows.Forms.Timer mouseTimer;

        public class MarginConfig
        {
            public string DeviceName { get; set; }
            public int Top { get; set; }
            public int Bottom { get; set; }
            public int Left { get; set; }
            public int Right { get; set; }
            public int DimmerOpacity { get; set; }
            public bool IntegrateTaskbar { get; set; }
        }

        public MainForm(bool startMinimized)
        {
            this.startMinimized = startMinimized;
            configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");

            // Form properties
            this.Size = new Size(720, 520);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = ColorBg;
            this.Text = "SafeDisplay - Corrección de Pantalla";

            InitializeGUI();
            LoadConfig();
            
            // Populate monitors
            PopulateMonitors();

            // Set up initial active AppBars
            UpdateAppBars();
            UpdateDimmers();

            // Set up dynamic cursor barrier timer
            mouseTimer = new System.Windows.Forms.Timer();
            mouseTimer.Interval = 10; // 10ms check for smooth barrier physics
            mouseTimer.Tick += MouseTimer_Tick;
            mouseTimer.Start();

            // Register system display settings change event
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == taskbarCreatedMsg)
            {
                // Explorer restarted! Re-register AppBars to restore boundaries
                UpdateAppBars();
            }
            base.WndProc(ref m);
        }

        private void InitializeGUI()
        {
            // Custom Title Bar
            headerPanel = new Panel();
            headerPanel.Size = new Size(this.Width, 40);
            headerPanel.Location = new Point(0, 0);
            headerPanel.BackColor = ColorHeader;
            headerPanel.MouseDown += Header_MouseDown;
            this.Controls.Add(headerPanel);

            titleLabel = new Label();
            titleLabel.Text = "⚡ SafeDisplay // Corrector de Pantalla";
            titleLabel.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            titleLabel.ForeColor = ColorAccent;
            titleLabel.AutoSize = true;
            titleLabel.Location = new Point(12, 10);
            titleLabel.MouseDown += Header_MouseDown;
            headerPanel.Controls.Add(titleLabel);

            closeButton = new Button();
            closeButton.Text = "✕";
            closeButton.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            closeButton.ForeColor = ColorTextLight;
            closeButton.BackColor = Color.Transparent;
            closeButton.FlatStyle = FlatStyle.Flat;
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.FlatAppearance.MouseOverBackColor = ColorRed;
            closeButton.Size = new Size(40, 40);
            closeButton.Location = new Point(this.Width - 40, 0);
            closeButton.Click += CloseButton_Click;
            headerPanel.Controls.Add(closeButton);

            minimizeButton = new Button();
            minimizeButton.Text = "—";
            minimizeButton.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            minimizeButton.ForeColor = ColorTextLight;
            minimizeButton.BackColor = Color.Transparent;
            minimizeButton.FlatStyle = FlatStyle.Flat;
            minimizeButton.FlatAppearance.BorderSize = 0;
            minimizeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(50, 50, 60);
            minimizeButton.Size = new Size(40, 40);
            minimizeButton.Location = new Point(this.Width - 80, 0);
            minimizeButton.Click += (s, e) => { this.WindowState = FormWindowState.Minimized; };
            headerPanel.Controls.Add(minimizeButton);

            // Left panel (Controls Area)
            Panel leftPanel = new Panel();
            leftPanel.Size = new Size(330, this.Height - 40);
            leftPanel.Location = new Point(0, 40);
            leftPanel.BackColor = ColorBg;
            this.Controls.Add(leftPanel);

            Label monitorLabel = new Label();
            monitorLabel.Text = "Selecciona la Pantalla (TV):";
            monitorLabel.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            monitorLabel.ForeColor = ColorTextLight;
            monitorLabel.Location = new Point(20, 10);
            monitorLabel.Size = new Size(200, 20);
            leftPanel.Controls.Add(monitorLabel);

            monitorComboBox = new ComboBox();
            monitorComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            monitorComboBox.Font = new Font("Segoe UI", 9);
            monitorComboBox.BackColor = ColorCard;
            monitorComboBox.ForeColor = ColorTextLight;
            monitorComboBox.FlatStyle = FlatStyle.Flat;
            monitorComboBox.Location = new Point(20, 32);
            monitorComboBox.Size = new Size(290, 25);
            monitorComboBox.SelectedIndexChanged += MonitorComboBox_SelectedIndexChanged;
            leftPanel.Controls.Add(monitorComboBox);

            // Presets Dropdown
            Label presetLabel = new Label();
            presetLabel.Text = "Preajuste de Tamaño (Monitor 27\" 2K):";
            presetLabel.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            presetLabel.ForeColor = ColorTextLight;
            presetLabel.Location = new Point(20, 65);
            presetLabel.Size = new Size(250, 18);
            leftPanel.Controls.Add(presetLabel);

            presetComboBox = new ComboBox();
            presetComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            presetComboBox.Font = new Font("Segoe UI", 9);
            presetComboBox.BackColor = ColorCard;
            presetComboBox.ForeColor = ColorTextLight;
            presetComboBox.FlatStyle = FlatStyle.Flat;
            presetComboBox.Location = new Point(20, 85);
            presetComboBox.Size = new Size(290, 25);
            presetComboBox.Items.AddRange(new object[] {
                "Personalizado (Manual)",
                "24.0\" Centrado (Barra integrada)",
                "24.0\" Alineado Abajo (Barra integrada)",
                "24.5\" Centrado (Barra integrada)",
                "24.5\" Alineado Abajo (Barra integrada)",
                "23.8\" Centrado (Barra integrada)",
                "23.8\" Alineado Abajo (Barra integrada)"
            });
            presetComboBox.SelectedIndex = 0;
            presetComboBox.SelectedIndexChanged += PresetComboBox_SelectedIndexChanged;
            leftPanel.Controls.Add(presetComboBox);

            // Sliders Container
            Panel slidersCard = new Panel();
            slidersCard.Size = new Size(290, 220);
            slidersCard.Location = new Point(20, 120);
            slidersCard.BackColor = ColorCard;
            leftPanel.Controls.Add(slidersCard);

            // Top Margin Slider
            Label topLabel = new Label();
            topLabel.Text = "Margen Superior (Franja Rota)";
            topLabel.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            topLabel.ForeColor = ColorTextDim;
            topLabel.Location = new Point(15, 10);
            topLabel.Size = new Size(180, 15);
            slidersCard.Controls.Add(topLabel);

            topValLabel = new Label();
            topValLabel.Text = "0 px";
            topValLabel.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            topValLabel.ForeColor = ColorAccent;
            topValLabel.Location = new Point(230, 10);
            topValLabel.Size = new Size(50, 15);
            topValLabel.TextAlign = ContentAlignment.TopRight;
            slidersCard.Controls.Add(topValLabel);

            topTrackBar = new TrackBar();
            topTrackBar.Minimum = 0;
            topTrackBar.Maximum = 720;
            topTrackBar.TickStyle = TickStyle.None;
            topTrackBar.AutoSize = false;
            topTrackBar.Location = new Point(10, 28);
            topTrackBar.Size = new Size(270, 22);
            topTrackBar.Scroll += (s, e) => {
                currentTopMargin = topTrackBar.Value;
                topValLabel.Text = currentTopMargin + " px";
                OnSliderScrolled();
                previewPanel.Invalidate();
            };
            slidersCard.Controls.Add(topTrackBar);

            // Bottom Margin Slider
            Label bottomLabel = new Label();
            bottomLabel.Text = "Margen Inferior";
            bottomLabel.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            bottomLabel.ForeColor = ColorTextDim;
            bottomLabel.Location = new Point(15, 58);
            bottomLabel.Size = new Size(180, 15);
            slidersCard.Controls.Add(bottomLabel);

            bottomValLabel = new Label();
            bottomValLabel.Text = "0 px";
            bottomValLabel.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            bottomValLabel.ForeColor = ColorAccent;
            bottomValLabel.Location = new Point(230, 58);
            bottomValLabel.Size = new Size(50, 15);
            bottomValLabel.TextAlign = ContentAlignment.TopRight;
            slidersCard.Controls.Add(bottomValLabel);

            bottomTrackBar = new TrackBar();
            bottomTrackBar.Minimum = 0;
            bottomTrackBar.Maximum = 720;
            bottomTrackBar.TickStyle = TickStyle.None;
            bottomTrackBar.AutoSize = false;
            bottomTrackBar.Location = new Point(10, 76);
            bottomTrackBar.Size = new Size(270, 22);
            bottomTrackBar.Scroll += (s, e) => {
                currentBottomMargin = bottomTrackBar.Value;
                bottomValLabel.Text = currentBottomMargin + " px";
                OnSliderScrolled();
                previewPanel.Invalidate();
            };
            slidersCard.Controls.Add(bottomTrackBar);

            // Left Margin Slider
            Label leftLabel = new Label();
            leftLabel.Text = "Margen Izquierdo";
            leftLabel.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            leftLabel.ForeColor = ColorTextDim;
            leftLabel.Location = new Point(15, 106);
            leftLabel.Size = new Size(180, 15);
            slidersCard.Controls.Add(leftLabel);

            leftValLabel = new Label();
            leftValLabel.Text = "0 px";
            leftValLabel.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            leftValLabel.ForeColor = ColorAccent;
            leftValLabel.Location = new Point(230, 106);
            leftValLabel.Size = new Size(50, 15);
            leftValLabel.TextAlign = ContentAlignment.TopRight;
            slidersCard.Controls.Add(leftValLabel);

            leftTrackBar = new TrackBar();
            leftTrackBar.Minimum = 0;
            leftTrackBar.Maximum = 1280;
            leftTrackBar.TickStyle = TickStyle.None;
            leftTrackBar.AutoSize = false;
            leftTrackBar.Location = new Point(10, 124);
            leftTrackBar.Size = new Size(270, 22);
            leftTrackBar.Scroll += (s, e) => {
                currentLeftMargin = leftTrackBar.Value;
                leftValLabel.Text = currentLeftMargin + " px";
                OnSliderScrolled();
                previewPanel.Invalidate();
            };
            slidersCard.Controls.Add(leftTrackBar);

            // Right Margin Slider
            Label rightLabel = new Label();
            rightLabel.Text = "Margen Derecho";
            rightLabel.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            rightLabel.ForeColor = ColorTextDim;
            rightLabel.Location = new Point(15, 154);
            rightLabel.Size = new Size(180, 15);
            slidersCard.Controls.Add(rightLabel);

            rightValLabel = new Label();
            rightValLabel.Text = "0 px";
            rightValLabel.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            rightValLabel.ForeColor = ColorAccent;
            rightValLabel.Location = new Point(230, 154);
            rightValLabel.Size = new Size(50, 15);
            rightValLabel.TextAlign = ContentAlignment.TopRight;
            slidersCard.Controls.Add(rightValLabel);

            rightTrackBar = new TrackBar();
            rightTrackBar.Minimum = 0;
            rightTrackBar.Maximum = 1280;
            rightTrackBar.TickStyle = TickStyle.None;
            rightTrackBar.AutoSize = false;
            rightTrackBar.Location = new Point(10, 172);
            rightTrackBar.Size = new Size(270, 22);
            rightTrackBar.Scroll += (s, e) => {
                currentRightMargin = rightTrackBar.Value;
                rightValLabel.Text = currentRightMargin + " px";
                OnSliderScrolled();
                previewPanel.Invalidate();
            };
            slidersCard.Controls.Add(rightTrackBar);

            // Extra checkboxes
            startupCheckBox = new CheckBox();
            startupCheckBox.Text = "Iniciar automáticamente con Windows";
            startupCheckBox.Font = new Font("Segoe UI", 8);
            startupCheckBox.ForeColor = ColorTextDim;
            startupCheckBox.Location = new Point(20, 350);
            startupCheckBox.Size = new Size(290, 18);
            startupCheckBox.FlatStyle = FlatStyle.Flat;
            startupCheckBox.CheckedChanged += StartupCheckBox_CheckedChanged;
            leftPanel.Controls.Add(startupCheckBox);

            closeToTrayCheckBox = new CheckBox();
            closeToTrayCheckBox.Text = "Minimizar a la bandeja al cerrar (X)";
            closeToTrayCheckBox.Font = new Font("Segoe UI", 8);
            closeToTrayCheckBox.ForeColor = ColorTextDim;
            closeToTrayCheckBox.Location = new Point(20, 370);
            closeToTrayCheckBox.Size = new Size(290, 18);
            closeToTrayCheckBox.FlatStyle = FlatStyle.Flat;
            closeToTrayCheckBox.Checked = true;
            leftPanel.Controls.Add(closeToTrayCheckBox);

            blockCursorCheckBox = new CheckBox();
            blockCursorCheckBox.Text = "Bloquear mouse en la franja rota (Barrera)";
            blockCursorCheckBox.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            blockCursorCheckBox.ForeColor = ColorAccent;
            blockCursorCheckBox.Location = new Point(20, 390);
            blockCursorCheckBox.Size = new Size(290, 18);
            blockCursorCheckBox.FlatStyle = FlatStyle.Flat;
            blockCursorCheckBox.Checked = true;
            blockCursorCheckBox.CheckedChanged += (s, e) => SaveConfig();
            leftPanel.Controls.Add(blockCursorCheckBox);

            integrateTaskbarCheckBox = new CheckBox();
            integrateTaskbarCheckBox.Text = "Integrar barra de tareas en margen inferior";
            integrateTaskbarCheckBox.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            integrateTaskbarCheckBox.ForeColor = ColorAccent;
            integrateTaskbarCheckBox.Location = new Point(20, 410);
            integrateTaskbarCheckBox.Size = new Size(290, 18);
            integrateTaskbarCheckBox.FlatStyle = FlatStyle.Flat;
            integrateTaskbarCheckBox.Checked = false;
            integrateTaskbarCheckBox.CheckedChanged += (s, e) => {
                OnSliderScrolled();
                SaveConfig();
            };
            leftPanel.Controls.Add(integrateTaskbarCheckBox);

            // Action Buttons
            applyButton = new Button();
            applyButton.Text = "APLICAR MÁRGENES";
            applyButton.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            applyButton.BackColor = ColorAccent;
            applyButton.ForeColor = ColorHeader;
            applyButton.FlatStyle = FlatStyle.Flat;
            applyButton.FlatAppearance.BorderSize = 0;
            applyButton.Size = new Size(180, 35);
            applyButton.Location = new Point(20, 445);
            applyButton.Click += ApplyButton_Click;
            leftPanel.Controls.Add(applyButton);

            resetButton = new Button();
            resetButton.Text = "RESTABLECER";
            resetButton.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            resetButton.BackColor = Color.FromArgb(55, 55, 65);
            resetButton.ForeColor = ColorTextLight;
            resetButton.FlatStyle = FlatStyle.Flat;
            resetButton.FlatAppearance.BorderSize = 0;
            resetButton.Size = new Size(100, 35);
            resetButton.Location = new Point(210, 445);
            resetButton.Click += ResetButton_Click;
            leftPanel.Controls.Add(resetButton);

            // Right panel (Preview Area)
            Panel rightPanel = new Panel();
            rightPanel.Size = new Size(390, this.Height - 40);
            rightPanel.Location = new Point(330, 40);
            rightPanel.BackColor = Color.FromArgb(22, 22, 28);
            this.Controls.Add(rightPanel);

            Label previewTitle = new Label();
            previewTitle.Text = "VISTA PREVIA DE PANTALLA EN VIVO";
            previewTitle.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            previewTitle.ForeColor = ColorTextDim;
            previewTitle.Location = new Point(20, 15);
            previewTitle.Size = new Size(300, 20);
            rightPanel.Controls.Add(previewTitle);

            // Live Preview Canvas
            previewPanel = new Panel();
            previewPanel.Size = new Size(350, 210);
            previewPanel.Location = new Point(20, 38);
            previewPanel.BackColor = Color.FromArgb(12, 12, 16);
            previewPanel.Paint += PreviewPanel_Paint;
            rightPanel.Controls.Add(previewPanel);

            // Dimmer card
            Panel dimmerCard = new Panel();
            dimmerCard.Size = new Size(350, 85);
            dimmerCard.Location = new Point(20, 270);
            dimmerCard.BackColor = ColorCard;
            rightPanel.Controls.Add(dimmerCard);

            Label dimmerTitle = new Label();
            dimmerTitle.Text = "Atenuador de Pantalla Nocturno";
            dimmerTitle.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            dimmerTitle.ForeColor = ColorTextLight;
            dimmerTitle.Location = new Point(15, 10);
            dimmerTitle.Size = new Size(200, 15);
            dimmerCard.Controls.Add(dimmerTitle);

            dimmerValLabel = new Label();
            dimmerValLabel.Text = "0%";
            dimmerValLabel.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            dimmerValLabel.ForeColor = ColorAccent;
            dimmerValLabel.Location = new Point(285, 10);
            dimmerValLabel.Size = new Size(50, 15);
            dimmerValLabel.TextAlign = ContentAlignment.TopRight;
            dimmerCard.Controls.Add(dimmerValLabel);

            dimmerTrackBar = new TrackBar();
            dimmerTrackBar.Minimum = 0;
            dimmerTrackBar.Maximum = 90;
            dimmerTrackBar.TickStyle = TickStyle.None;
            dimmerTrackBar.Location = new Point(10, 32);
            dimmerTrackBar.Size = new Size(330, 30);
            dimmerTrackBar.Scroll += (s, e) => {
                currentDimmerOpacity = dimmerTrackBar.Value;
                dimmerValLabel.Text = currentDimmerOpacity + "%";
                previewPanel.Invalidate();
                
                // Real-time update
                if (selectedScreen != null)
                {
                    if (!configMap.ContainsKey(selectedScreen.DeviceName))
                    {
                        configMap[selectedScreen.DeviceName] = new MarginConfig();
                    }
                    configMap[selectedScreen.DeviceName].DeviceName = selectedScreen.DeviceName;
                    configMap[selectedScreen.DeviceName].DimmerOpacity = currentDimmerOpacity;
                    UpdateDimmers();
                }
            };
            dimmerCard.Controls.Add(dimmerTrackBar);

            // Legend card
            Panel legendCard = new Panel();
            legendCard.Size = new Size(350, 75);
            legendCard.Location = new Point(20, 365);
            legendCard.BackColor = ColorCard;
            rightPanel.Controls.Add(legendCard);

            Label infoTitle = new Label();
            infoTitle.Text = "Información del Área de Trabajo";
            infoTitle.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            infoTitle.ForeColor = ColorTextLight;
            infoTitle.Location = new Point(15, 8);
            infoTitle.Size = new Size(200, 15);
            legendCard.Controls.Add(infoTitle);

            // Legend indicators
            Panel redDot = new Panel() { Size = new Size(10, 10), Location = new Point(15, 30), BackColor = ColorRed };
            legendCard.Controls.Add(redDot);
            Label redText = new Label() { Text = "Zona Excluida (Borde de pantalla)", ForeColor = ColorTextDim, Font = new Font("Segoe UI", 8), Location = new Point(32, 28), Size = new Size(300, 15) };
            legendCard.Controls.Add(redText);

            Panel tealDot = new Panel() { Size = new Size(10, 10), Location = new Point(15, 50), BackColor = ColorAccent };
            legendCard.Controls.Add(tealDot);
            Label tealText = new Label() { Text = "Área Segura Utilizable", ForeColor = ColorTextDim, Font = new Font("Segoe UI", 8), Location = new Point(32, 48), Size = new Size(300, 15) };
            legendCard.Controls.Add(tealText);

            Button aboutButton = new Button();
            aboutButton.Text = "Acerca de...";
            aboutButton.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            aboutButton.BackColor = Color.FromArgb(55, 55, 65);
            aboutButton.ForeColor = ColorTextLight;
            aboutButton.FlatStyle = FlatStyle.Flat;
            aboutButton.FlatAppearance.BorderSize = 0;
            aboutButton.Size = new Size(100, 25);
            aboutButton.Location = new Point(270, 448);
            aboutButton.Click += (s, e) => {
                DialogResult res = MessageBox.Show("SafeDisplay - Corrección de Pantalla\n\n© Service PC Glew 2026\n\n¿Deseas visitar nuestro GitHub para más proyectos?", "Acerca de...", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (res == DialogResult.Yes) {
                    System.Diagnostics.Process.Start("https://github.com/servicepcglew");
                }
            };
            rightPanel.Controls.Add(aboutButton);

            Button donateButton = new Button();
            donateButton.Text = "👍 Apoyar en Matecito";
            donateButton.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            donateButton.BackColor = Color.FromArgb(245, 124, 0); // Orange / Naranja
            donateButton.ForeColor = Color.White;
            donateButton.FlatStyle = FlatStyle.Flat;
            donateButton.FlatAppearance.BorderSize = 0;
            donateButton.Size = new Size(150, 25);
            donateButton.Location = new Point(20, 448);
            donateButton.Cursor = Cursors.Hand;
            donateButton.Click += (s, e) => {
                // Abre el link de donación en el navegador (cafecito/matecito)
                System.Diagnostics.Process.Start("https://cafecito.app/servicepcglew");
            };
            rightPanel.Controls.Add(donateButton);


            // System Tray Setup
            trayMenu = new ContextMenu();
            trayMenu.MenuItems.Add("Mostrar Configuración", (s, e) => ShowMainForm());
            trayMenu.MenuItems.Add("Restablecer Todo", (s, e) => {
                ResetAllMargins();
                MessageBox.Show("Márgenes restablecidos por completo.", "SafeDisplay", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
            trayMenu.MenuItems.Add("-");
            trayMenu.MenuItems.Add("Salir", (s, e) => ExitApplication());

            trayIcon = new NotifyIcon();
            trayIcon.Text = "SafeDisplay - Administrador de Pantalla";
            trayIcon.Icon = SystemIcons.Application; // Default icon
            trayIcon.ContextMenu = trayMenu;
            trayIcon.DoubleClick += (s, e) => ShowMainForm();
            trayIcon.Visible = true;
        }

        private void Header_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
            }
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            if (closeToTrayCheckBox.Checked)
            {
                HideToTray();
            }
            else
            {
                ExitApplication();
            }
        }

        private void HideToTray()
        {
            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;
            this.Hide();
            
            trayIcon.ShowBalloonTip(3000, "SafeDisplay Activo", "La aplicación se está ejecutando en segundo plano para mantener los márgenes de pantalla.", ToolTipIcon.Info);
        }

        private void ShowMainForm()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.ShowInTaskbar = true;
            this.Activate();
        }

        private void ExitApplication()
        {
            isExiting = true;
            mouseTimer.Stop();
            SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            
            // Restore original work areas by removing all AppBars
            CloseAllAppBars();
            CloseAllDimmers();

            trayIcon.Visible = false;
            trayIcon.Dispose();
            Application.Exit();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (startMinimized)
            {
                HideToTray();
            }
        }

        private void OnSliderScrolled()
        {
            if (isUpdatingSliders) return;
            isUpdatingSliders = true;
            try
            {
                presetComboBox.SelectedIndex = 0; // Manual
            }
            finally
            {
                isUpdatingSliders = false;
            }
        }

        private void PresetComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (isUpdatingSliders) return;
            int idx = presetComboBox.SelectedIndex;
            if (idx <= 0) return; // Personalizado / Manual
            isUpdatingSliders = true;
            try
            {
                int top = 0, bottom = 0, left = 0, right = 0;
                bool integrate = false;
                if (idx == 1) // 24.0" Centrado (Barra integrada)
                {
                    left = 142; right = 142; top = 80; bottom = 80; integrate = true;
                }
                else if (idx == 2) // 24.0" Alineado Abajo (Barra integrada)
                {
                    left = 142; right = 142; top = 160; bottom = 0; integrate = true;
                }
                else if (idx == 3) // 24.5" Centrado (Barra integrada)
                {
                    left = 118; right = 118; top = 67; bottom = 67; integrate = true;
                }
                else if (idx == 4) // 24.5" Alineado Abajo (Barra integrada)
                {
                    left = 118; right = 118; top = 134; bottom = 0; integrate = true;
                }
                else if (idx == 5) // 23.8" Centrado (Barra integrada)
                {
                    left = 152; right = 152; top = 85; bottom = 85; integrate = true;
                }
                else if (idx == 6) // 23.8" Alineado Abajo (Barra integrada)
                {
                    left = 152; right = 152; top = 170; bottom = 0; integrate = true;
                }

                currentLeftMargin = left;
                currentRightMargin = right;
                currentTopMargin = top;
                currentBottomMargin = bottom;
                integrateTaskbarCheckBox.Checked = integrate;

                leftTrackBar.Value = Math.Min(leftTrackBar.Maximum, left);
                rightTrackBar.Value = Math.Min(rightTrackBar.Maximum, right);
                topTrackBar.Value = Math.Min(topTrackBar.Maximum, top);
                bottomTrackBar.Value = Math.Min(bottomTrackBar.Maximum, bottom);

                leftValLabel.Text = left + " px";
                rightValLabel.Text = right + " px";
                topValLabel.Text = top + " px";
                bottomValLabel.Text = bottom + " px";

                previewPanel.Invalidate();
            }
            finally
            {
                isUpdatingSliders = false;
            }
        }

        private void UpdatePresetComboBoxFromMargins()
        {
            isUpdatingSliders = true;
            try
            {
                bool integrate = integrateTaskbarCheckBox != null ? integrateTaskbarCheckBox.Checked : false;
                if (currentLeftMargin == 142 && currentRightMargin == 142 && currentTopMargin == 80 && currentBottomMargin == 80 && !integrate)
                {
                    presetComboBox.SelectedIndex = 1; // 24.0" Centrado (Sin barra)
                }
                else if (currentLeftMargin == 142 && currentRightMargin == 142 && currentTopMargin == 80 && currentBottomMargin == 80 && integrate)
                {
                    presetComboBox.SelectedIndex = 2; // 24.0" Centrado (Barra integrada)
                }
                else if (currentLeftMargin == 142 && currentRightMargin == 142 && currentTopMargin == 160 && currentBottomMargin == 0)
                {
                    presetComboBox.SelectedIndex = 3; // 24.0" Alineado Abajo
                }
                else if (currentLeftMargin == 118 && currentRightMargin == 118 && currentTopMargin == 67 && currentBottomMargin == 67 && !integrate)
                {
                    presetComboBox.SelectedIndex = 4; // 24.5" Centrado (Sin barra)
                }
                else if (currentLeftMargin == 118 && currentRightMargin == 118 && currentTopMargin == 67 && currentBottomMargin == 67 && integrate)
                {
                    presetComboBox.SelectedIndex = 5; // 24.5" Centrado (Barra integrada)
                }
                else if (currentLeftMargin == 118 && currentRightMargin == 118 && currentTopMargin == 134 && currentBottomMargin == 0)
                {
                    presetComboBox.SelectedIndex = 6; // 24.5" Alineado Abajo
                }
                else if (currentLeftMargin == 152 && currentRightMargin == 152 && currentTopMargin == 85 && currentBottomMargin == 85 && !integrate)
                {
                    presetComboBox.SelectedIndex = 7; // 23.8" Centrado (Sin barra)
                }
                else if (currentLeftMargin == 152 && currentRightMargin == 152 && currentTopMargin == 85 && currentBottomMargin == 85 && integrate)
                {
                    presetComboBox.SelectedIndex = 8; // 23.8" Centrado (Barra integrada)
                }
                else if (currentLeftMargin == 152 && currentRightMargin == 152 && currentTopMargin == 170 && currentBottomMargin == 0)
                {
                    presetComboBox.SelectedIndex = 9; // 23.8" Alineado Abajo
                }
                else
                {
                    presetComboBox.SelectedIndex = 0; // Manual
                }
            }
            finally
            {
                isUpdatingSliders = false;
            }
        }

        private void PopulateMonitors()
        {
            monitorComboBox.Items.Clear();
            Screen[] screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                string info = string.Format("Pantalla {0} ({1}x{2}) {3}", 
                    i + 1, 
                    screens[i].Bounds.Width, 
                    screens[i].Bounds.Height,
                    screens[i].Primary ? "[Principal]" : "");
                monitorComboBox.Items.Add(info);
            }

            if (screens.Length > 0)
            {
                monitorComboBox.SelectedIndex = 0;
            }
        }

        private void MonitorComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            int idx = monitorComboBox.SelectedIndex;
            if (idx >= 0 && idx < Screen.AllScreens.Length)
            {
                selectedScreen = Screen.AllScreens[idx];
                bool integrate = false;
                
                // Load current values from our configuration map
                if (configMap.ContainsKey(selectedScreen.DeviceName))
                {
                    var cfg = configMap[selectedScreen.DeviceName];
                    currentTopMargin = cfg.Top;
                    currentBottomMargin = cfg.Bottom;
                    currentLeftMargin = cfg.Left;
                    currentRightMargin = cfg.Right;
                    currentDimmerOpacity = cfg.DimmerOpacity;
                    integrate = cfg.IntegrateTaskbar;
                }
                else
                {
                    currentTopMargin = 0;
                    currentBottomMargin = 0;
                    currentLeftMargin = 0;
                    currentRightMargin = 0;
                    currentDimmerOpacity = 0;
                    integrate = false;
                }

                // Update controls
                topTrackBar.Value = Math.Min(topTrackBar.Maximum, currentTopMargin);
                bottomTrackBar.Value = Math.Min(bottomTrackBar.Maximum, currentBottomMargin);
                leftTrackBar.Value = Math.Min(leftTrackBar.Maximum, currentLeftMargin);
                rightTrackBar.Value = Math.Min(rightTrackBar.Maximum, currentRightMargin);
                dimmerTrackBar.Value = Math.Min(dimmerTrackBar.Maximum, currentDimmerOpacity);
                if (integrateTaskbarCheckBox != null)
                {
                    integrateTaskbarCheckBox.Checked = integrate;
                }

                topValLabel.Text = currentTopMargin + " px";
                bottomValLabel.Text = currentBottomMargin + " px";
                leftValLabel.Text = currentLeftMargin + " px";
                rightValLabel.Text = currentRightMargin + " px";
                dimmerValLabel.Text = currentDimmerOpacity + "%";

                UpdatePresetComboBoxFromMargins();
                previewPanel.Invalidate();
            }
        }

        private void PreviewPanel_Paint(object sender, PaintEventArgs e)
        {
            if (selectedScreen == null) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            int pw = previewPanel.Width;
            int ph = previewPanel.Height;

            // Maintain aspect ratio inside the canvas
            double screenAspect = (double)selectedScreen.Bounds.Width / selectedScreen.Bounds.Height;
            double panelAspect = (double)pw / ph;

            int mw, mh;
            if (screenAspect > panelAspect)
            {
                mw = pw - 40;
                mh = (int)(mw / screenAspect);
            }
            else
            {
                mh = ph - 40;
                mw = (int)(mh * screenAspect);
            }

            int mx = (pw - mw) / 2;
            int my = (ph - mh) / 2;

            // Draw monitor body background
            using (Brush bgBrush = new SolidBrush(Color.FromArgb(40, 40, 48)))
            {
                g.FillRectangle(bgBrush, mx, my, mw, mh);
            }

            double scaleX = (double)mw / selectedScreen.Bounds.Width;
            double scaleY = (double)mh / selectedScreen.Bounds.Height;

            int topPx = (int)(currentTopMargin * scaleY);
            int bottomPx = (int)(currentBottomMargin * scaleY);
            int leftPx = (int)(currentLeftMargin * scaleX);
            int rightPx = (int)(currentRightMargin * scaleX);

            // Shaded dead area (broken/inaccessible)
            using (Brush deadBrush = new SolidBrush(Color.FromArgb(140, 231, 76, 60))) // Semi-transparent Red
            {
                if (topPx > 0) g.FillRectangle(deadBrush, mx, my, mw, topPx);
                if (bottomPx > 0) g.FillRectangle(deadBrush, mx, my + mh - bottomPx, mw, bottomPx);
                if (leftPx > 0) g.FillRectangle(deadBrush, mx, my, leftPx, mh);
                if (rightPx > 0) g.FillRectangle(deadBrush, mx + mw - rightPx, my, rightPx, mh);
            }

            // Draw border of physical monitor
            using (Pen borderPen = new Pen(Color.FromArgb(90, 90, 100), 2))
            {
                g.DrawRectangle(borderPen, mx, my, mw, mh);
            }

            // Draw border of usable safe area
            int safeX = mx + leftPx;
            int safeY = my + topPx;
            int safeW = mw - leftPx - rightPx;
            int safeH = mh - topPx - bottomPx;

            if (safeW > 0 && safeH > 0)
            {
                using (Pen safePen = new Pen(ColorAccent, 2))
                {
                    safePen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                    g.DrawRectangle(safePen, safeX, safeY, safeW, safeH);
                }

                // Shaded inside safe area
                using (Brush safeBgBrush = new SolidBrush(Color.FromArgb(25, 0, 173, 181))) // Transparent Teal
                {
                    g.FillRectangle(safeBgBrush, safeX + 1, safeY + 1, safeW - 1, safeH - 1);
                }

                // Draw dimmer preview in live canvas
                if (currentDimmerOpacity > 0)
                {
                    int dimAlpha = (int)(currentDimmerOpacity * 255 / 100.0);
                    using (Brush dimBrush = new SolidBrush(Color.FromArgb(dimAlpha, 0, 0, 0)))
                    {
                        g.FillRectangle(dimBrush, safeX + 1, safeY + 1, safeW - 1, safeH - 1);
                    }
                }

                // Draw label in the center
                string activeText = string.Format("{0}x{1}", 
                    selectedScreen.Bounds.Width - currentLeftMargin - currentRightMargin,
                    selectedScreen.Bounds.Height - currentTopMargin - currentBottomMargin);
                
                using (Font font = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                using (Brush textBrush = new SolidBrush(ColorAccent))
                {
                    SizeF size = g.MeasureString(activeText, font);
                    float tx = safeX + (safeW - size.Width) / 2;
                    float ty = safeY + (safeH - size.Height) / 2;
                    g.DrawString(activeText, font, textBrush, tx, ty);
                }
            }
        }

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            if (selectedScreen == null) return;

            // Save to local map
            if (!configMap.ContainsKey(selectedScreen.DeviceName))
            {
                configMap[selectedScreen.DeviceName] = new MarginConfig();
            }

            var cfg = configMap[selectedScreen.DeviceName];
            cfg.DeviceName = selectedScreen.DeviceName;
            cfg.Top = currentTopMargin;
            cfg.Bottom = currentBottomMargin;
            cfg.Left = currentLeftMargin;
            cfg.Right = currentRightMargin;
            cfg.DimmerOpacity = currentDimmerOpacity;
            cfg.IntegrateTaskbar = integrateTaskbarCheckBox != null ? integrateTaskbarCheckBox.Checked : false;

            // Write to config file
            SaveConfig();

            // Setup boundary AppBars instantly
            UpdateAppBars();
            UpdateDimmers();

            MessageBox.Show("Márgenes y atenuación guardados con éxito.", "SafeDisplay", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ResetButton_Click(object sender, EventArgs e)
        {
            if (selectedScreen == null) return;

            currentTopMargin = 0;
            currentBottomMargin = 0;
            currentLeftMargin = 0;
            currentRightMargin = 0;
            currentDimmerOpacity = 0;

            topTrackBar.Value = 0;
            bottomTrackBar.Value = 0;
            leftTrackBar.Value = 0;
            rightTrackBar.Value = 0;
            dimmerTrackBar.Value = 0;
            if (integrateTaskbarCheckBox != null)
            {
                integrateTaskbarCheckBox.Checked = false;
            }

            topValLabel.Text = "0 px";
            bottomValLabel.Text = "0 px";
            leftValLabel.Text = "0 px";
            rightValLabel.Text = "0 px";
            dimmerValLabel.Text = "0%";

            UpdatePresetComboBoxFromMargins();
            previewPanel.Invalidate();

            if (configMap.ContainsKey(selectedScreen.DeviceName))
            {
                configMap.Remove(selectedScreen.DeviceName);
            }
            SaveConfig();

            // Restore screen boundaries
            UpdateAppBars();
            UpdateDimmers();

            MessageBox.Show("Configuración de esta pantalla restablecida.", "SafeDisplay", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ResetAllMargins()
        {
            configMap.Clear();
            SaveConfig();
            UpdateAppBars();
            UpdateDimmers();
        }

        private void CloseAllAppBars()
        {
            foreach (var bar in activeAppBars)
            {
                try
                {
                    bar.Close();
                    bar.Dispose();
                }
                catch {}
            }
            activeAppBars.Clear();
        }

        private void CloseAllDimmers()
        {
            foreach (var dim in activeDimmers.Values)
            {
                try
                {
                    dim.Close();
                    dim.Dispose();
                }
                catch {}
            }
            activeDimmers.Clear();
        }

        private void UpdateDimmers()
        {
            if (isExiting) return;

            List<string> activeDevices = new List<string>();
            Screen[] screens = Screen.AllScreens;

            foreach (Screen screen in screens)
            {
                int top = 0, bottom = 0, left = 0, right = 0;
                double dimOpacity = 0.0;

                if (configMap.ContainsKey(screen.DeviceName))
                {
                    var cfg = configMap[screen.DeviceName];
                    top = cfg.Top;
                    bottom = cfg.Bottom;
                    left = cfg.Left;
                    right = cfg.Right;
                    dimOpacity = cfg.DimmerOpacity / 100.0;
                }

                if (dimOpacity > 0.0)
                {
                    activeDevices.Add(screen.DeviceName);

                    int x = screen.Bounds.Left + left;
                    int y = screen.Bounds.Top + top;
                    int w = screen.Bounds.Width - left - right;
                    int h = screen.Bounds.Height - top - bottom;

                    if (w > 0 && h > 0)
                    {
                        if (activeDimmers.ContainsKey(screen.DeviceName))
                        {
                            var dim = activeDimmers[screen.DeviceName];
                            dim.UpdateBounds(new Rectangle(x, y, w, h));
                            dim.UpdateOpacity(dimOpacity);
                        }
                        else
                        {
                            var dim = new DimmerWindow(screen, dimOpacity);
                            dim.Bounds = new Rectangle(x, y, w, h);
                            dim.Show();
                            activeDimmers[screen.DeviceName] = dim;
                        }
                    }
                }
                else
                {
                    if (activeDimmers.ContainsKey(screen.DeviceName))
                    {
                        try
                        {
                            activeDimmers[screen.DeviceName].Close();
                            activeDimmers[screen.DeviceName].Dispose();
                        }
                        catch {}
                        activeDimmers.Remove(screen.DeviceName);
                    }
                }
            }

            List<string> toRemove = new List<string>();
            foreach (var key in activeDimmers.Keys)
            {
                if (!activeDevices.Contains(key))
                {
                    toRemove.Add(key);
                }
            }
            foreach (var key in toRemove)
            {
                try
                {
                    activeDimmers[key].Close();
                    activeDimmers[key].Dispose();
                }
                catch {}
                activeDimmers.Remove(key);
            }
        }

        private void UpdateAppBars()
        {
            if (isExiting) return;

            // Safely close existing AppBars
            CloseAllAppBars();

            Screen[] screens = Screen.AllScreens;
            foreach (Screen screen in screens)
            {
                if (configMap.ContainsKey(screen.DeviceName))
                {
                    var cfg = configMap[screen.DeviceName];

                    // Spawn AppBars for non-zero margins
                    if (cfg.Top > 0)
                    {
                        var bar = new AppBarWindow(screen, AppBarWindow.ABE_TOP, cfg.Top, false);
                        bar.Show();
                        activeAppBars.Add(bar);
                    }
                    if (cfg.Bottom > 0)
                    {
                        var bar = new AppBarWindow(screen, AppBarWindow.ABE_BOTTOM, cfg.Bottom, cfg.IntegrateTaskbar);
                        bar.Show();
                        activeAppBars.Add(bar);
                    }
                    if (cfg.Left > 0)
                    {
                        var bar = new AppBarWindow(screen, AppBarWindow.ABE_LEFT, cfg.Left, false);
                        bar.Show();
                        activeAppBars.Add(bar);
                    }
                    if (cfg.Right > 0)
                    {
                        var bar = new AppBarWindow(screen, AppBarWindow.ABE_RIGHT, cfg.Right, false);
                        bar.Show();
                        activeAppBars.Add(bar);
                    }
                }
            }
        }

        private void MouseTimer_Tick(object sender, EventArgs e)
        {
            if (isExiting || blockCursorCheckBox == null || !blockCursorCheckBox.Checked) return;

            Point pt = Cursor.Position;
            Screen[] screens = Screen.AllScreens;
            
            foreach (Screen screen in screens)
            {
                if (configMap.ContainsKey(screen.DeviceName))
                {
                    var cfg = configMap[screen.DeviceName];
                    
                    // Check if mouse is within this screen's bounds
                    if (screen.Bounds.Contains(pt))
                    {
                        bool adjusted = false;
                        int newX = pt.X;
                        int newY = pt.Y;

                        // Enforce Top Margin (Invisible Wall)
                        if (cfg.Top > 0 && pt.Y < screen.Bounds.Top + cfg.Top)
                        {
                            newY = screen.Bounds.Top + cfg.Top;
                            adjusted = true;
                        }

                        // Enforce Bottom Margin (Allow cursor to pass to taskbar if bottom margin <= 45px)
                        if (cfg.Bottom > 45 && pt.Y > screen.Bounds.Bottom - cfg.Bottom)
                        {
                            newY = screen.Bounds.Bottom - cfg.Bottom;
                            adjusted = true;
                        }

                        // Enforce Left Margin
                        if (cfg.Left > 0 && pt.X < screen.Bounds.Left + cfg.Left)
                        {
                            newX = screen.Bounds.Left + cfg.Left;
                            adjusted = true;
                        }

                        // Enforce Right Margin
                        if (cfg.Right > 0 && pt.X > screen.Bounds.Right - cfg.Right)
                        {
                            newX = screen.Bounds.Right - cfg.Right;
                            adjusted = true;
                        }

                        if (adjusted)
                        {
                            Cursor.Position = new Point(newX, newY);
                        }
                        break; // Cursor can only be inside one screen
                    }
                }
            }
        }

        private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
        {
            // Screens configuration changed (resolution, scale, connection)
            PopulateMonitors();
            UpdateAppBars();
            UpdateDimmers();
        }

        private void SaveConfig()
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(configPath))
                {
                    // Save global settings first
                    writer.WriteLine("GLOBAL|BlockCursor|" + (blockCursorCheckBox != null ? blockCursorCheckBox.Checked.ToString() : "True"));
                    
                    foreach (var cfg in configMap.Values)
                    {
                        writer.WriteLine(string.Format("{0}|{1}|{2}|{3}|{4}|{5}|{6}", 
                            cfg.DeviceName, cfg.Top, cfg.Bottom, cfg.Left, cfg.Right, cfg.DimmerOpacity, cfg.IntegrateTaskbar));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error saving config: " + ex.Message);
            }
        }

        private void LoadConfig()
        {
            configMap.Clear();
            if (File.Exists(configPath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(configPath);
                    foreach (string line in lines)
                    {
                        if (string.IsNullOrEmpty(line)) continue;
                        string[] parts = line.Split('|');
                        
                        if (parts[0] == "GLOBAL" && parts.Length == 3)
                        {
                            if (parts[1] == "BlockCursor" && blockCursorCheckBox != null)
                            {
                                blockCursorCheckBox.Checked = bool.Parse(parts[2]);
                            }
                        }
                        else if (parts.Length >= 5)
                        {
                            var cfg = new MarginConfig();
                            cfg.DeviceName = parts[0];
                            cfg.Top = int.Parse(parts[1]);
                            cfg.Bottom = int.Parse(parts[2]);
                            cfg.Left = int.Parse(parts[3]);
                            cfg.Right = int.Parse(parts[4]);
                            cfg.DimmerOpacity = parts.Length >= 6 ? int.Parse(parts[5]) : 0;
                            cfg.IntegrateTaskbar = parts.Length >= 7 ? bool.Parse(parts[6]) : false;

                            configMap[cfg.DeviceName] = cfg;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error loading config: " + ex.Message);
                }
            }

            // Load Windows Startup state
            try
            {
                string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath, false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("SafeDisplay");
                        startupCheckBox.Checked = (val != null);
                    }
                }
            }
            catch {}
        }

        private void StartupCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath, true))
                {
                    if (startupCheckBox.Checked)
                    {
                        string runCmd = string.Format("\"{0}\" --startup", Application.ExecutablePath);
                        key.SetValue("SafeDisplay", runCmd);
                    }
                    else
                    {
                        key.DeleteValue("SafeDisplay", false);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al configurar el inicio con Windows: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    /// <summary>
    /// An elegant, completely transparent click-through black layered window
    /// that covers the safe area of a monitor to simulate software-level display dimming.
    /// </summary>
    public class DimmerWindow : Form
    {
        private Screen targetScreen;
        private double opacityPercent;

        public DimmerWindow(Screen screen, double opacityPercent)
        {
            this.targetScreen = screen;
            this.opacityPercent = opacityPercent;

            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = Color.Black;
            this.TopMost = true;

            this.Opacity = opacityPercent;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // WS_EX_TOOLWINDOW (0x80): No Alt-Tab list
                // WS_EX_NOACTIVATE (0x08000000): Do not activate when clicked
                // WS_EX_LAYERED (0x80000): Required for opacity
                // WS_EX_TRANSPARENT (0x20): Enables click-through physics
                cp.ExStyle |= 0x00000080 | 0x08000000 | 0x00080000 | 0x00000020;
                return cp;
            }
        }

        public void UpdateOpacity(double newOpacity)
        {
            if (this.Opacity != newOpacity)
            {
                this.Opacity = newOpacity;
            }
        }

        public void UpdateBounds(Rectangle bounds)
        {
            if (this.Bounds != bounds)
            {
                this.Bounds = bounds;
            }
        }
    }

    /// <summary>
    /// An elegant invisible solid-black border reservation form that docks via Win32 AppBar APIs
    /// to reserve desktop space and physically cover broken regions on any monitor.
    /// </summary>
    public class AppBarWindow : Form
    {
        // P/Invoke constants and signatures for AppBar
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public RECT(int left, int top, int right, int bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

        public const uint ABM_NEW = 0;
        public const uint ABM_REMOVE = 1;
        public const uint ABM_QUERYPOS = 2;
        public const uint ABM_SETPOS = 3;

        public const uint ABE_LEFT = 0;
        public const uint ABE_TOP = 1;
        public const uint ABE_RIGHT = 2;
        public const uint ABE_BOTTOM = 3;

        private bool isRegistered = false;
        private Screen targetScreen;
        private uint edge;
        private int thickness;
        private bool integrateTaskbar;

        public AppBarWindow(Screen screen, uint edge, int thickness, bool integrateTaskbar)
        {
            this.targetScreen = screen;
            this.edge = edge;
            this.thickness = thickness;
            this.integrateTaskbar = integrateTaskbar;

            // Borderless flat window properties
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = Color.Black;
            this.TopMost = true;

            // Initial positioning inside the selected screen coordinates
            int x = screen.Bounds.Left;
            int y = screen.Bounds.Top;
            int w = screen.Bounds.Width;
            int h = screen.Bounds.Height;

            if (edge == ABE_TOP) { h = thickness; }
            else if (edge == ABE_BOTTOM) { y = screen.Bounds.Bottom - thickness; h = thickness; }
            else if (edge == ABE_LEFT) { w = thickness; }
            else if (edge == ABE_RIGHT) { x = screen.Bounds.Right - thickness; w = thickness; }

            this.Bounds = new Rectangle(x, y, w, h);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW (No Alt-Tab list)
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE (Do not activate when clicked)
                return cp;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RegisterAppBar();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            UnregisterAppBar();
            base.OnFormClosing(e);
        }

        private void RegisterAppBar()
        {
            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            abd.hWnd = this.Handle;
            abd.uCallbackMessage = 0x8000 + 202; // Custom Callback message

            SHAppBarMessage(ABM_NEW, ref abd);
            isRegistered = true;

            abd.uEdge = this.edge;
            
            int x = targetScreen.Bounds.Left;
            int y = targetScreen.Bounds.Top;
            int r = targetScreen.Bounds.Right;
            int b = targetScreen.Bounds.Bottom;

            if (edge == ABE_TOP) { b = y + thickness; }
            else if (edge == ABE_BOTTOM) { y = b - thickness; }
            else if (edge == ABE_LEFT) { r = x + thickness; }
            else if (edge == ABE_RIGHT) { x = r - thickness; }

            abd.rc = new RECT(x, y, r, b);

            if (edge == ABE_BOTTOM && integrateTaskbar)
            {
                // Force our AppBar to the absolute bottom edge of the physical screen (Y = 1360 to 1440).
                // Do NOT let ABM_QUERYPOS shift us away from the edge. This forces the system taskbar to 
                // stack above us (at Y = 1320 to 1360), keeping it visible and inside the 24" workspace.
                abd.rc.Top = targetScreen.Bounds.Bottom - thickness;
                abd.rc.Bottom = targetScreen.Bounds.Bottom;
                
                SHAppBarMessage(ABM_SETPOS, ref abd);
            }
            else
            {
                // Query the shell for positioning
                SHAppBarMessage(ABM_QUERYPOS, ref abd);
                
                // Force the size to be our desired thickness
                if (edge == ABE_TOP) { abd.rc.Bottom = abd.rc.Top + thickness; }
                else if (edge == ABE_BOTTOM) { abd.rc.Top = abd.rc.Bottom - thickness; }
                else if (edge == ABE_LEFT) { abd.rc.Right = abd.rc.Left + thickness; }
                else if (edge == ABE_RIGHT) { abd.rc.Left = abd.rc.Right - thickness; }

                // Set size and position in the shell, reserving the workspace area
                SHAppBarMessage(ABM_SETPOS, ref abd);
            }
            
            // Dock our solid black form but make it stretch corner-to-corner and behind the taskbar
            int ax = abd.rc.Left;
            int ay = abd.rc.Top;
            int aw = abd.rc.Right - abd.rc.Left;
            int ah = abd.rc.Bottom - abd.rc.Top;

            if (edge == ABE_BOTTOM)
            {
                if (integrateTaskbar)
                {
                    ay = targetScreen.Bounds.Bottom - thickness;
                    ah = thickness;
                }
                else if (thickness <= 45)
                {
                    ah = thickness;
                }
                else
                {
                    ah = targetScreen.Bounds.Bottom - ay;
                }
            }
            else if (edge == ABE_LEFT || edge == ABE_RIGHT)
            {
                ay = targetScreen.Bounds.Top;
                ah = targetScreen.Bounds.Height;
            }
            else if (edge == ABE_TOP)
            {
                ax = targetScreen.Bounds.Left;
                aw = targetScreen.Bounds.Width;
            }

            this.Bounds = new Rectangle(ax, ay, aw, ah);
        }

        private void UnregisterAppBar()
        {
            if (isRegistered)
            {
                APPBARDATA abd = new APPBARDATA();
                abd.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
                abd.hWnd = this.Handle;
                SHAppBarMessage(ABM_REMOVE, ref abd);
                isRegistered = false;
            }
        }
    }
}
