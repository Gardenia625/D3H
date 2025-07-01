using System;
using System.Runtime.InteropServices;  // 获取窗口大小
using System.Windows;

namespace D3H.Classes
{
    // 所有数据采自 2560 x 1600 屏幕
    internal class D3UI
    {
        public Rect d3 { get; private set; }
        public Rect[] skillRects { get; private set; } = Array.Empty<Rect>();

        public Rect[] backpackRects { get; private set; } = Array.Empty<Rect>();

        public Double[][] dialogBoxPoints { get; private set; } = Array.Empty<Double[]>();

        public Double[][] smithPoints { get; private set; } = Array.Empty<Double[]>();

        public Double[][] gamblingPoints { get; private set; } = Array.Empty<Double[]>();

        public D3UI()
        {
            GetD3WidthAndHeight(); // 获取游戏窗口信息
            GetCDRects();          // 生成技能冷却 Rects
            GetBackpackRects();    // 生成背包格子 Rects
            GetDialogBoxPoints();  // 计算确认框上两个点
            GetSmithPoints();      // 计算铁匠铺中重要的点
            GetGamblingPoints();   // 计算血岩界面 logo 上的点
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
        /// 按分辨率成比例转换坐标 (仅作比例缩放)
        /// </summary>
        public double mapX(double x)
        {
            return x * d3.Width / 2560;
        }
        public double mapY(double y)
        {
            return y * d3.Height / 1600;
        }


        #region 技能图标

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

        /// <summary>
        /// 生成技能冷却截图 Rect
        /// </summary>s
        private void GetCDRects()
        {
            double y = mapY(1500);
            double width = mapX(10);
            double height = mapX(5);
            Func<double, double> skillX = (x => d3.Width / 2 - mapY(1280 - x));
            skillRects =
            [
                new Rect(d3.X + skillX(829), d3.Y + y, width, height),
                new Rect(d3.X + skillX(928), d3.Y + y, width, height),
                new Rect(d3.X + skillX(1026), d3.Y + y, width, height),
                new Rect(d3.X + skillX(1125), d3.Y + y, width, height),
                new Rect(d3.X + skillX(1228), d3.Y + y, width, height),
                new Rect(d3.X + skillX(1325), d3.Y + y, width, height)
            ];

        }

        #endregion 


        /// <summary>
        /// 获取背包 Rects
        /// </summary>
        private void GetBackpackRects()
        {
            int[] xs = [1794, 1869, 1944, 2018, 2093, 2168, 2242, 2317, 2392, 2466, 2541];
            int[] ys = [830, 904, 977, 1051, 1125, 1199, 1272];

            backpackRects = new Rect[60];
            for (int row = 0; row < 6; row++)
            {
                for (int col = 0; col < 10; col++)
                {
                    backpackRects[row * 10 + col] = new Rect(
                        d3.X + d3.Width - mapY(2560 - xs[col]),
                        d3.Y + mapY(ys[row]),
                        mapY(xs[col + 1] - xs[col]),
                        mapY(ys[row + 1] - ys[row])
                        );
                }
            }
        }

        /// <summary>
        /// 获取确认框上的两个点
        /// </summary>
        private void GetDialogBoxPoints()
        {
            dialogBoxPoints = new double[2][];
            dialogBoxPoints[0] = [d3.X + d3.Width / 2 - mapY(1280 - 1205), mapY(555)];
            dialogBoxPoints[1] = [d3.X + d3.Width / 2 - mapY(1280 - 1045), mapY(555)];
        }

        /// <summary>
        /// 铁匠铺的点
        /// </summary>
        private void GetSmithPoints()
        {
            smithPoints = new double[13][];
            // logo
            smithPoints[0] = [mapY(377), mapY(89)];
            smithPoints[1] = [mapY(390), mapY(119)];
            smithPoints[2] = [mapY(431), mapY(96)];
            smithPoints[3] = [mapY(748), mapY(1156)];
            // 分解子页按键
            smithPoints[4] = [mapY(765), mapY(720)];
            // 四个分解按钮边缘
            smithPoints[5] = [mapY(226), mapY(412)];
            smithPoints[6] = [mapY(372), mapY(412)];
            smithPoints[7] = [mapY(471), mapY(412)];
            smithPoints[8] = [mapY(571), mapY(412)];
            // 四个分解按钮中心
            smithPoints[9] = [mapY(245), mapY(431)];
            smithPoints[10] = [mapY(372), mapY(431)];
            smithPoints[11] = [mapY(471), mapY(431)];
            smithPoints[12] = [mapY(571), mapY(431)];
        }

        /// <summary>
        /// 血岩页面的点
        /// </summary>
        private void GetGamblingPoints()
        {
            gamblingPoints = new double[4][];
            gamblingPoints[0] = [mapY(356), mapY(107)];
            gamblingPoints[1] = [mapY(390), mapY(111)];
            gamblingPoints[2] = [mapY(216), mapY(75)];
            gamblingPoints[3] = [mapY(165), mapY(104)];
        }

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