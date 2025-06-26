using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;  // 获取窗口大小
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using static D3H.Classes.D3UI;

namespace D3H.Classes
{
    // 所有数据采自 2560 x 1600 屏幕
    internal class D3UI
    {
        public Rect d3 { get; private set; }
        public Rect[] skillRects { get; private set; } = Array.Empty<Rect>();




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
                    d3 = rect.ToRect();
                    Console.WriteLine($"游戏窗口位置: {d3.X} x {d3.Y}");
                    Console.WriteLine($"游戏窗口大小: {d3.Width} x {d3.Height}");
                }
            }
            else
            {
                Console.WriteLine("未找到游戏窗口");
            }
        }

        /// <summary>
        /// 按分辨率成比例转换坐标
        /// </summary>
        private double mapX(double x)
        {
            return x * d3.Width / 2560;
        }
        private double mapY(double y)
        {
            return y * d3.Height / 1600;
        }


      

        /// <summary>
        /// 生成游戏内重要坐标和矩形
        /// </summary>
        private void GeneratePositions()
        {
            double y = mapY(1500);
            double width = mapX(10);
            double height = mapX(5);
            Func<double, double> skillX = (x => d3.Width / 2 - mapY(1280 - x));
            skillRects =
            [
                new Rect(skillX(829), y, width, height),
                new Rect(skillX(928), y, width, height),
                new Rect(skillX(1026), y, width, height),
                new Rect(skillX(1125), y, width, height),
                new Rect(skillX(1228), y, width, height),
                new Rect(skillX(1325), y, width, height)
            ];

        }


        // 技能图标
        // 大小: 66 * 66
        // 左上角 x 坐标: [801, 900, 998, 1097, 1200, 1297]
        // 左上角 y 坐标: 1492
        // 判断冷却是否结束在 [0, 65]^2 中采 [28, 37] * [8, 12]
        //private static readonly Rect[] skillRectsOriginal =
        //{
        //    new Rect(829, 1500, 10, 5),
        //    new Rect(928, 1500, 10, 5),
        //    new Rect(1026, 1500, 10, 5),
        //    new Rect(1125, 1500, 10, 5),
        //    new Rect(1228, 1500, 10, 5),
        //    new Rect(1325, 1500, 10, 5)
        //};



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
