using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace D3H.Classes
{
    public class HotkeyBinding
    {
        public string key { get; set; } = "None";
        public string mod { get; set; } = "None";

        public HotkeyBinding() { }
        public HotkeyBinding(string k, string m)
        {
            key = k;
            mod = m;
        }
    }
    public class AppSettings
    {
        public Dictionary<string, HotkeyBinding> hotkeys { get; set; } = new()
        {
            ["战斗"] = new HotkeyBinding("D1", "Control"),
            ["冷却初始化"] = new HotkeyBinding("F5", "None"),
            ["仅按住技能"] = new HotkeyBinding("D2", "Control"),
            ["日常"] = new HotkeyBinding("F10", "None"),
        };
        public HotkeyBinding[] skillHotkeys { get; set; } =
        {
            new HotkeyBinding("D1", "None"),
            new HotkeyBinding("D2", "None"),
            new HotkeyBinding("D3", "None"),
            new HotkeyBinding("D4", "None")
        };
        public string[] modes { get; set; } = ["无", "无", "无", "无", "无", "无"];
        public int[] intervals { get; set; } = [100, 100, 100, 100, 100, 100];

        public Dictionary<string, bool> checks { get; set; } = new()
        {
            { "开启按键音", false }
        };
    }
}
