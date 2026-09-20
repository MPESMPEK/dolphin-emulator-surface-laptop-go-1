using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace TouchJoystick
{
    public enum ControllerLayout
    {
        GameCube,
        WiiRemote,
        PlayStation
    }

    public partial class MainWindow : Window
    {
        // ── Win32 Imports ──
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus; // 0 = Battery, 1 = AC, 255 = Unknown
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int SM_DIGITIZER = 94;
        private const int NID_MULTI_INPUT = 0x40;
        private const int NID_READY = 0x80;

        // ── Input Structures ──
        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        // Virtual Key Codes
        private const ushort VK_UP = 0x26;
        private const ushort VK_DOWN = 0x28;
        private const ushort VK_LEFT = 0x25;
        private const ushort VK_RIGHT = 0x27;
        private const ushort VK_RETURN = 0x0D; // Enter / Start
        private const ushort VK_SPACE = 0x20;  // Space / Select
        private const ushort VK_TAB = 0x09;    // Tab / Turbo
        private const ushort VK_F1 = 0x70;     // Save state
        private const ushort VK_F8 = 0x77;     // Load state

        // GameCube / Dolphin keys
        private const ushort VK_X = 0x58;      // A
        private const ushort VK_Z = 0x5A;      // B
        private const ushort VK_S = 0x53;      // X
        private const ushort VK_A = 0x41;      // Y
        private const ushort VK_Q = 0x51;      // L
        private const ushort VK_W = 0x57;      // R
        private const ushort VK_I = 0x49;      // C-Stick Up
        private const ushort VK_K = 0x4B;      // C-Stick Down
        private const ushort VK_J = 0x4A;      // C-Stick Left
        private const ushort VK_L = 0x4C;      // C-Stick Right

        // Wii keys
        private const ushort VK_1 = 0x31;      // 1
        private const ushort VK_2 = 0x32;      // 2

        // ── Active State ──
        private Process? _activeProcess;
        private ControllerLayout _currentLayout = ControllerLayout.GameCube;
        private double _globalOpacity = 0.6;
        private double _globalScale = 1.0;
        private bool _controlsVisible = true;
        private DispatcherTimer? _batteryTimer;

        // ── Touch Handling ──
        private int _stickTouchId = -1;
        private Point _stickCenter;
        private double _stickRadius = 75;
        private double _knobRadius = 32;
        private readonly double _deadZone = 0.15;
        private Ellipse? _stickKnob;

        private int _cStickTouchId = -1;
        private Point _cStickCenter;
        private double _cStickRadius = 55;
        private double _cKnobRadius = 24;
        private Ellipse? _cStickKnob;

        private bool _keyUp, _keyDown, _keyLeft, _keyRight;
        private bool _cKeyUp, _cKeyDown, _cKeyLeft, _cKeyRight;

        private class TouchButtonInfo
        {
            public string Name = "";
            public ushort Vk;
            public Ellipse Visual = null!;
            public int TouchId = -1;
            public bool IsPressed = false;
        }

        private readonly List<TouchButtonInfo> _activeButtons = new();

        public MainWindow()
        {
            InitializeComponent();

            Loaded += OnLoaded;

            KeyDown += (s, e) =>
            {
                if (e.Key == Key.J && Keyboard.Modifiers == ModifierKeys.Control)
                    ToggleControls();
                else if (e.Key == Key.Escape)
                {
                    if (OverlayCanvas.Visibility == Visibility.Visible)
                        SwitchToHub();
                    else
                        Close();
                }
            };
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, new IntPtr(exStyle.ToInt64() | WS_EX_TOOLWINDOW));

            // Setup Touch Events on Overlay
            OverlayCanvas.TouchDown += Overlay_TouchDown;
            OverlayCanvas.TouchMove += Overlay_TouchMove;
            OverlayCanvas.TouchUp += Overlay_TouchUp;

            // Setup Battery Monitor
            InitBatteryMonitor();

            // Center GameBar
            double screenW = SystemParameters.PrimaryScreenWidth;
            Canvas.SetLeft(GameBar, (screenW - 550) / 2);
            Canvas.SetLeft(SettingsDrawer, (screenW - 320) / 2);

            // Default view is Hub
            SwitchToHub();
        }

        // ══════════════════════════════════════════════════════
        // NAVIGATION & VIEWS
        // ══════════════════════════════════════════════════════

        private void SwitchToHub()
        {
            HubContainer.Visibility = Visibility.Visible;
            OverlayCanvas.Visibility = Visibility.Collapsed;
            Topmost = false;
        }

        private void SwitchToOverlay()
        {
            HubContainer.Visibility = Visibility.Collapsed;
            OverlayCanvas.Visibility = Visibility.Visible;
            Topmost = true;
            RenderCurrentLayout();
        }

        private void BtnCloseHub_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnStartOverlayOnly_Click(object sender, RoutedEventArgs e)
        {
            SwitchToOverlay();
        }

        private void BtnBackToHub_Click(object sender, RoutedEventArgs e)
        {
            SwitchToHub();
        }

        // ══════════════════════════════════════════════════════
        // EMULATOR LAUNCHERS
        // ══════════════════════════════════════════════════════

        private void LaunchDolphin_Click(object sender, MouseButtonEventArgs e)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dolphinExe = System.IO.Path.Combine(baseDir, "Dolphin.exe");

            if (!File.Exists(dolphinExe))
            {
                string parentDir = Directory.GetParent(baseDir)?.FullName ?? baseDir;
                string alt = System.IO.Path.Combine(parentDir, "Dolphin-x64", "Dolphin.exe");
                if (File.Exists(alt)) dolphinExe = alt;
            }

            if (File.Exists(dolphinExe))
            {
                StartEmulatorProcess(dolphinExe, ControllerLayout.GameCube);
            }
            else
            {
                MessageBox.Show("Dolphin.exe siap pakai ditemukan di folder Dolphin-x64.", "Dolphin", MessageBoxButton.OK, MessageBoxImage.Information);
                SwitchToOverlay();
            }
        }

        private void LaunchPcsx2_Click(object sender, MouseButtonEventArgs e)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string parentDir = Directory.GetParent(baseDir)?.FullName ?? baseDir;
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            var candidates = new[]
            {
                System.IO.Path.Combine(baseDir, "pcsx2-qt.exe"),
                System.IO.Path.Combine(baseDir, "PCSX2-x64", "pcsx2-qt.exe"),
                System.IO.Path.Combine(parentDir, "PCSX2-x64", "pcsx2-qt.exe"),
                System.IO.Path.Combine(docs, "Dolphin-Surface-Go", "PCSX2-x64", "pcsx2-qt.exe"),
                System.IO.Path.Combine(docs, "PCSX2-Surface-Go", "pcsx2-qt.exe"),
                System.IO.Path.Combine(docs, "pcsx2-surface", "bin", "pcsx2-qt.exe")
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    StartEmulatorProcess(path, ControllerLayout.PlayStation);
                    return;
                }
            }

            TryLaunchEmulator("PCSX2", "pcsx2-qt.exe", ControllerLayout.PlayStation);
        }

        private void LaunchDuckStation_Click(object sender, MouseButtonEventArgs e)
        {
            TryLaunchEmulator("DuckStation", "duckstation-qt-x64-ReleaseLTCG.exe", ControllerLayout.PlayStation);
        }

        private void LaunchPpsspp_Click(object sender, MouseButtonEventArgs e)
        {
            TryLaunchEmulator("PPSSPP", "PPSSPPWindows64.exe", ControllerLayout.PlayStation);
        }

        private void LaunchRetroArch_Click(object sender, MouseButtonEventArgs e)
        {
            TryLaunchEmulator("RetroArch", "retroarch.exe", ControllerLayout.WiiRemote);
        }

        private void TryLaunchEmulator(string name, string exeName, ControllerLayout defaultLayout)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localPath = System.IO.Path.Combine(baseDir, exeName);

            if (File.Exists(localPath))
            {
                StartEmulatorProcess(localPath, defaultLayout);
                return;
            }

            // Search typical locations
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var candidates = new[]
            {
                System.IO.Path.Combine(docs, name, exeName),
                System.IO.Path.Combine(@"C:\Program Files", name, exeName),
                System.IO.Path.Combine(@"C:\Program Files (x86)", name, exeName)
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    StartEmulatorProcess(path, defaultLayout);
                    return;
                }
            }

            var res = MessageBox.Show(
                $"{name} belum terdeteksi di folder emulator.\n\nApakah Anda ingin tetap membuka overlay joystick layar sentuh dengan layout {defaultLayout}?",
                $"{name} Hub", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                _currentLayout = defaultLayout;
                SwitchToOverlay();
            }
        }

        private void StartEmulatorProcess(string exePath, ControllerLayout layout)
        {
            try
            {
                _currentLayout = layout;
                var info = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(exePath)!
                };
                _activeProcess = Process.Start(info);
                if (_activeProcess != null)
                {
                    _activeProcess.EnableRaisingEvents = true;
                    _activeProcess.Exited += (s, ev) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _activeProcess = null;
                            SwitchToHub();
                        });
                    };
                }
                SwitchToOverlay();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal menjalankan emulator: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ══════════════════════════════════════════════════════
        // IN-GAME QUICK TOUCH GAMEBAR
        // ══════════════════════════════════════════════════════

        private void BtnToggleControls_Click(object sender, RoutedEventArgs e)
        {
            ToggleControls();
        }

        private void ToggleControls()
        {
            _controlsVisible = !_controlsVisible;
            ControllerCanvas.Opacity = _controlsVisible ? 1.0 : 0.0;
            BtnToggleControls.Content = _controlsVisible ? "🎮 Kontrol" : "👁️ Tampilkan";
        }

        private void BtnSaveState_Click(object sender, RoutedEventArgs e)
        {
            SendSingleKey(VK_F1);
            ShowNotification("💾 Save State Disimpan (F1)");
        }

        private void BtnLoadState_Click(object sender, RoutedEventArgs e)
        {
            SendSingleKey(VK_F8);
            ShowNotification("📂 Load State Dimuat (F8)");
        }

        private void BtnTurbo_Click(object sender, RoutedEventArgs e)
        {
            SendSingleKey(VK_TAB);
            ShowNotification("⏩ Kecepatan Turbo Dialihkan (Tab)");
        }

        private void BtnSettingsDrawer_Click(object sender, RoutedEventArgs e)
        {
            SettingsDrawer.Visibility = SettingsDrawer.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void ShowNotification(string msg)
        {
            // Update button or indicator
            BtnToggleControls.Content = msg;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (s, ev) =>
            {
                BtnToggleControls.Content = _controlsVisible ? "🎮 Kontrol" : "👁️ Tampilkan";
                timer.Stop();
            };
            timer.Start();
        }

        // ══════════════════════════════════════════════════════
        // DYNAMIC TOUCH CONTROLLER LAYOUTS
        // ══════════════════════════════════════════════════════

        private void SetLayoutGC_Click(object sender, RoutedEventArgs e)
        {
            _currentLayout = ControllerLayout.GameCube;
            UpdateLayoutButtons();
            RenderCurrentLayout();
        }

        private void SetLayoutWii_Click(object sender, RoutedEventArgs e)
        {
            _currentLayout = ControllerLayout.WiiRemote;
            UpdateLayoutButtons();
            RenderCurrentLayout();
        }

        private void SetLayoutPS_Click(object sender, RoutedEventArgs e)
        {
            _currentLayout = ControllerLayout.PlayStation;
            UpdateLayoutButtons();
            RenderCurrentLayout();
        }

        private void UpdateLayoutButtons()
        {
            var activeBg = new SolidColorBrush(Color.FromRgb(0, 229, 255));
            var idleBg = new SolidColorBrush(Color.FromRgb(38, 43, 59));
            var activeFg = new SolidColorBrush(Color.FromRgb(15, 17, 23));
            var idleFg = Brushes.White;

            BtnLayoutGC.Background = _currentLayout == ControllerLayout.GameCube ? activeBg : idleBg;
            BtnLayoutGC.Foreground = _currentLayout == ControllerLayout.GameCube ? activeFg : idleFg;

            BtnLayoutWii.Background = _currentLayout == ControllerLayout.WiiRemote ? activeBg : idleBg;
            BtnLayoutWii.Foreground = _currentLayout == ControllerLayout.WiiRemote ? activeFg : idleFg;

            BtnLayoutPS.Background = _currentLayout == ControllerLayout.PlayStation ? activeBg : idleBg;
            BtnLayoutPS.Foreground = _currentLayout == ControllerLayout.PlayStation ? activeFg : idleFg;
        }

        private void SliderOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtOpacity == null) return;
            _globalOpacity = e.NewValue;
            TxtOpacity.Text = $"Transparansi Tombol: {(int)(_globalOpacity * 100)}%";
            RenderCurrentLayout();
        }

        private void SliderScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtScale == null) return;
            _globalScale = e.NewValue;
            TxtScale.Text = $"Ukuran Tombol: {(int)(_globalScale * 100)}%";
            RenderCurrentLayout();
        }

        private void RenderCurrentLayout()
        {
            ControllerCanvas.Children.Clear();
            _activeButtons.Clear();

            double w = SystemParameters.PrimaryScreenWidth;
            double h = SystemParameters.PrimaryScreenHeight;

            byte alpha = (byte)(_globalOpacity * 255);
            byte pressedAlpha = (byte)Math.Min(255, alpha + 80);

            var baseBrush = new SolidColorBrush(Color.FromArgb((byte)(alpha * 0.5), 255, 255, 255));
            var knobBrush = new SolidColorBrush(Color.FromArgb(alpha, 0, 200, 255));
            var borderBrush = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255));

            if (_currentLayout == ControllerLayout.GameCube)
            {
                RenderGameCubeLayout(w, h, baseBrush, knobBrush, borderBrush, alpha);
            }
            else if (_currentLayout == ControllerLayout.WiiRemote)
            {
                RenderWiiRemoteLayout(w, h, baseBrush, knobBrush, borderBrush, alpha);
            }
            else if (_currentLayout == ControllerLayout.PlayStation)
            {
                RenderPlayStationLayout(w, h, baseBrush, knobBrush, borderBrush, alpha);
            }
        }

        private void RenderGameCubeLayout(double w, double h, Brush baseBrush, Brush knobBrush, Brush borderBrush, byte alpha)
        {
            // Left: Analog Stick
            _stickRadius = 75 * _globalScale;
            _knobRadius = 32 * _globalScale;
            double stickX = 140 * _globalScale;
            double stickY = h - (170 * _globalScale);
            _stickCenter = new Point(stickX, stickY);

            var stickBase = new Ellipse
            {
                Width = _stickRadius * 2,
                Height = _stickRadius * 2,
                Fill = baseBrush,
                Stroke = borderBrush,
                StrokeThickness = 2
            };
            Canvas.SetLeft(stickBase, stickX - _stickRadius);
            Canvas.SetTop(stickBase, stickY - _stickRadius);
            ControllerCanvas.Children.Add(stickBase);

            _stickKnob = new Ellipse
            {
                Width = _knobRadius * 2,
                Height = _knobRadius * 2,
                Fill = knobBrush,
                Stroke = borderBrush,
                StrokeThickness = 1.5,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(_stickKnob, stickX - _knobRadius);
            Canvas.SetTop(_stickKnob, stickY - _knobRadius);
            ControllerCanvas.Children.Add(_stickKnob);

            // Right: Diamond A/B/X/Y
            double btnCenterX = w - (140 * _globalScale);
            double btnCenterY = h - (170 * _globalScale);
            double spacing = 65 * _globalScale;
            double btnSize = 58 * _globalScale;

            AddButton("A", btnCenterX, btnCenterY + spacing, btnSize, VK_X, "A", alpha);
            AddButton("B", btnCenterX - spacing, btnCenterY, btnSize, VK_Z, "B", alpha);
            AddButton("X", btnCenterX + spacing, btnCenterY, btnSize, VK_S, "X", alpha);
            AddButton("Y", btnCenterX, btnCenterY - spacing, btnSize, VK_A, "Y", alpha);

            // Mini C-Stick
            _cStickRadius = 50 * _globalScale;
            _cKnobRadius = 22 * _globalScale;
            double cStickX = btnCenterX - (spacing * 1.5);
            double cStickY = btnCenterY + (spacing * 1.2);
            _cStickCenter = new Point(cStickX, cStickY);

            var cStickBase = new Ellipse
            {
                Width = _cStickRadius * 2,
                Height = _cStickRadius * 2,
                Fill = baseBrush,
                Stroke = borderBrush,
                StrokeThickness = 1.5
            };
            Canvas.SetLeft(cStickBase, cStickX - _cStickRadius);
            Canvas.SetTop(cStickBase, cStickY - _cStickRadius);
            ControllerCanvas.Children.Add(cStickBase);

            _cStickKnob = new Ellipse
            {
                Width = _cKnobRadius * 2,
                Height = _cKnobRadius * 2,
                Fill = new SolidColorBrush(Color.FromArgb(alpha, 255, 215, 0)),
                Stroke = borderBrush,
                StrokeThickness = 1.5,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(_cStickKnob, cStickX - _cKnobRadius);
            Canvas.SetTop(_cStickKnob, cStickY - _cKnobRadius);
            ControllerCanvas.Children.Add(_cStickKnob);

            // Start & Triggers
            AddButton("Start", w / 2, h - (50 * _globalScale), 48 * _globalScale, VK_RETURN, "▶", alpha);
            AddTriggerButton("L", 100 * _globalScale, 70, 75 * _globalScale, 42 * _globalScale, VK_Q, "L", alpha);
            AddTriggerButton("R", w - (100 * _globalScale), 70, 75 * _globalScale, 42 * _globalScale, VK_W, "R", alpha);
        }

        private void RenderWiiRemoteLayout(double w, double h, Brush baseBrush, Brush knobBrush, Brush borderBrush, byte alpha)
        {
            // Left: D-Pad cross
            double dpadCenterX = 150 * _globalScale;
            double dpadCenterY = h - (170 * _globalScale);
            double padSize = 54 * _globalScale;
            double padDist = 58 * _globalScale;

            AddButton("Up", dpadCenterX, dpadCenterY - padDist, padSize, VK_UP, "▲", alpha);
            AddButton("Down", dpadCenterX, dpadCenterY + padDist, padSize, VK_DOWN, "▼", alpha);
            AddButton("Left", dpadCenterX - padDist, dpadCenterY, padSize, VK_LEFT, "◀", alpha);
            AddButton("Right", dpadCenterX + padDist, dpadCenterY, padSize, VK_RIGHT, "▶", alpha);

            // Right: 1 & 2 Buttons + A & B
            double btnCenterX = w - (140 * _globalScale);
            double btnCenterY = h - (170 * _globalScale);
            double spacing = 65 * _globalScale;
            double btnSize = 58 * _globalScale;

            AddButton("2", btnCenterX + spacing, btnCenterY, btnSize, VK_2, "2", alpha);
            AddButton("1", btnCenterX - spacing, btnCenterY, btnSize, VK_1, "1", alpha);
            AddButton("A", btnCenterX, btnCenterY - spacing, btnSize, VK_X, "A", alpha);
            AddButton("B", btnCenterX, btnCenterY + spacing, btnSize, VK_Z, "B", alpha);

            // Plus (+) & Minus (-)
            AddButton("-", (w / 2) - (50 * _globalScale), h - (50 * _globalScale), 44 * _globalScale, VK_SPACE, "−", alpha);
            AddButton("+", (w / 2) + (50 * _globalScale), h - (50 * _globalScale), 44 * _globalScale, VK_RETURN, "+", alpha);
        }

        private void RenderPlayStationLayout(double w, double h, Brush baseBrush, Brush knobBrush, Brush borderBrush, byte alpha)
        {
            // Left: DualShock Analog Stick
            _stickRadius = 75 * _globalScale;
            _knobRadius = 32 * _globalScale;
            double stickX = 140 * _globalScale;
            double stickY = h - (170 * _globalScale);
            _stickCenter = new Point(stickX, stickY);

            var stickBase = new Ellipse
            {
                Width = _stickRadius * 2,
                Height = _stickRadius * 2,
                Fill = baseBrush,
                Stroke = borderBrush,
                StrokeThickness = 2
            };
            Canvas.SetLeft(stickBase, stickX - _stickRadius);
            Canvas.SetTop(stickBase, stickY - _stickRadius);
            ControllerCanvas.Children.Add(stickBase);

            _stickKnob = new Ellipse
            {
                Width = _knobRadius * 2,
                Height = _knobRadius * 2,
                Fill = knobBrush,
                Stroke = borderBrush,
                StrokeThickness = 1.5,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(_stickKnob, stickX - _knobRadius);
            Canvas.SetTop(_stickKnob, stickY - _knobRadius);
            ControllerCanvas.Children.Add(_stickKnob);

            // Right: Triangle (△), Square (□), Cross (✕), Circle (○)
            double btnCenterX = w - (140 * _globalScale);
            double btnCenterY = h - (170 * _globalScale);
            double spacing = 65 * _globalScale;
            double btnSize = 58 * _globalScale;

            AddButton("Cross", btnCenterX, btnCenterY + spacing, btnSize, VK_X, "✕", alpha);
            AddButton("Square", btnCenterX - spacing, btnCenterY, btnSize, VK_Z, "□", alpha);
            AddButton("Circle", btnCenterX + spacing, btnCenterY, btnSize, VK_S, "○", alpha);
            AddButton("Triangle", btnCenterX, btnCenterY - spacing, btnSize, VK_A, "△", alpha);

            // Select & Start
            AddButton("Select", (w / 2) - (50 * _globalScale), h - (50 * _globalScale), 44 * _globalScale, VK_SPACE, "SEL", alpha);
            AddButton("Start", (w / 2) + (50 * _globalScale), h - (50 * _globalScale), 44 * _globalScale, VK_RETURN, "START", alpha);

            // L1 & R1
            AddTriggerButton("L1", 100 * _globalScale, 70, 75 * _globalScale, 42 * _globalScale, VK_Q, "L1", alpha);
            AddTriggerButton("R1", w - (100 * _globalScale), 70, 75 * _globalScale, 42 * _globalScale, VK_W, "R1", alpha);
        }

        private void AddButton(string name, double cx, double cy, double size, ushort vk, string label, byte alpha)
        {
            var btn = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(Color.FromArgb((byte)(alpha * 0.5), 255, 255, 255)),
                Stroke = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)),
                StrokeThickness = 1.5,
                IsHitTestVisible = true
            };
            Canvas.SetLeft(btn, cx - size / 2);
            Canvas.SetTop(btn, cy - size / 2);
            ControllerCanvas.Children.Add(btn);

            var lbl = new Border
            {
                Width = size,
                Height = size,
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = label,
                    Foreground = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)),
                    FontSize = Math.Max(12, 18 * _globalScale),
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            };
            Canvas.SetLeft(lbl, cx - size / 2);
            Canvas.SetTop(lbl, cy - size / 2);
            ControllerCanvas.Children.Add(lbl);

            _activeButtons.Add(new TouchButtonInfo
            {
                Name = name,
                Vk = vk,
                Visual = btn
            });
        }

        private void AddTriggerButton(string name, double cx, double cy, double w, double h, ushort vk, string label, byte alpha)
        {
            var btn = new Ellipse
            {
                Width = w,
                Height = h,
                Fill = new SolidColorBrush(Color.FromArgb((byte)(alpha * 0.5), 255, 255, 255)),
                Stroke = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)),
                StrokeThickness = 1.5,
                IsHitTestVisible = true
            };
            Canvas.SetLeft(btn, cx - w / 2);
            Canvas.SetTop(btn, cy - h / 2);
            ControllerCanvas.Children.Add(btn);

            var lbl = new Border
            {
                Width = w,
                Height = h,
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = label,
                    Foreground = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)),
                    FontSize = 14 * _globalScale,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            };
            Canvas.SetLeft(lbl, cx - w / 2);
            Canvas.SetTop(lbl, cy - h / 2);
            ControllerCanvas.Children.Add(lbl);

            _activeButtons.Add(new TouchButtonInfo
            {
                Name = name,
                Vk = vk,
                Visual = btn
            });
        }

        // ══════════════════════════════════════════════════════
        // TOUCH EVENTS HANDLING
        // ══════════════════════════════════════════════════════

        private void Overlay_TouchDown(object? sender, TouchEventArgs e)
        {
            if (!_controlsVisible) return;

            var pos = e.GetTouchPoint(OverlayCanvas).Position;
            int touchId = e.TouchDevice.Id;

            // Check Analog Stick
            if (_stickKnob != null && Distance(pos, _stickCenter) <= _stickRadius + 30 && _stickTouchId == -1)
            {
                _stickTouchId = touchId;
                UpdateStick(pos);
                e.Handled = true;
                return;
            }

            // Check C-Stick (if present)
            if (_cStickKnob != null && Distance(pos, _cStickCenter) <= _cStickRadius + 25 && _cStickTouchId == -1)
            {
                _cStickTouchId = touchId;
                UpdateCStick(pos);
                e.Handled = true;
                return;
            }

            // Check Buttons
            foreach (var btn in _activeButtons)
            {
                if (btn.TouchId == -1 && HitTestEllipse(btn.Visual, pos))
                {
                    btn.TouchId = touchId;
                    btn.IsPressed = true;
                    btn.Visual.Fill = new SolidColorBrush(Color.FromArgb(180, 0, 229, 255));
                    SendKey(btn.Vk, true);
                    e.Handled = true;
                    return;
                }
            }
        }

        private void Overlay_TouchMove(object? sender, TouchEventArgs e)
        {
            if (!_controlsVisible) return;

            int touchId = e.TouchDevice.Id;
            var pos = e.GetTouchPoint(OverlayCanvas).Position;

            if (touchId == _stickTouchId)
            {
                UpdateStick(pos);
                e.Handled = true;
            }
            else if (touchId == _cStickTouchId)
            {
                UpdateCStick(pos);
                e.Handled = true;
            }
        }

        private void Overlay_TouchUp(object? sender, TouchEventArgs e)
        {
            int touchId = e.TouchDevice.Id;

            if (touchId == _stickTouchId)
            {
                _stickTouchId = -1;
                ResetStick();
                e.Handled = true;
                return;
            }

            if (touchId == _cStickTouchId)
            {
                _cStickTouchId = -1;
                ResetCStick();
                e.Handled = true;
                return;
            }

            foreach (var btn in _activeButtons)
            {
                if (btn.TouchId == touchId)
                {
                    btn.TouchId = -1;
                    btn.IsPressed = false;
                    byte alpha = (byte)(_globalOpacity * 255);
                    btn.Visual.Fill = new SolidColorBrush(Color.FromArgb((byte)(alpha * 0.5), 255, 255, 255));
                    SendKey(btn.Vk, false);
                    e.Handled = true;
                    return;
                }
            }
        }

        private void UpdateStick(Point touchPos)
        {
            if (_stickKnob == null) return;

            double dx = touchPos.X - _stickCenter.X;
            double dy = touchPos.Y - _stickCenter.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            if (dist > _stickRadius)
            {
                dx = dx / dist * _stickRadius;
                dy = dy / dist * _stickRadius;
            }

            Canvas.SetLeft(_stickKnob, _stickCenter.X + dx - _knobRadius);
            Canvas.SetTop(_stickKnob, _stickCenter.Y + dy - _knobRadius);

            double nx = dx / _stickRadius;
            double ny = dy / _stickRadius;

            bool up = ny < -_deadZone;
            bool down = ny > _deadZone;
            bool left = nx < -_deadZone;
            bool right = nx > _deadZone;

            SetDirectionKey(ref _keyUp, up, VK_UP);
            SetDirectionKey(ref _keyDown, down, VK_DOWN);
            SetDirectionKey(ref _keyLeft, left, VK_LEFT);
            SetDirectionKey(ref _keyRight, right, VK_RIGHT);
        }

        private void ResetStick()
        {
            if (_stickKnob == null) return;
            Canvas.SetLeft(_stickKnob, _stickCenter.X - _knobRadius);
            Canvas.SetTop(_stickKnob, _stickCenter.Y - _knobRadius);

            SetDirectionKey(ref _keyUp, false, VK_UP);
            SetDirectionKey(ref _keyDown, false, VK_DOWN);
            SetDirectionKey(ref _keyLeft, false, VK_LEFT);
            SetDirectionKey(ref _keyRight, false, VK_RIGHT);
        }

        private void UpdateCStick(Point touchPos)
        {
            if (_cStickKnob == null) return;

            double dx = touchPos.X - _cStickCenter.X;
            double dy = touchPos.Y - _cStickCenter.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            if (dist > _cStickRadius)
            {
                dx = dx / dist * _cStickRadius;
                dy = dy / dist * _cStickRadius;
            }

            Canvas.SetLeft(_cStickKnob, _cStickCenter.X + dx - _cKnobRadius);
            Canvas.SetTop(_cStickKnob, _cStickCenter.Y + dy - _cKnobRadius);

            double nx = dx / _cStickRadius;
            double ny = dy / _cStickRadius;

            bool up = ny < -_deadZone;
            bool down = ny > _deadZone;
            bool left = nx < -_deadZone;
            bool right = nx > _deadZone;

            SetDirectionKey(ref _cKeyUp, up, VK_I);
            SetDirectionKey(ref _cKeyDown, down, VK_K);
            SetDirectionKey(ref _cKeyLeft, left, VK_J);
            SetDirectionKey(ref _cKeyRight, right, VK_L);
        }

        private void ResetCStick()
        {
            if (_cStickKnob == null) return;
            Canvas.SetLeft(_cStickKnob, _cStickCenter.X - _cKnobRadius);
            Canvas.SetTop(_cStickKnob, _cStickCenter.Y - _cKnobRadius);

            SetDirectionKey(ref _cKeyUp, false, VK_I);
            SetDirectionKey(ref _cKeyDown, false, VK_K);
            SetDirectionKey(ref _cKeyLeft, false, VK_J);
            SetDirectionKey(ref _cKeyRight, false, VK_L);
        }

        private void SetDirectionKey(ref bool current, bool desired, ushort vk)
        {
            if (current == desired) return;
            current = desired;
            SendKey(vk, desired);
        }

        // ══════════════════════════════════════════════════════
        // INPUT SENDING & WIN32 HELPERS
        // ══════════════════════════════════════════════════════

        private static void SendSingleKey(ushort vk)
        {
            SendKey(vk, true);
            SendKey(vk, false);
        }

        private static void SendKey(ushort vk, bool press)
        {
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vk,
                        wScan = 0,
                        dwFlags = press ? KEYEVENTF_KEYDOWN : KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        private static bool HitTestEllipse(Ellipse el, Point pos)
        {
            double cx = Canvas.GetLeft(el) + el.Width / 2;
            double cy = Canvas.GetTop(el) + el.Height / 2;
            double rx = el.Width / 2 + 15;
            double ry = el.Height / 2 + 15;
            double dx = pos.X - cx;
            double dy = pos.Y - cy;
            return (dx * dx) / (rx * rx) + (dy * dy) / (ry * ry) <= 1.0;
        }

        private static double Distance(Point a, Point b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // ══════════════════════════════════════════════════════
        // SMART BATTERY & PERFORMANCE MONITOR
        // ══════════════════════════════════════════════════════

        private void InitBatteryMonitor()
        {
            _batteryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _batteryTimer.Tick += (s, e) => UpdateBatteryStatus();
            _batteryTimer.Start();
            UpdateBatteryStatus();
        }

        private void UpdateBatteryStatus()
        {
            if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
            {
                bool isAc = status.ACLineStatus == 1;
                int percent = status.BatteryLifePercent;

                string label = isAc
                    ? "⚡ AC Boost: 60 FPS Maksimal"
                    : $"🔋 Baterai: {percent}% (Mode Dingin & Hemat Daya)";

                HubBatteryText.Text = label;
                TxtOverlayBattery.Text = isAc
                    ? "⚡ Mode Daya: Colok Listrik (Performa Penuh)"
                    : $"🔋 Mode Daya: Baterai ({percent}% - Dingin & Efisien)";

                if (isAc)
                {
                    HubBatteryText.Foreground = new SolidColorBrush(Color.FromRgb(0, 229, 255));
                    HubBatteryPill.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 229, 255));
                }
                else
                {
                    HubBatteryText.Foreground = new SolidColorBrush(Color.FromRgb(255, 183, 77));
                    HubBatteryPill.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 183, 77));
                }
            }
        }
    }
}