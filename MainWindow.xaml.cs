using D3H.Classes;
using System;
using System.Drawing; // Bitmap
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text.Json; // JSON 序列化/反序列化
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Xml.Linq;

//using System.Windows.Media;
using WindowsInput; // 鼠标键盘输入
using WindowsInput.Native; // VirtualKeyCode

// dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "%USERPROFILE%\Desktop\D3H"


namespace D3H   
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool isInitialized = false; // 初始化是否完成
        private bool isRecordingHotkey = false; // 快捷键是否正在录入
        private bool isRunning = false; // 刚按完快捷键后, 阻止一段时间快捷键监听
        private TextBox? currentRecordingTextBox = null;
        private static readonly Dictionary<string, int> hotkeyID = new()
        {
            { "战斗", 0 },
            { "冷却初始化", 1 },
            { "仅按住技能", 2 },
            { "日常", 100 },
            { "按左键", 101 }
        };
        private Dictionary<string, HotkeyBinding> hotkeysJSON = new();
        private Dictionary<string, (Key key, ModifierKeys mod)> hotkeys = new();
        // 战斗区
        private bool battle = false; // 是否在战斗
        private static readonly Dictionary<string, int> skillIndex = new()
        {
            { "技能1", 0 },
            { "技能2", 1 },
            { "技能3", 2 },
            { "技能4", 3 },
            { "左键技能", 4 },
            { "右键技能", 5 }
        };
        
        private (Key key, ModifierKeys mod)[] skillHotkeys = Enumerable.Repeat((Key.None, ModifierKeys.None), 4).ToArray(); // 技能快捷键
        private Timer[] timers = new Timer[6];
        private string[] modes = ["无", "无", "无", "无", "无", "无"];
        private TimerCallback[] callbacks = new TimerCallback[6]; // 放技能函数
        private int[] intervals = [0, 0, 0, 0, 0, 0]; // 释放间隔
        private Bitmap[] coldDownOK = new Bitmap[6]; // 冷却转好的图片
        private Bitmap[] latestScreenshots = new Bitmap[6]; // 冷却截图缓冲区
        private static readonly InputSimulator sim = new InputSimulator(); // 模拟按键


        // 日常区
        private Dictionary<string, bool> checks = new();
        private HashSet<int> safeZone = new();


        private static readonly D3UI d3UI = new D3UI();
        private AppSettings _settings; // 从 JSON 读取/保存数据
        private static readonly string[] skillNames = ["技能1", "技能2", "技能3", "技能4", "左键技能", "右键技能"];
        private static readonly Dictionary<string, int> modeIndex = new()
        {
            { "无", 0 },
            { "按住不放", 1 },
            { "好了就按", 2 },
            { "固定间隔", 3 }
        };

        #region Win32 API
        // 注册/注销系统热键所需的 Win32 API
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        // 修饰键定义
        const int MOD_ALT = 0x0001;     // Alt
        const int MOD_CONTROL = 0x0002; // Ctrl
        const int MOD_SHIFT = 0x0004;   // Shift

        // 获取键盘布局
        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint idThread);
        // 发送 Windows 消息
        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050; // Windows消息常量 - 请求更改输入语言
        private const uint INPUTLANGCHANGE_SYSCHARSET = 0x0001; // 输入语言更改标志 - 使用系统字符集
        private static readonly IntPtr ENGLISH_LAYOUT = (IntPtr)0x04090409; // 英语(美国)

        // 获取屏幕分辨率
        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int nIndex);
        int screenWidth = GetSystemMetrics(0);
        int screenHeight = GetSystemMetrics(1);

        // 控制台
        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();
        #endregion

        public MainWindow()
        {
            #if DEBUG
                AllocConsole(); // 打开控制台窗口
            #endif
            InitializeComponent();
            isInitialized = true;
            _settings = LoadSettingsFile();

            // 切换窗口时, 取消快捷键录入状态
            Deactivated += (sender, e) =>
            {
                isRecordingHotkey = false;
                currentRecordingTextBox = null;
            };


            // 设为上次的设置
            Loaded += (s, e) => LoadSettings();
            // 禁用输入法（强制英文模式）
            Loaded += (s, e) =>
            {
                var hWnd = new WindowInteropHelper(this).Handle;
                PostMessage(hWnd, WM_INPUTLANGCHANGEREQUEST,
                           (IntPtr)INPUTLANGCHANGE_SYSCHARSET, ENGLISH_LAYOUT);
            };
            Closing += MainWindow_Closing;
        }

        #region JSON

        /// <summary>
        /// 获取设置文件路径
        /// </summary>
        private string GetSettingsFilePath()
        {
            string folder = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(folder, "settings.json");
        }

        /// <summary>
        /// 读取设置文件
        /// </summary>
        private AppSettings LoadSettingsFile()
        {
            string filePath = GetSettingsFilePath();

            // 第一次启动或文件被删
            if (!File.Exists(filePath))
            {
                return new AppSettings();
            }

            try
            {
                Console.WriteLine($"文件是否存在：{File.Exists(filePath)}");
                Console.WriteLine($"文件内容：{File.ReadAllText(filePath)}");
                string json = File.ReadAllText(filePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                return settings ?? new AppSettings();
            }
            catch
            {
                Console.WriteLine("???");
                return new AppSettings();
            }
        }

        /// <summary>
        /// 将设置保存到文件
        /// </summary>
        private void SaveSettingsFile(AppSettings settings)
        {
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            string filePath = GetSettingsFilePath();
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// 将 _settings 读取到各设置
        /// </summary>
        private void LoadSettings()
        {
            // 读取并设置系统快捷键
            isRecordingHotkey = true;
            foreach (var item in _settings.hotkeys)
            {
                hotkeysJSON[item.Key] = item.Value;
                string keyString = item.Value.key;
                string modString = item.Value.mod;
                Enum.TryParse(keyString, ignoreCase: true, out Key key);
                Enum.TryParse(modString, ignoreCase: true, out ModifierKeys mod);
                currentRecordingTextBox = (TextBox)FindName(item.Key);
                SetHotKey(key, mod);
            }

            // 读取并设置技能快捷键
            for (int index = 0; index < 4; index++)
            {
                string keyString = _settings.skillHotkeys[index].key;
                string modString = _settings.skillHotkeys[index].mod;
                Enum.TryParse(keyString, ignoreCase: true, out Key key);
                Enum.TryParse(modString, ignoreCase: true, out ModifierKeys mod);
                currentRecordingTextBox = (TextBox)FindName(skillNames[index]);
                SetHotKey(key, mod);
            }
            currentRecordingTextBox = null;
            isRecordingHotkey = false;

            // 读取并设置技能释放方式
            for (int index = 0; index < 6; index++)
            {
                string mode = _settings.modes[index];
                modes[index] = mode;
                ComboBox checkBox = (ComboBox)FindName(skillNames[index] + "模式");
                checkBox.SelectedIndex = modeIndex[mode];

                intervals[index] = _settings.intervals[index];
                TextBox textBox = (TextBox)FindName(skillNames[index] + "间隔");
                textBox.Text = intervals[index].ToString();
            }

            // 读取 checks 设置
            foreach (var item in _settings.checks)
            {
                CheckBox checkBox = (CheckBox)FindName(item.Key);
                checkBox.IsChecked = item.Value;
                SetCheck(item.Key, item.Value);
            }

            // 若未选中键盘代替左键, 则注销快捷键
            if (!checks["键盘代替左键"])
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                UnregisterHotKey(hwnd, hotkeyID["按左键"]);
            }

            // 读取安全格
            safeZone = JsonSerializer.Deserialize<HashSet<int>>(_settings.safeZone) ?? new HashSet<int>();
        }

        /// <summary>
        /// 保存设置, 并存到设置文件
        /// </summary>
        private void SaveSettings()
        {
            // 保存系统快捷键
            foreach (var item in hotkeysJSON)
            {
                _settings.hotkeys[item.Key] = item.Value;
            }
            // 保存技能快捷键
            for (int index = 0; index < 4; index++)
            {
                string key = skillHotkeys[index].key.ToString();
                string mod = skillHotkeys[index].mod.ToString();
                _settings.skillHotkeys[index] = new HotkeyBinding(key, mod);
            }
            // 保存技能释放方式
            for (int index = 0; index < 6; index++)
            {
                _settings.modes[index] = modes[index];
                _settings.intervals[index] = intervals[index];
            }
            // 保存 checks 设置
            foreach (var item in checks)
            {
                _settings.checks[item.Key] = item.Value;
            }
            // 保存安全格
            _settings.safeZone = JsonSerializer.Serialize(safeZone);

            SaveSettingsFile(_settings);
        }

        /// <summary>
        /// 保存按钮
        /// </summary>
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
        }

        #endregion

        #region 快捷键

        /// <summary>
        /// Key -> string
        /// </summary>
        private static string KeyToString(Key key)
        {
            return key switch
            {
                >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(), // D0–D9 → 0–9
                >= Key.NumPad0 and <= Key.NumPad9 => "Num " + ((int)key - (int)Key.NumPad0), // 小键盘
                Key.OemTilde => "`",
                Key.OemMinus => "-",
                Key.OemPlus => "=",
                Key.OemOpenBrackets => "[",
                Key.OemCloseBrackets => "]",
                Key.OemPipe => "\\",
                Key.OemSemicolon => ";",
                Key.OemQuotes => "'",
                Key.OemComma => ",",
                Key.OemPeriod => ".",
                Key.OemQuestion => "/",

                // 一些特殊键的自定义名称
                Key.Return => "Enter",
                Key.Back => "Backspace",
                Key.Space => "Space",
                Key.Tab => "Tab",
                Key.Escape => "Esc",
                _ => key.ToString() // 默认保持原始名字
            };
        }

        /// <summary>
        /// 手动设置快捷键
        /// </summary>
        private void SetHotKey(Key key, ModifierKeys modifiers)
        {
            List<string> keys = new List<string>();

            if (modifiers.HasFlag(ModifierKeys.Control)) keys.Add("Ctrl");
            if (modifiers.HasFlag(ModifierKeys.Alt)) keys.Add("Alt");
            if (modifiers.HasFlag(ModifierKeys.Shift)) keys.Add("Shift");

            if (key != Key.LeftCtrl && key != Key.RightCtrl &&
                key != Key.LeftAlt && key != Key.RightAlt &&
                key != Key.LeftShift && key != Key.RightShift &&
                key != Key.None)
            {
                keys.Add(KeyToString(key));
            }

            // 显示快捷键组合
            currentRecordingTextBox?.Text = string.Join(" + ", keys);


            string name = currentRecordingTextBox?.Name ?? "技能?";
            Console.WriteLine($"{name} 的快捷键设置为 {currentRecordingTextBox?.Text}");
            int fsModifiers = 0;
            if (modifiers.HasFlag(ModifierKeys.Alt)) fsModifiers |= MOD_ALT;
            if (modifiers.HasFlag(ModifierKeys.Control)) fsModifiers |= MOD_CONTROL;
            if (modifiers.HasFlag(ModifierKeys.Shift)) fsModifiers |= MOD_SHIFT;
            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (hotkeyID.ContainsKey(name))
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                UnregisterHotKey(hwnd, hotkeyID[name]);
                RegisterHotKey(hwnd, hotkeyID[name], fsModifiers, vk);
                hotkeysJSON[name] = new HotkeyBinding(key.ToString(), modifiers.ToString());
                hotkeys[name] = (key, modifiers);
            }
            else
            {
                int index = skillIndex[name];
                skillHotkeys[index] = (key, modifiers);
            }
        }

        /// <summary>
        /// 输入框录入快捷键
        /// </summary>
        private void TextBox_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            e.Handled = true; // 阻止 TextBox 正常输入字符
            Key key = (e.Key == Key.System) ? e.SystemKey : e.Key;
            SetHotKey(key, e.KeyboardDevice.Modifiers);
        }

        /// <summary>
        /// 阻止快捷键触发, 并记录当前输入框
        /// </summary>
        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            isRecordingHotkey = true;
            currentRecordingTextBox = sender as TextBox;
        }

        /// <summary>
        /// 允许快捷键触发
        /// </summary>
        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            currentRecordingTextBox = null;
            isRecordingHotkey = false;
        }

        #endregion

        #region 技能释放方式

        /// <summary>
        /// 释放第 index 个技能
        /// </summary>
        /// <param name="type">0: 按一次, 1: 按住, 2: 松开</param>
        private void Cast(int index, int type = 0)
        {
            if (type == 0) Console.Write("释放");
            else if (type == 1) Console.Write("按下");
            else if (type == 2) Console.Write("松开");
            Console.WriteLine($"技能 {index}");
            if (index == 4)
            {
                if (type != 2) sim.Mouse.LeftButtonDown();
                if (type != 1) sim.Mouse.LeftButtonUp();
                return;
            }
            else if (index == 5)
            {
                if (type != 2) sim.Mouse.RightButtonDown();
                if (type != 1) sim.Mouse.RightButtonUp();
                return;
            }

            Key key = skillHotkeys[index].key;
            ModifierKeys modifiers = skillHotkeys[index].mod;
            // 转换 WPF 的 Key 到 InputSimulator 的 VirtualKeyCode
            var keyCode = (VirtualKeyCode)KeyInterop.VirtualKeyFromKey(key);

            if (type != 2)
            {
                // 按下修饰键
                if (modifiers.HasFlag(ModifierKeys.Control))
                    sim.Keyboard.KeyDown(VirtualKeyCode.CONTROL);

                if (modifiers.HasFlag(ModifierKeys.Alt))
                    sim.Keyboard.KeyDown(VirtualKeyCode.MENU); // Alt 键的代码是 MENU

                if (modifiers.HasFlag(ModifierKeys.Shift))
                    sim.Keyboard.KeyDown(VirtualKeyCode.SHIFT);

                // 按下主键
                sim.Keyboard.KeyDown(keyCode);
            }

            if (type != 1)
            {
                // 释放主键
                sim.Keyboard.KeyUp(keyCode);

                // 释放修饰键
                if (modifiers.HasFlag(ModifierKeys.Shift))
                    sim.Keyboard.KeyUp(VirtualKeyCode.SHIFT);

                if (modifiers.HasFlag(ModifierKeys.Alt))
                    sim.Keyboard.KeyUp(VirtualKeyCode.MENU);

                if (modifiers.HasFlag(ModifierKeys.Control))
                    sim.Keyboard.KeyUp(VirtualKeyCode.CONTROL);
            }
        }

        /// <summary>
        /// 对 rect 区域截图
        /// </summary>
        private static Bitmap ScreenShot(Rect rect)
        {
            Bitmap bmp = new Bitmap((int)Math.Round(rect.Width), (int)Math.Round(rect.Height));
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen((int)Math.Round(rect.X), (int)Math.Round(rect.Y), 0, 0,
                    new System.Drawing.Size((int)Math.Round(rect.Width), (int)Math.Round(rect.Height)));
            }
            return bmp;
        }

        /// <summary>
        /// 对第 index 个技能的冷却进行截图
        /// </summary>
        private static Bitmap GetColdDownImage(int index)
        {
            return ScreenShot(d3UI.skillRects[index]);
        }

        /// <summary>
        /// 计算两个图片的相似度
        /// </summary>
        private static double CalculateSimilarity(Bitmap? bmp1, Bitmap? bmp2)
        {
            if (bmp1 == null || bmp2 == null)
                return 0;

            if (bmp1.Width != bmp2.Width || bmp1.Height != bmp2.Height)
                return 0;

            double mse = 0;
            int width = bmp1.Width;
            int height = bmp1.Height;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color c1 = bmp1.GetPixel(x, y);
                    Color c2 = bmp2.GetPixel(x, y);

                    double dr = c1.R - c2.R;
                    double dg = c1.G - c2.G;
                    double db = c1.B - c2.B;

                    mse += (dr * dr + dg * dg + db * db) / 3.0;
                }
            }

            mse /= (width * height);

            double maxMse = 255 * 255;
            double similarity = 1.0 - (mse / maxMse);
            Console.WriteLine(similarity);
            return similarity;
        }

        /// <summary>
        /// 好了就按技能
        /// </summary>
        private void SmartCast(int index)
        {
            latestScreenshots[index]?.Dispose();
            latestScreenshots[index] = GetColdDownImage(index);
            double similarity = CalculateSimilarity(coldDownOK[index], latestScreenshots[index]);
            if (similarity > 0.999)
            {
                Cast(index);
            }
        }

        /// <summary>
        /// 选项框修改技能模式
        /// </summary>
        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isInitialized) return;
            var comboBox = (ComboBox)sender;
            string name = comboBox.Name[..^2];
            int index = skillIndex[name];

            string mode = ((ComboBoxItem)comboBox.SelectedItem)?.Content?.ToString() ?? "无";
            modes[index] = mode; // 记录当前模式

            switch (mode)
            {
                case "无":
                case "按住不放":
                    break;
                case "好了就按":
                    callbacks[index] = ( _ => SmartCast(index));
                    break;
                case "固定间隔":
                    callbacks[index] = (_ => Cast(index));
                    break;
            }
            Console.WriteLine($"第 {index} 个技能的案件模式 = {mode}");

            var intervalBox = FindName(name + "间隔") as TextBox;
            intervalBox?.IsEnabled = (mode == "固定间隔");
        }

        /// <summary>
        /// 设置冷却间隔 (阻止输入非数字符号)
        /// </summary>
        private void IntTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = (TextBox)sender;
            string text = textBox.Text;

            // 只保留数字字符和可选负号（根据需求调整）
            string filtered = new string(text.Where(c => char.IsDigit(c)).ToArray());

            // 清除先导 0, 当结果为空的时候设置为 0
            filtered = filtered.TrimStart('0');
            if (filtered.Length == 0)
            {
                filtered = "0";
            }

            if (text != filtered)
            {
                int selStart = textBox.SelectionStart - (text.Length - filtered.Length);
                if (selStart < 0) selStart = 0;

                textBox.Text = filtered;
                textBox.SelectionStart = selStart;
            }

            // 存储并设置间隔
            string name = textBox.Name[..^2];
            int index = skillIndex[name];
            intervals[index] = int.Parse(filtered);
        }

        #endregion

        #region 日常功能

        /// <summary>
        /// 设置 check
        /// </summary>
        private void SetCheck(string name, bool isChecked)
        {
            checks[name] = isChecked;
            Console.WriteLine($"{name} 状态：{isChecked}");
        }

        /// <summary>
        /// 点击 CheckBox 时的响应
        /// </summary>
        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            var checkbox = (CheckBox)sender;
            SetCheck(checkbox.Name, checkbox?.IsChecked ?? false);
        }

        /// <summary>
        /// 取消键盘代替鼠标时, 注销快捷键
        /// </summary>
        private void CheckBox_Click_LeftButton(object sender, RoutedEventArgs e)
        {
            var checkbox = (CheckBox)sender;
            SetCheck(checkbox.Name, checkbox?.IsChecked ?? false);
            if (!checks["键盘代替左键"])
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                UnregisterHotKey(hwnd, hotkeyID["按左键"]);
            }
            else
            {
                currentRecordingTextBox = (TextBox)FindName("按左键");
                SetHotKey(hotkeys["按左键"].key, hotkeys["按左键"].mod);
                currentRecordingTextBox = null;
            }
        }

        /// <summary>
        /// 移动鼠标到 (x, y)
        /// </summary>
        private void MoveMouseTo(double x, double y)
        {
            sim.Mouse.MoveMouseTo(65535 * x / screenWidth, 65535 * y / screenHeight);
        }

        /// <summary>
        /// 获取 rect 区域 RGB 各自最大值
        /// </summary>
        private (int r, int g, int b) ScreenShotMax(Rect rect)
        {
            Bitmap tmp = ScreenShot(rect);
            int width = tmp.Width;
            int height = tmp.Height;

            int r = 0, g = 0, b = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color c = tmp.GetPixel(x, y);
                    r = Math.Max(r, c.R);
                    g = Math.Max(g, c.G);
                    b = Math.Max(b, c.B);
                }
            }
            tmp.Dispose();
            return (r, g, b);
        }

        /// <summary>
        /// 把鼠标移到第 index 个格子并返回物品品质
        /// </summary>
        private async Task<int> GetItemQuality(int index)
        {
            Rect rect = d3UI.backpackRects[index];
            // 鼠标移动到格子中心
            MoveMouseTo(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            int r = -1, g = -1, b = -1;
            // 获取边框颜色
            DateTime start = DateTime.Now;
            while ((DateTime.Now - start).TotalMilliseconds < 100)
            {
                var (rr, gg, bb) = ScreenShotMax(new Rect(rect.X - d3UI.mapY(9), rect.Y + rect.Height / 2, 3, 1));
                if (r == rr && g == gg && b == bb) break;
                await Task.Delay(20);
                if (rr < 22 && gg < 20 && bb < 15 && rr > bb && gg > bb) continue;
                r = rr;
                g = gg;
                b = bb;
            }

            // 装备品质
            // 0: 未定义
            // 1: 垃圾
            // 2: 远古
            // 3: 神圣
            // 4: 太古
            int quality = 0;
            if ((r >= 70 || b <= 20) // 红色多, 蓝色少 (偏暖)
                && Math.Max(Math.Abs(r - g), Math.Max(Math.Abs(g - b), Math.Abs(b - r))) > 20 // 有颜色倾向
                && (r + g + b < 410)) // 偏暗
            {
                quality = (g < 35) ? 4 : 2; // 以红色分量来判断是太古还是远古
            }
            else if (b > 100 && b > g && g > r) // 蓝色调冷光
            {
                quality = 3; // 神圣
            }
            else
            {
                quality = 1;
            }

            return quality;
        }

        /// <summary>
        /// 全屏截图
        /// </summary>
        private Bitmap ScreenShot()
        {
            Bitmap screen = new Bitmap(screenWidth, screenHeight);
            using (Graphics g = Graphics.FromImage(screen))
            {
                g.CopyFromScreen(0, 0, 0, 0, screen.Size);
            }
            return screen;
        }

        /// <summary>
        /// 获取 screen 上 xy 点的颜色
        /// </summary>
        private Color GetPixel(Bitmap screen, double[] xy)
        {
            using (Bitmap bmp = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen((int)Math.Round(xy[0]), (int)Math.Round(xy[1]), 0, 0, bmp.Size);
                    return bmp.GetPixel(0, 0);
            }
        }

        /// <summary>
        /// 判断是否存在确认框
        /// </summary>
        private bool IsDialogBoxOnScreen()
        {
            Bitmap screen = ScreenShot();
            Color c1 = GetPixel(screen, d3UI.dialogBoxPoints[0]);
            Color c2 = GetPixel(screen, d3UI.dialogBoxPoints[1]);

            Func<Color, bool> isDarkRed = c => (c.R > c.G && c.G > c.B
                && c.B < 5 && c.G < 15 && c.R > 25);
            bool ans = isDarkRed(c1) && isDarkRed(c2);
            screen.Dispose();
            return ans;
        }

        /// <summary>
        /// 等待确认框出现, 若出现则回车, 返回是否成功
        /// </summary>
        private async Task<bool> ClickDialogBox()
        {
            DateTime start = DateTime.Now;
            while ((DateTime.Now - start).TotalMilliseconds < 100)
            {
                if (IsDialogBoxOnScreen())
                {
                    sim.Keyboard.KeyPress(VirtualKeyCode.RETURN);
                    return true;
                }
                await Task.Delay(20);
            }
            return false;
        }

        /// <summary>
        /// 一键分解装备
        /// </summary>
        private async Task Decompose()
        {
            // 切换到分解页面
            MoveMouseTo(d3UI.smithPoints[4][0], d3UI.smithPoints[4][1]);
            sim.Mouse.LeftButtonClick();

            // 分解三色垃圾
            Bitmap screen = ScreenShot();
            Color cWhite = GetPixel(screen, d3UI.smithPoints[6]);
            Color cBlue = GetPixel(screen, d3UI.smithPoints[7]);
            Color cYellow = GetPixel(screen, d3UI.smithPoints[8]);
            bool wait = false;
            if (cWhite.R > 65)
            {
                MoveMouseTo(d3UI.smithPoints[10][0], d3UI.smithPoints[10][1]);
                sim.Mouse.LeftButtonClick();
                await ClickDialogBox();
                wait = true;
            }
            if (cBlue.B > 65)
            {
                MoveMouseTo(d3UI.smithPoints[11][0], d3UI.smithPoints[11][1]);
                sim.Mouse.LeftButtonClick();
                await ClickDialogBox();
                wait = true;
            }
            if (cYellow.R > 60)
            {
                MoveMouseTo(d3UI.smithPoints[12][0], d3UI.smithPoints[12][1]);
                sim.Mouse.LeftButtonClick();
                await ClickDialogBox();
                wait = true;
            }
            if (wait)
            {
                await Task.Delay(100);
                screen.Dispose();
                screen = ScreenShot();
            }

            bool[] hasItem = new bool[60];
            Color[] colors = new Color[60]; // 保存格子左下角信息
            double[][] scans = [[0.6, 0.7], [0.4, 0.4], [0.7, 0.25]];
            for (int index = 0; index < 60; index++)
            {
                colors[index] = screen.GetPixel(
                    (int)Math.Round(d3UI.backpackRects[index].X + 0.1 * d3UI.backpackRects[index].Width),
                    (int)Math.Round(d3UI.backpackRects[index].Y + 0.7 * d3UI.backpackRects[index].Height)
                    );
                if (safeZone.Contains(index)) continue; // 跳过安全格
                foreach (double[] scan in scans)
                {
                    Color c = screen.GetPixel(
                        (int)Math.Round(d3UI.backpackRects[index].X + scan[0] * d3UI.backpackRects[index].Width),
                        (int)Math.Round(d3UI.backpackRects[index].Y + scan[1] * d3UI.backpackRects[index].Height)
                        );
                    if (c.R < 22 && c.G < 20 && c.B < 15 && c.R > c.B && c.G > c.B) continue;
                    hasItem[index] = true;
                    break;
                }
            }

            // 分解背包中的装备
            MoveMouseTo(d3UI.smithPoints[5][0], d3UI.smithPoints[5][1]);
            sim.Mouse.LeftButtonClick();
            for (int index = 0; index < 60; index++)
            {
                if (!hasItem[index]) continue;
                int quality = await GetItemQuality(index);
                if (quality > 1) continue; // 品质足够
                // 分解
                sim.Mouse.LeftButtonClick();
                if (await ClickDialogBox() && index < 50 && hasItem[index + 10])
                {
                    // 先前格子左下角颜色
                    Color cPre = colors[index + 10];
                    // 重新对左下角取色
                    int r = -1, g = -1, b = -1;
                    DateTime start = DateTime.Now;
                    while ((DateTime.Now - start).TotalMilliseconds < 100)
                    {
                        screen.Dispose();
                        screen = ScreenShot();
                        Color c = screen.GetPixel(
                            (int)Math.Round(d3UI.backpackRects[index].X + 0.1 * d3UI.backpackRects[index].Width),
                            (int)Math.Round(d3UI.backpackRects[index].Y + 0.7 * d3UI.backpackRects[index].Height)
                            );
                        if (r == c.R && g == c.G && b == c.B) break;
                        r = c.R;
                        g = c.G;
                        b = c.B;
                        await Task.Delay(20);
                    }
                    // 下方格子发生变化, 意味着空了
                    if (r != cPre.R || g != cPre.G || b != cPre.B)
                    {
                        hasItem[index + 10] = false;
                    }
                }
            }

            sim.Mouse.RightButtonClick(); // 结束分解
            screen.Dispose();
            isRunning = false;
        }

        /// <summary>
        /// 判断铁匠页面状态 0: 未开启, 1: 开启但不是分解页面, 2: 分解页面
        /// </summary>
        private bool IsSmithPageOn()
        {
            Bitmap screen = ScreenShot();
            Color c1 = GetPixel(screen, d3UI.smithPoints[0]);
            Color c2 = GetPixel(screen, d3UI.smithPoints[1]);
            Color c3 = GetPixel(screen, d3UI.smithPoints[2]);
            Color c4 = GetPixel(screen, d3UI.smithPoints[3]);
            bool ans = c1.B > c1.G && c1.G > c1.R && c1.B > 170 && c1.B - c1.R > 80 // 亮蓝色
                && c2.R + c2.G > 350 // 白黄高亮
                && c3.B > c3.G && c3.G > c3.R && c3.B > 110 // 蓝偏亮
                && c4.R > 50 && c4.G < 15 && c4.B < 15; // 偏红
            screen.Dispose();
            return ans;
        }

        #endregion

        #region 主要行为

        /// <summary>
        /// 开启战斗模式
        /// </summary>
        private void BattleStart()
        {
            for (int index = 0; index < 6; index++)
            {
                switch (modes[index])
                {
                    case "无":
                        break;
                    case "按住不放":
                        Cast(index, 1);
                        break;
                    case "好了就按":
                        timers[index] = new Timer(callbacks[index], null, 0, 20);
                        break;
                    case "固定间隔":
                        timers[index] = new Timer(callbacks[index], null, 0, intervals[index]);
                        break;
                }
            }
        }

        /// <summary>
        /// 按下所有需要按住的技能
        /// </summary>
        private void BattlePress()
        {
            for (int index = 0; index < 6; index++)
            {
                if (modes[index] == "按住不放")
                {
                    Cast(index, 1);
                }
            }
        }

        /// <summary>
        /// 停止战斗模式
        /// </summary>
        private void BattleStop()
        {
            foreach (Timer timer in timers)
            {
                timer?.Dispose();
            }
            for (int index = 0; index < 6; index++)
            {
                if (modes[index] == "按住不放")
                {
                    Cast(index, 2); // 松开按键
                }
            }
        }

        /// <summary>
        /// 截取技能未冷却时的图标
        /// </summary>
        private void SetColdDownOK()
        {
            for (int index = 0; index < 6; index++)
            {
                if (modes[index] == "好了就按")
                {
                    coldDownOK[index]?.Dispose();
                    coldDownOK[index] = GetColdDownImage(index);
                }
            }
        }

        private async Task Gambling()
        {
            for (int i = 0; i < 20; i++)
            {
                sim.Mouse.RightButtonClick();
                await Task.Delay(20);
            }
        }



        #endregion


        // 监听系统热键消息
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(HwndHook);
        }
        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {

            // Windows API 常量
            const int WM_HOTKEY = 0x0312;

            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                // 录入快捷键时阻止触发快捷键, 并手动录入
                if (isRecordingHotkey)
                {
                    Console.WriteLine("手动录入快捷键");
                    int lParamInt = lParam.ToInt32();

                    // 高 16 位是虚拟键码
                    int vk = (lParamInt >> 16) & 0xFFFF;
                    Key key = (Key)vk;

                    // 低 16 位是修饰符
                    int mod = lParamInt & 0xFFFF;
                    ModifierKeys modifiers = ModifierKeys.None;

                    if ((mod & MOD_ALT) != 0) modifiers |= ModifierKeys.Alt;
                    if ((mod & MOD_CONTROL) != 0) modifiers |= ModifierKeys.Control;
                    if ((mod & MOD_SHIFT) != 0) modifiers |= ModifierKeys.Shift;

                    SetHotKey(key, modifiers);
                    return (IntPtr)1;
                }
                else
                {
                    if (checks["开启按键音"]) System.Media.SystemSounds.Beep.Play();
                    // 按一次开启战斗, 再按一次结束
                    if (wParam.ToInt32() == hotkeyID["战斗"])
                    {
                        if (isRunning) return (IntPtr)1;
                        isRunning = true;
                        Task.Delay(200).ContinueWith(_ => isRunning = false);
                        for (int index = 0; index < 6; index++)
                        {
                            if (modes[index] == "好了就按" && coldDownOK[index] == null)
                            {
                                MessageBox.Show(
                                    $"请先按 {((TextBox)FindName("冷却初始化")).Text} 来截取技能未冷却时的图标",
                                    "缺少技能未冷却图标",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information);
                                return (IntPtr)1;
                            }
                        }
                        battle = !battle;
                        if (battle)
                        {
                            Console.WriteLine("开始战斗");
                            BattleStart();
                        }
                        else
                        {
                            Console.WriteLine("结束战斗");
                            BattleStop();
                        }
                    }
                    else if (wParam.ToInt32() == hotkeyID["冷却初始化"])
                    {
                        Console.WriteLine("截取技能未冷却时的图标");
                        SetColdDownOK();
                    }
                    else if (wParam.ToInt32() == hotkeyID["仅按住技能"])
                    {
                        Console.WriteLine("按下所有需要按住的技能");
                        BattlePress();
                    }
                    else if (wParam.ToInt32() == hotkeyID["日常"])
                    {
                        if (IsSmithPageOn())
                        {
                            if (!checks["一键分解"]) return (IntPtr)1;
                            if (isRunning) return (IntPtr)1;
                            isRunning = true;
                            Console.WriteLine("开始一键分解");
                            _ = Decompose();
                        }
                        else if (checks["赌博"])
                        {
                            _ = Gambling();
                        }
                    }
                    else if (wParam.ToInt32() == hotkeyID["按左键"])
                    {
                        if (checks["键盘代替左键"])
                        {
                            sim.Mouse.LeftButtonClick();
                        }
                    }
                }
            }

            return (IntPtr)0;
        }

        /// <summary>
        /// 程序关闭时取消热键注册
        /// </summary>
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            foreach (int id in hotkeyID.Values)
            {
                UnregisterHotKey(hwnd, id);
            }
            SaveSettings();
        }
    }
}
