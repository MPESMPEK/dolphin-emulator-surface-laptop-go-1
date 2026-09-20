using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

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
        private const int WS_EX_TRANSPARENT = 0x00000020;
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

        // Virtual key codes — Dolphin default keyboard bindings
        private const ushort VK_UP = 0x26;
        private const ushort VK_DOWN = 0x28;
        private const ushort VK_LEFT = 0x25;
        private const ushort VK_RIGHT = 0x27;
        private const ushort VK_X = 0x58;  // A button
        private const ushort VK_Z = 0x5A;  // B button
        private const ushort VK_S = 0x53;  // X button
        private const ushort VK_A = 0x41;  // Y button
        private const ushort VK_RETURN = 0x0D; // Start
        private const ushort VK_Q = 0x51;  // L trigger
        private const ushort VK_W = 0x57;  // R trigger

        // ── UI Elements ──
        private Ellipse _stickBase = null!;
        private Ellipse _stickKnob = null!;
        private readonly Dictionary<string, Ellipse> _buttons = new();
        private readonly Dictionary<string, Border> _buttonLabels = new();
        private Ellipse _startBtn = null!;
        private Border _startLabel = null!;
        private Ellipse _lBtn = null!;
        private Border _lLabel = null!;
        private Ellipse _rBtn = null!;
        private Border _rLabel = null!;

        // ── Touch State ──
        private int _stickTouchId = -1;
        private Point _stickCenter;
        private readonly double _stickRadius = 70;
        private readonly double _knobRadius = 30;
        private readonly double _deadZone = 0.15;

        // Currently pressed direction keys (to avoid repeated SendInput)
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

        // Visibility toggle
        private bool _isVisible = true;
        private DispatcherTimer? _hideTimer;

        // ── Layout constants ──
        private readonly double _btnSize = 56;
        private readonly double _btnSpacing = 64;
        private readonly SolidColorBrush _baseBrush = new(Color.FromArgb(60, 255, 255, 255));
        private readonly SolidColorBrush _knobBrush = new(Color.FromArgb(120, 100, 200, 255));
        private readonly SolidColorBrush _btnBrush = new(Color.FromArgb(60, 255, 255, 255));
        private readonly SolidColorBrush _btnPressedBrush = new(Color.FromArgb(150, 100, 200, 255));
        private readonly SolidColorBrush _borderBrush = new(Color.FromArgb(100, 180, 180, 180));
        private readonly SolidColorBrush _textBrush = new(Color.FromArgb(200, 255, 255, 255));

        public MainWindow()
        {
            InitializeComponent();

            // Check touchscreen support
            int digitizer = GetSystemMetrics(SM_DIGITIZER);
            bool hasTouch = (digitizer & NID_MULTI_INPUT) != 0 && (digitizer & NID_READY) != 0;

            if (!hasTouch)
            {
                MessageBox.Show(
                    "Touchscreen tidak terdeteksi!\nAplikasi ini membutuhkan layar sentuh.",
                    "Touch Joystick", MessageBoxButton.OK, MessageBoxImage.Warning);
                Application.Current.Shutdown();
                return;
            }

            Loaded += OnLoaded;

            // Touch events
            OverlayCanvas.TouchDown += Canvas_TouchDown;
            OverlayCanvas.TouchMove += Canvas_TouchMove;
            OverlayCanvas.TouchUp += Canvas_TouchUp;

            // Keyboard shortcut to toggle visibility (Ctrl+J)
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.J && Keyboard.Modifiers == ModifierKeys.Control)
                    ToggleVisibility();
                if (e.Key == Key.Escape)
                    Close();
            };
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Make window click-through for non-control areas
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, new IntPtr(exStyle.ToInt64() | WS_EX_TOOLWINDOW));

            CreateControls();
        }

        private void CreateControls()
        {
            double w = OverlayCanvas.ActualWidth > 0 ? OverlayCanvas.ActualWidth : SystemParameters.PrimaryScreenWidth;
            double h = OverlayCanvas.ActualHeight > 0 ? OverlayCanvas.ActualHeight : SystemParameters.PrimaryScreenHeight;

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
            OverlayCanvas.Children.Add(_stickBase);

            _stickKnob = new Ellipse
            {
                Width = _knobRadius * 2,
                Height = _knobRadius * 2,
                Fill = _knobBrush,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(_stickKnob, stickX - _knobRadius);
            Canvas.SetTop(_stickKnob, stickY - _knobRadius);
            OverlayCanvas.Children.Add(_stickKnob);

            // ── Right: A/B/X/Y Buttons (diamond layout) ──
            double btnCenterX = w - 140;
            double btnCenterY = h - 180;

            // A = bottom (like GC controller)
            CreateButton("A", btnCenterX, btnCenterY + _btnSpacing, "A", VK_X);
            // B = left
            CreateButton("B", btnCenterX - _btnSpacing, btnCenterY, "B", VK_Z);
            // X = right
            CreateButton("X", btnCenterX + _btnSpacing, btnCenterY, "X", VK_S);
            // Y = top
            CreateButton("Y", btnCenterX, btnCenterY - _btnSpacing, "Y", VK_A);

            // ── Start Button (center bottom) ──
            double startX = w / 2;
            double startY = h - 60;
            _startBtn = new Ellipse
            {
                Width = 44,
                Height = 44,
                Fill = _btnBrush,
                Stroke = _borderBrush,
                StrokeThickness = 1.5,
                IsHitTestVisible = true,
                Tag = "Start"
            };
            Canvas.SetLeft(_startBtn, startX - 22);
            Canvas.SetTop(_startBtn, startY - 22);
            OverlayCanvas.Children.Add(_startBtn);

            _startLabel = CreateLabel("▶", startX, startY, 44);
            OverlayCanvas.Children.Add(_startLabel);

            // ── L/R Triggers (top corners) ──
            double triggerY = 50;
            _lBtn = CreateTriggerButton("L", 100, triggerY, "L");
            _rBtn = CreateTriggerButton("R", w - 100, triggerY, "R");
        }

        private void CreateButton(string name, double cx, double cy, string label, ushort vk)
        {
            var btn = new Ellipse
            {
                Width = _btnSize,
                Height = _btnSize,
                Fill = _btnBrush,
                Stroke = _borderBrush,
                StrokeThickness = 1.5,
                IsHitTestVisible = true,
                Tag = name
            };
            Canvas.SetLeft(btn, cx - _btnSize / 2);
            Canvas.SetTop(btn, cy - _btnSize / 2);
            OverlayCanvas.Children.Add(btn);
            _buttons[name] = btn;

            var lbl = CreateLabel(label, cx, cy, _btnSize);
            OverlayCanvas.Children.Add(lbl);
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
            var btn = new Ellipse
            {
                Width = 70,
                Height = 40,
                Fill = _btnBrush,
                Stroke = _borderBrush,
                StrokeThickness = 1.5,
                IsHitTestVisible = true,
                Tag = name
            };
            Canvas.SetLeft(btn, cx - 35);
            Canvas.SetTop(btn, cy - 20);
            OverlayCanvas.Children.Add(btn);

            var lbl = CreateLabel(label, cx, cy, 70);
            lbl.Height = 40;
            OverlayCanvas.Children.Add(lbl);

            if (name == "L") { _lLabel = lbl; }
            else { _rLabel = lbl; }

            return btn;
        }

        // ── Touch Event Handlers ──

        private void Canvas_TouchDown(object sender, TouchEventArgs e)
        {
            var pos = e.GetTouchPoint(OverlayCanvas).Position;
            int touchId = e.TouchDevice.Id;

            // Check if touch is on analog stick area (generous hit area)
            double distToStick = Distance(pos, _stickCenter);
            if (distToStick <= _stickRadius + 30 && _stickTouchId == -1)
            {
                _stickTouchId = touchId;
                UpdateStick(pos);
                e.Handled = true;
                return;
            }

            // Check buttons
            if (TryHitButton("A", pos, touchId, VK_X)) { e.Handled = true; return; }
            if (TryHitButton("B", pos, touchId, VK_Z)) { e.Handled = true; return; }
            if (TryHitButton("X", pos, touchId, VK_S)) { e.Handled = true; return; }
            if (TryHitButton("Y", pos, touchId, VK_A)) { e.Handled = true; return; }

            // Start button
            if (HitTestEllipse(_startBtn, pos))
            {
                _buttonTouchIds["Start"] = touchId;
                PressButton("Start", VK_RETURN);
                _startBtn.Fill = _btnPressedBrush;
                e.Handled = true;
                return;
            }

            // L trigger
            if (HitTestEllipse(_lBtn, pos))
            {
                _buttonTouchIds["L"] = touchId;
                PressButton("L", VK_Q);
                _lBtn.Fill = _btnPressedBrush;
                e.Handled = true;
                return;
            }

            // R trigger
            if (HitTestEllipse(_rBtn, pos))
            {
                _buttonTouchIds["R"] = touchId;
                PressButton("R", VK_W);
                _rBtn.Fill = _btnPressedBrush;
                e.Handled = true;
                return;
            }
        }

        private void Canvas_TouchMove(object sender, TouchEventArgs e)
        {
            if (e.TouchDevice.Id == _stickTouchId)
            {
                var pos = e.GetTouchPoint(OverlayCanvas).Position;
                UpdateStick(pos);
                e.Handled = true;
            }
        }

        private void Canvas_TouchUp(object sender, TouchEventArgs e)
        {
            int touchId = e.TouchDevice.Id;

            if (touchId == _stickTouchId)
            {
                _stickTouchId = -1;
                ResetStick();
                e.Handled = true;
                return;
            }

            // Release buttons
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

                    // Reset visual
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

            // Clamp to stick radius
            if (dist > _stickRadius)
            {
                dx = dx / dist * _stickRadius;
                dy = dy / dist * _stickRadius;
                dist = _stickRadius;
            }

            // Move knob visual
            Canvas.SetLeft(_stickKnob, _stickCenter.X + dx - _knobRadius);
            Canvas.SetTop(_stickKnob, _stickCenter.Y + dy - _knobRadius);

            // Normalize to -1..1
            double nx = dx / _stickRadius;
            double ny = dy / _stickRadius;

            // Apply deadzone
            bool up = ny < -_deadZone;
            bool down = ny > _deadZone;
            bool left = nx < -_deadZone;
            bool right = nx > _deadZone;

            // Send key events for direction changes
            SetDirectionKey(ref _keyUp, up, VK_UP);
            SetDirectionKey(ref _keyDown, down, VK_DOWN);
            SetDirectionKey(ref _keyLeft, left, VK_LEFT);
            SetDirectionKey(ref _keyRight, right, VK_RIGHT);
        }

        private void ResetStick()
        {
            // Return knob to center
            Canvas.SetLeft(_stickKnob, _stickCenter.X - _knobRadius);
            Canvas.SetTop(_stickKnob, _stickCenter.Y - _knobRadius);

            // Release all direction keys
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
            double rx = el.Width / 2 + 15; // generous hit area
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

        private void ToggleVisibility()
        {
            _isVisible = !_isVisible;
            OverlayCanvas.Opacity = _isVisible ? 1.0 : 0.0;
        }
    }
}