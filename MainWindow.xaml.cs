using D3H.Classes;
using System.Drawing; // Bitmap
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json; // JSON 序列化/反序列化
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using WindowsInput; // 鼠标键盘输入

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
            { "日常", 100 }
        };
        private Dictionary<string, HotkeyBinding> hotkeys = new();
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
        // 技能快捷键
        private (Key key, ModifierKeys mod)[] skillHotkeys = Enumerable.Repeat((Key.None, ModifierKeys.None), 4).ToArray();
        private Timer[] timers = new Timer[6];
        private string[] modes = ["无", "无", "无", "无", "无", "无"];
        // 放技能函数
        private TimerCallback[] callbacks = new TimerCallback[6];
        private int[] intervals = [0, 0, 0, 0, 0, 0]; // 释放间隔

        private Bitmap[] coldDownOK = new Bitmap[6]; // 冷却转好的图片
        private Bitmap[] latestScreenshots = new Bitmap[6]; // 截图缓冲区
        private static readonly InputSimulator sim = new InputSimulator(); // 模拟按键


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
        #endregion



        // 控制台
        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

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
                hotkeys[item.Key] = item.Value;
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
                ComboBox cb = (ComboBox)FindName(skillNames[index] + "模式");
                cb.SelectedIndex = modeIndex[mode];

                intervals[index] = _settings.intervals[index];
                TextBox tb = (TextBox)FindName(skillNames[index] + "间隔");
                tb.Text = intervals[index].ToString();
            }

        }

        /// <summary>
        /// 保存设置, 并存到设置文件
        /// </summary>
        private void SaveSettings()
        {
            // 保存系统快捷键
            foreach (var item in hotkeys)
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
            SaveSettingsFile(_settings);
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
                hotkeys[name] = new HotkeyBinding(key.ToString(), modifiers.ToString());
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
            var keyCode = (WindowsInput.Native.VirtualKeyCode)KeyInterop.VirtualKeyFromKey(key);

            if (type != 2)
            {
                // 按下修饰键
                if (modifiers.HasFlag(ModifierKeys.Control))
                    sim.Keyboard.KeyDown(WindowsInput.Native.VirtualKeyCode.CONTROL);

                if (modifiers.HasFlag(ModifierKeys.Alt))
                    sim.Keyboard.KeyDown(WindowsInput.Native.VirtualKeyCode.MENU); // Alt 键的代码是 MENU

                if (modifiers.HasFlag(ModifierKeys.Shift))
                    sim.Keyboard.KeyDown(WindowsInput.Native.VirtualKeyCode.SHIFT);

                // 按下主键
                sim.Keyboard.KeyDown(keyCode);
            }

            if (type != 1)
            {
                // 释放主键
                sim.Keyboard.KeyUp(keyCode);

                // 释放修饰键
                if (modifiers.HasFlag(ModifierKeys.Shift))
                    sim.Keyboard.KeyUp(WindowsInput.Native.VirtualKeyCode.SHIFT);

                if (modifiers.HasFlag(ModifierKeys.Alt))
                    sim.Keyboard.KeyUp(WindowsInput.Native.VirtualKeyCode.MENU);

                if (modifiers.HasFlag(ModifierKeys.Control))
                    sim.Keyboard.KeyUp(WindowsInput.Native.VirtualKeyCode.CONTROL);
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
                //await Task.Delay(1);
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
                        if (coldDownOK[index] == null)
                        {
                            battle = false;
                            BattleStop(true);
                            return;
                        }
                        timers[index] = new Timer(callbacks[index], null, 0, 20);
                        break;
                    case "固定间隔":
                        timers[index] = new Timer(callbacks[index], null, 0, intervals[index]);
                        break;
                }
            }
        }

        /// <summary>
        /// 停止战斗模式
        /// </summary>
        private void BattleStop(bool bad=false)
        {
            foreach (Timer timer in timers)
            {
                timer?.Dispose();
            }

            if (bad)
            {
                MessageBox.Show(
                $"请先按 {((TextBox)FindName("冷却初始化")).Text} 来截取技能未冷却时的图标",
                "缺少技能未冷却图标",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
                // 按一次开启战斗, 再按一次结束
                else if (wParam.ToInt32() == hotkeyID["战斗"])
                {
                    if (isRunning) return (IntPtr)1;
                    isRunning = true;
                    Task.Delay(200).ContinueWith(_ => isRunning = false);
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
