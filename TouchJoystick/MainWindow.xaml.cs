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

namespace TouchJoystick
{
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

        // Virtual key codes matching Dolphin's default keyboard mappings
        private const ushort VK_UP = 0x26;
        private const ushort VK_DOWN = 0x28;
        private const ushort VK_LEFT = 0x25;
        private const ushort VK_RIGHT = 0x27;
        private const ushort VK_X = 0x58;      // A button
        private const ushort VK_Z = 0x5A;      // B button
        private const ushort VK_S = 0x53;      // X button
        private const ushort VK_A = 0x41;      // Y button
        private const ushort VK_RETURN = 0x0D; // Start
        private const ushort VK_Q = 0x51;      // L trigger
        private const ushort VK_W = 0x57;      // R trigger

        // ── Process Management ──
        private Process? _dolphinProcess;

        // ── UI Elements ──
        private Canvas? _controlsContainer;
        private Ellipse _stickBase = null!;
        private Ellipse _stickKnob = null!;
        private readonly Dictionary<string, Ellipse> _buttons = new();
        private readonly Dictionary<string, Border> _buttonLabels = new();
        private Ellipse _startBtn = null!;
        private Ellipse _lBtn = null!;
        private Ellipse _rBtn = null!;
        private Border _togglePill = null!;

        // ── Touch State ──
        private int _stickTouchId = -1;
        private Point _stickCenter;
        private readonly double _stickRadius = 75;
        private readonly double _knobRadius = 32;
        private readonly double _deadZone = 0.15;

        private bool _keyUp, _keyDown, _keyLeft, _keyRight;
        private readonly Dictionary<string, bool> _buttonPressed = new()
        {
            {"A", false}, {"B", false}, {"X", false}, {"Y", false},
            {"Start", false}, {"L", false}, {"R", false}
        };
        private readonly Dictionary<string, int> _buttonTouchIds = new()
        {
            {"A", -1}, {"B", -1}, {"X", -1}, {"Y", -1},
            {"Start", -1}, {"L", -1}, {"R", -1}
        };

        private bool _controlsVisible = true;

        // ── Brushes ──
        private readonly SolidColorBrush _baseBrush = new(Color.FromArgb(50, 255, 255, 255));
        private readonly SolidColorBrush _knobBrush = new(Color.FromArgb(130, 0, 180, 255));
        private readonly SolidColorBrush _btnBrush = new(Color.FromArgb(60, 255, 255, 255));
        private readonly SolidColorBrush _btnPressedBrush = new(Color.FromArgb(160, 0, 180, 255));
        private readonly SolidColorBrush _borderBrush = new(Color.FromArgb(120, 255, 255, 255));
        private readonly SolidColorBrush _textBrush = new(Color.FromArgb(220, 255, 255, 255));

        public MainWindow()
        {
            InitializeComponent();

            Loaded += OnLoaded;

            // Global keyboard shortcuts
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.J && Keyboard.Modifiers == ModifierKeys.Control)
                    ToggleControlsVisibility();
                else if (e.Key == Key.Escape)
                    ToggleControlsVisibility();
            };
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, new IntPtr(exStyle.ToInt64() | WS_EX_TOOLWINDOW));

            // 1. Launch / Attach Dolphin
            LaunchOrAttachDolphin();

            // 2. Check Touchscreen
            int digitizer = GetSystemMetrics(SM_DIGITIZER);
            bool hasTouch = (digitizer & NID_MULTI_INPUT) != 0 && (digitizer & NID_READY) != 0;

            if (hasTouch)
            {
                CreateControls();

                OverlayCanvas.TouchDown += Canvas_TouchDown;
                OverlayCanvas.TouchMove += Canvas_TouchMove;
                OverlayCanvas.TouchUp += Canvas_TouchUp;
            }
            else
            {
                // Non-touch device: hide overlay but keep monitoring Dolphin
                Visibility = Visibility.Hidden;
            }
        }

        private void LaunchOrAttachDolphin()
        {
            try
            {
                var existingProcesses = Process.GetProcessesByName("Dolphin");
                if (existingProcesses.Length > 0)
                {
                    _dolphinProcess = existingProcesses[0];
                }
                else
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string dolphinExe = System.IO.Path.Combine(baseDir, "Dolphin.exe");

                    if (!File.Exists(dolphinExe))
                    {
                        // Search in adjacent folders if any
                        string parentDir = Directory.GetParent(baseDir)?.FullName ?? baseDir;
                        string altDolphin = System.IO.Path.Combine(parentDir, "Dolphin-x64", "Dolphin.exe");
                        if (File.Exists(altDolphin))
                            dolphinExe = altDolphin;
                    }

                    if (File.Exists(dolphinExe))
                    {
                        var startInfo = new ProcessStartInfo
                        {
                            FileName = dolphinExe,
                            WorkingDirectory = System.IO.Path.GetDirectoryName(dolphinExe)!
                        };
                        _dolphinProcess = Process.Start(startInfo);
                    }
                }

                if (_dolphinProcess != null)
                {
                    _dolphinProcess.EnableRaisingEvents = true;
                    _dolphinProcess.Exited += (s, ev) =>
                    {
                        Dispatcher.Invoke(() => Application.Current.Shutdown());
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Dolphin-Surface] Error managing Dolphin process: {ex.Message}");
            }
        }

        private void CreateControls()
        {
            double w = OverlayCanvas.ActualWidth > 0 ? OverlayCanvas.ActualWidth : SystemParameters.PrimaryScreenWidth;
            double h = OverlayCanvas.ActualHeight > 0 ? OverlayCanvas.ActualHeight : SystemParameters.PrimaryScreenHeight;

            _controlsContainer = new Canvas
            {
                Width = w,
                Height = h,
                Background = Brushes.Transparent,
                IsHitTestVisible = true
            };
            OverlayCanvas.Children.Add(_controlsContainer);

            // ── Top Center: Touch Toggle Pill (so user can toggle controls without a keyboard) ──
            double pillW = 80;
            double pillH = 28;
            _togglePill = new Border
            {
                Width = pillW,
                Height = pillH,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)),
                BorderBrush = _borderBrush,
                BorderThickness = new Thickness(1),
                IsHitTestVisible = true,
                Child = new TextBlock
                {
                    Text = "🎮 Touch",
                    Foreground = _textBrush,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Canvas.SetLeft(_togglePill, (w - pillW) / 2);
            Canvas.SetTop(_togglePill, 12);
            _togglePill.TouchDown += (s, e) =>
            {
                ToggleControlsVisibility();
                e.Handled = true;
            };
            _togglePill.MouseDown += (s, e) =>
            {
                ToggleControlsVisibility();
                e.Handled = true;
            };
            OverlayCanvas.Children.Add(_togglePill);

            // ── Left: Analog Stick ──
            double stickX = 140;
            double stickY = h - 180;
            _stickCenter = new Point(stickX, stickY);

            _stickBase = new Ellipse
            {
                Width = _stickRadius * 2,
                Height = _stickRadius * 2,
                Fill = _baseBrush,
                Stroke = _borderBrush,
                StrokeThickness = 2,
                IsHitTestVisible = true
            };
            Canvas.SetLeft(_stickBase, stickX - _stickRadius);
            Canvas.SetTop(_stickBase, stickY - _stickRadius);
            _controlsContainer.Children.Add(_stickBase);

            _stickKnob = new Ellipse
            {
                Width = _knobRadius * 2,
                Height = _knobRadius * 2,
                Fill = _knobBrush,
                Stroke = _borderBrush,
                StrokeThickness = 1.5,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(_stickKnob, stickX - _knobRadius);
            Canvas.SetTop(_stickKnob, stickY - _knobRadius);
            _controlsContainer.Children.Add(_stickKnob);

            // ── Right: A/B/X/Y Buttons (GameCube diamond layout) ──
            double btnCenterX = w - 140;
            double btnCenterY = h - 180;
            double spacing = 65;

            CreateButton("A", btnCenterX, btnCenterY + spacing, "A");
            CreateButton("B", btnCenterX - spacing, btnCenterY, "B");
            CreateButton("X", btnCenterX + spacing, btnCenterY, "X");
            CreateButton("Y", btnCenterX, btnCenterY - spacing, "Y");

            // ── Start Button (Center Bottom) ──
            double startX = w / 2;
            double startY = h - 60;
            _startBtn = new Ellipse
            {
                Width = 46,
                Height = 46,
                Fill = _btnBrush,
                Stroke = _borderBrush,
                StrokeThickness = 1.5,
                IsHitTestVisible = true,
                Tag = "Start"
            };
            Canvas.SetLeft(_startBtn, startX - 23);
            Canvas.SetTop(_startBtn, startY - 23);
            _controlsContainer.Children.Add(_startBtn);

            var startLabel = CreateLabel("▶", startX, startY, 46);
            _controlsContainer.Children.Add(startLabel);

            // ── L/R Triggers (Top Corners) ──
            _lBtn = CreateTriggerButton("L", 100, 50, "L");
            _rBtn = CreateTriggerButton("R", w - 100, 50, "R");
        }

        private void CreateButton(string name, double cx, double cy, string label)
        {
            if (_controlsContainer == null) return;

            double btnSize = 58;
            var btn = new Ellipse
            {
                Width = btnSize,
                Height = btnSize,
                Fill = _btnBrush,
                Stroke = _borderBrush,
                StrokeThickness = 1.5,
                IsHitTestVisible = true,
                Tag = name
            };
            Canvas.SetLeft(btn, cx - btnSize / 2);
            Canvas.SetTop(btn, cy - btnSize / 2);
            _controlsContainer.Children.Add(btn);
            _buttons[name] = btn;

            var lbl = CreateLabel(label, cx, cy, btnSize);
            _controlsContainer.Children.Add(lbl);
            _buttonLabels[name] = lbl;
        }

        private Border CreateLabel(string text, double cx, double cy, double size)
        {
            var border = new Border
            {
                Width = size,
                Height = size,
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = _textBrush,
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            };
            Canvas.SetLeft(border, cx - size / 2);
            Canvas.SetTop(border, cy - size / 2);
            return border;
        }

        private Ellipse CreateTriggerButton(string name, double cx, double cy, string label)
        {
            double w = 75;
            double h = 42;
            var btn = new Ellipse
            {
                Width = w,
                Height = h,
                Fill = _btnBrush,
                Stroke = _borderBrush,
                StrokeThickness = 1.5,
                IsHitTestVisible = true,
                Tag = name
            };
            Canvas.SetLeft(btn, cx - w / 2);
            Canvas.SetTop(btn, cy - h / 2);
            _controlsContainer!.Children.Add(btn);

            var lbl = CreateLabel(label, cx, cy, w);
            lbl.Height = h;
            _controlsContainer.Children.Add(lbl);

            return btn;
        }

        // ── Touch Event Handlers ──

        private void Canvas_TouchDown(object? sender, TouchEventArgs e)
        {
            if (!_controlsVisible) return;

            var pos = e.GetTouchPoint(OverlayCanvas).Position;
            int touchId = e.TouchDevice.Id;

            // Check Analog Stick
            double distToStick = Distance(pos, _stickCenter);
            if (distToStick <= _stickRadius + 35 && _stickTouchId == -1)
            {
                _stickTouchId = touchId;
                UpdateStick(pos);
                e.Handled = true;
                return;
            }

            // Check A/B/X/Y Buttons
            if (TryHitButton("A", pos, touchId, VK_X)) { e.Handled = true; return; }
            if (TryHitButton("B", pos, touchId, VK_Z)) { e.Handled = true; return; }
            if (TryHitButton("X", pos, touchId, VK_S)) { e.Handled = true; return; }
            if (TryHitButton("Y", pos, touchId, VK_A)) { e.Handled = true; return; }

            // Start Button
            if (HitTestEllipse(_startBtn, pos))
            {
                _buttonTouchIds["Start"] = touchId;
                PressButton("Start", VK_RETURN);
                _startBtn.Fill = _btnPressedBrush;
                e.Handled = true;
                return;
            }

            // L Trigger
            if (HitTestEllipse(_lBtn, pos))
            {
                _buttonTouchIds["L"] = touchId;
                PressButton("L", VK_Q);
                _lBtn.Fill = _btnPressedBrush;
                e.Handled = true;
                return;
            }

            // R Trigger
            if (HitTestEllipse(_rBtn, pos))
            {
                _buttonTouchIds["R"] = touchId;
                PressButton("R", VK_W);
                _rBtn.Fill = _btnPressedBrush;
                e.Handled = true;
                return;
            }
        }

        private void Canvas_TouchMove(object? sender, TouchEventArgs e)
        {
            if (!_controlsVisible) return;

            if (e.TouchDevice.Id == _stickTouchId)
            {
                var pos = e.GetTouchPoint(OverlayCanvas).Position;
                UpdateStick(pos);
                e.Handled = true;
            }
        }

        private void Canvas_TouchUp(object? sender, TouchEventArgs e)
        {
            int touchId = e.TouchDevice.Id;

            if (touchId == _stickTouchId)
            {
                _stickTouchId = -1;
                ResetStick();
                e.Handled = true;
                return;
            }

            foreach (var kvp in _buttonTouchIds)
            {
                if (kvp.Value == touchId)
                {
                    string name = kvp.Key;
                    _buttonTouchIds[name] = -1;
                    ushort vk = name switch
                    {
                        "A" => VK_X,
                        "B" => VK_Z,
                        "X" => VK_S,
                        "Y" => VK_A,
                        "Start" => VK_RETURN,
                        "L" => VK_Q,
                        "R" => VK_W,
                        _ => 0
                    };
                    ReleaseButton(name, vk);

                    if (_buttons.TryGetValue(name, out var btn))
                        btn.Fill = _btnBrush;
                    if (name == "Start") _startBtn.Fill = _btnBrush;
                    if (name == "L") _lBtn.Fill = _btnBrush;
                    if (name == "R") _rBtn.Fill = _btnBrush;

                    e.Handled = true;
                    return;
                }
            }
        }

        // ── Stick Logic ──

        private void UpdateStick(Point touchPos)
        {
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
            Canvas.SetLeft(_stickKnob, _stickCenter.X - _knobRadius);
            Canvas.SetTop(_stickKnob, _stickCenter.Y - _knobRadius);

            SetDirectionKey(ref _keyUp, false, VK_UP);
            SetDirectionKey(ref _keyDown, false, VK_DOWN);
            SetDirectionKey(ref _keyLeft, false, VK_LEFT);
            SetDirectionKey(ref _keyRight, false, VK_RIGHT);
        }

        private void SetDirectionKey(ref bool current, bool desired, ushort vk)
        {
            if (current == desired) return;
            current = desired;
            SendKey(vk, desired);
        }

        // ── Button Logic ──

        private bool TryHitButton(string name, Point pos, int touchId, ushort vk)
        {
            if (!_buttons.TryGetValue(name, out var btn)) return false;
            if (!HitTestEllipse(btn, pos)) return false;

            _buttonTouchIds[name] = touchId;
            PressButton(name, vk);
            btn.Fill = _btnPressedBrush;
            return true;
        }

        private void PressButton(string name, ushort vk)
        {
            if (_buttonPressed[name]) return;
            _buttonPressed[name] = true;
            SendKey(vk, true);
        }

        private void ReleaseButton(string name, ushort vk)
        {
            if (!_buttonPressed[name]) return;
            _buttonPressed[name] = false;
            SendKey(vk, false);
        }

        // ── Input Sending ──

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

        // ── Helpers ──

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

        private void ToggleControlsVisibility()
        {
            _controlsVisible = !_controlsVisible;
            if (_controlsContainer != null)
            {
                _controlsContainer.Opacity = _controlsVisible ? 1.0 : 0.0;
            }
            if (_togglePill.Child is TextBlock tb)
            {
                tb.Text = _controlsVisible ? "🎮 Touch" : "👁️ Show";
            }
        }
    }
}