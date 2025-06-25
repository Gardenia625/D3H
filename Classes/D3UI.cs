using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;  // 获取窗口大小
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace D3H.Classes
{
    // 所有数据采自 2560 x 1600 屏幕
    internal class D3UI
    {
        public Rect d3Rect { get; private set; }
        public Rect[] skillRects { get; private set; } = [];




        public D3UI()
        {
            GetD3WidthAndHeight();  // 获取游戏窗口信息
            GeneratePositions();    // 生成各元素信息
        }

        /// <summary>
        /// 获取游戏窗口位置与大小
        /// </summary>
        private void GetD3WidthAndHeight()
        {
            nint hWnd = FindWindow(null, "暗黑破坏神III");
            if (hWnd != nint.Zero)
            {
                if (GetWindowRect(hWnd, out RECT rect))
                {
                    d3Rect = rect.ToRect();
                    Console.WriteLine($"游戏窗口位置: {d3Rect.X} x {d3Rect.Y}");
                    Console.WriteLine($"游戏窗口大小: {d3Rect.Width} x {d3Rect.Height}");
                }
            }
            else
            {
                Console.WriteLine("未找到游戏窗口");
            }
        }

        /// <summary>
        /// 游戏内坐标转换为屏幕坐标
        /// </summary>
        private (int, int) GameToScreenXY(double x, double y)
        {
            return ((int)Math.Round(x * d3Rect.Width / 2560), (int)Math.Round(y * d3Rect.Height / 1600));
        }
        /// <summary>
        /// 游戏内矩形转换为屏幕矩形
        /// </summary>
        private Rect GameToScreenRect(Rect rect)
        {
            var (x, y) = GameToScreenXY(rect.X, rect.Y);
            var (width, height) = GameToScreenXY(rect.Width, rect.Height);
            return new Rect(x, y, width, height);
        }

        /// <summary>
        /// 生成游戏内重要坐标和矩形
        /// </summary>
        private void GeneratePositions()
        {
            skillRects = skillRectsOriginal
                .Select(x => GameToScreenRect(x))
                .ToArray();

        }


        // 技能图标
        // 大小: 66 * 66
        // 左上角 x 坐标: [801, 900, 998, 1097, 1200, 1297]
        // 左上角 y 坐标: 1492
        // 判断冷却是否结束在 [0, 65]^2 中采 [28, 37] * [8, 12]
        private static readonly Rect[] skillRectsOriginal =
        {
            new Rect(829, 1500, 10, 5),
            new Rect(928, 1500, 10, 5),
            new Rect(1026, 1500, 10, 5),
            new Rect(1125, 1500, 10, 5),
            new Rect(1228, 1500, 10, 5),
            new Rect(1325, 1500, 10, 5)
        };



        #region Win32 API
        // 查找窗口
        [DllImport("user32.dll", SetLastError = true)]
        static extern nint FindWindow(string? lpClassName, string lpWindowName);

        // 获取窗口大小
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

        // 定义 RECT 存储窗口信息
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            // 转换为 Rect
            public Rect ToRect()
            {
                return new Rect(Left, Top, Right - Left, Bottom - Top);
            }
        }
        #endregion
    }
}
