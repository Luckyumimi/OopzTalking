using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace OopzTalking;

// 屏幕覆盖位置管理：
//  - Reset()   把 oopz 游戏内屏幕覆盖重置回目标屏中央（修改配置文件并重启覆盖进程）
//  - Hide()    把覆盖窗口丢到屏幕外很远（隐藏）
//  - IsHidden() 覆盖窗口是否位于屏幕外隐藏位置
// 移植自 oopz-overlay-reset.exe 的核心逻辑，去掉 CLI 参数，直接以方法调用。
public static class OverlayReset {
    #region Win32

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DISPLAY_DEVICE {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODE {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight;
        public int dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType;
        public int dmReserved1, dmReserved2;
        public int dmPanningWidth, dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int L, T, R, B; }

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplayDevices(string? dev, uint i, ref DISPLAY_DEVICE dd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr h, out RECT r);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private const uint DISPLAY_DEVICE_ATTACHED = 0x1;
    private const uint DISPLAY_DEVICE_PRIMARY = 0x4;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    // 隐藏位置：丢到屏幕左侧外很远（不参与显示器枚举范围）
    private const int HiddenX = -32000;
    private const int HiddenY = -32000;

    #endregion

    private class Monitor {
        public string Name = "";
        public int W, H, X, Y;
        public bool Primary;
    }

    private class KV {
        public string Key = "";
        public string Raw = "";
    }

    private static readonly string[] OverlayProcessNames = { "oopz-overlay2" };

    /// <summary>把覆盖重置回目标屏中央。返回 null 表示成功，否则返回错误信息。</summary>
    public static string? Reset() {
        try {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] configs = FindConfigs();
            if (configs.Length == 0) {
                return "未找到 game_overlay 配置，请确认 oopz 已安装并登录。";
            }

            // 读当前配置
            string outer = File.ReadAllText(configs[0]);
            Match mb = Regex.Match(outer, "\"config_config\"\\s*:\\s*\"([^\"]+)\"");
            if (!mb.Success) {
                return "配置格式异常：" + configs[0];
            }

            string innerText = Encoding.UTF8.GetString(Convert.FromBase64String(mb.Groups[1].Value));
            List<KV> inner = ParseInner(innerText);
            KV? kScreen = GetK(inner, "screen_name");
            KV? kLeft = GetK(inner, "left");
            KV? kTop = GetK(inner, "top");
            if (kScreen == null || kLeft == null || kTop == null) {
                return "配置缺少必需字段（screen_name/left/top）。";
            }

            var screen = kScreen;
            var left = kLeft;
            var top = kTop;
            string cfgScreen = JsonUnescape(screen.Raw);
            int.TryParse(left.Raw, out int cfgLeft);
            int.TryParse(top.Raw, out int cfgTop);

            // 枚举显示器
            List<Monitor> mons = EnumerateMonitors();
            if (mons.Count == 0) {
                return "无法枚举显示器。";
            }

            // 选目标屏
            Monitor? target = mons.Find(m => m.Name == cfgScreen);
            if (target == null) {
                target = mons.Find(m => m.Primary) ?? mons[0];
            }

            var targetMon = target;
            if (targetMon == null) {
                return "无法确定目标显示器。";
            }

            // 量取当前覆盖窗口尺寸（用于精确居中）
            Process? overlay = FindOverlay();
            int newLeft, newTop;
            if (overlay != null) {
                RECT r;
                if (GetWindowRect(overlay.MainWindowHandle, out r)) {
                    int cw = Math.Min(r.R - r.L, targetMon.W);
                    int ch = Math.Min(r.B - r.T, targetMon.H);
                    newLeft = (int)Math.Round(targetMon.X + (targetMon.W - cw) / 2.0);
                    newTop = (int)Math.Round(targetMon.Y + (targetMon.H - ch) / 2.0);
                } else {
                    newLeft = 0;
                    newTop = 0;
                }
            } else {
                const int estW = 400, estH = 500;
                newLeft = (int)Math.Round(targetMon.X + (targetMon.W - estW) / 2.0);
                newTop = (int)Math.Round(targetMon.Y + Math.Max(0, (targetMon.H - estH) / 2.0));
            }

            // 组装新配置
            foreach (KV kv in inner) {
                if (kv.Key == "screen_name") {
                    kv.Raw = "\"" + JsonEscape(targetMon.Name) + "\"";
                } else if (kv.Key == "left") {
                    kv.Raw = newLeft.ToString();
                } else if (kv.Key == "top") {
                    kv.Raw = newTop.ToString();
                }
            }

            StringBuilder sb = new StringBuilder("{");
            for (int n = 0; n < inner.Count; n++) {
                if (n > 0) {
                    sb.Append(',');
                }

                sb.Append('"').Append(inner[n].Key).Append("\":").Append(inner[n].Raw);
            }

            sb.Append('}');
            string newInner = sb.ToString();
            List<KV> chk = ParseInner(newInner);
            var chkScreen = GetK(chk, "screen_name");
            if (chkScreen == null || JsonUnescape(chkScreen.Raw) != targetMon.Name) {
                return "转义校验失败。";
            }

            string newB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(newInner));
            string newOuter = outer.Replace(mb.Groups[1].Value, newB64);

            // 以居中新配置重启覆盖进程
            if (overlay != null) {
                foreach (Process o in Process.GetProcessesByName(OverlayProcessNames[0])) {
                    try { o.Kill(); } catch { }
                }

                System.Threading.Thread.Sleep(600);
                TryLaunchOverlay(newB64, localAppData);
                System.Threading.Thread.Sleep(2500);
            }

            // 写回所有配置（含沙箱副本），先备份
            foreach (string f in configs) {
                try { File.Copy(f, f + ".bak_reset", true); } catch { }

                var attrs = File.GetAttributes(f);
                bool wasReadOnly = (attrs & FileAttributes.ReadOnly) != 0;
                if (wasReadOnly) {
                    File.SetAttributes(f, attrs & ~FileAttributes.ReadOnly);
                }

                File.WriteAllText(f, newOuter, new UTF8Encoding(false));

                // 写后校验
                string vo = File.ReadAllText(f);
                Match vmb = Regex.Match(vo, "\"config_config\"\\s*:\\s*\"([^\"]+)\"");
                if (!vmb.Success) {
                    return "写后校验失败（读取）：" + f;
                }

                List<KV> vk = ParseInner(Encoding.UTF8.GetString(Convert.FromBase64String(vmb.Groups[1].Value)));
                var vkScreen = GetK(vk, "screen_name");
                var vkLeft = GetK(vk, "left");
                var vkTop = GetK(vk, "top");
                if (vkScreen == null
                    || vkLeft == null
                    || vkTop == null
                    || JsonUnescape(vkScreen.Raw) != targetMon.Name
                    || vkLeft.Raw != newLeft.ToString()
                    || vkTop.Raw != newTop.ToString()) {
                    return "写后校验失败（内容）：" + f;
                }
            }

            return null;
        } catch (Exception ex) {
            return "出错：" + ex.Message;
        }
    }

    /// <summary>把覆盖窗口丢到屏幕外隐藏。返回 null 表示成功，否则返回错误信息。</summary>
    public static string? Hide() {
        try {
            Process? overlay = FindOverlay();
            if (overlay == null) {
                return "未找到 oopz 覆盖窗口（oopz-overlay2.exe 未运行）。";
            }

            SetWindowPos(
                overlay.MainWindowHandle,
                IntPtr.Zero,
                HiddenX,
                HiddenY,
                0,
                0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE
            );
            return null;
        } catch (Exception ex) {
            return "出错：" + ex.Message;
        }
    }

    /// <summary>覆盖窗口当前是否位于隐藏位置。</summary>
    public static bool IsHidden() {
        Process? overlay = FindOverlay();
        if (overlay == null) {
            return false;
        }

        RECT r;
        if (!GetWindowRect(overlay.MainWindowHandle, out r)) {
            return false;
        }

        // 完全移出所有显示器可视范围即视为隐藏
        return Math.Abs(r.L) >= 30000 && Math.Abs(r.T) >= 30000;
    }

    private static string[] FindConfigs() {
        var results = new List<string>();
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (string root in new[] {
                     Path.Combine(appData, "oopz"),
                     Path.Combine(localAppData, "oopz", "sandbox"),
                 }) {
            if (!Directory.Exists(root)) {
                continue;
            }

            try {
                foreach (string f in Directory.GetFiles(root, "game_overlay", SearchOption.AllDirectories)) {
                    if (!Regex.IsMatch(Path.GetFileName(Path.GetDirectoryName(f)) ?? "", "^[0-9a-f]{32}$")) {
                        continue;
                    }

                    if (!results.Contains(f)) {
                        results.Add(f);
                    }
                }
            } catch {
                // 忽略无法访问的目录
            }
        }

        return results.ToArray();
    }

    private static List<Monitor> EnumerateMonitors() {
        var mons = new List<Monitor>();
        uint i = 0;
        while (true) {
            DISPLAY_DEVICE dd = new DISPLAY_DEVICE();
            dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            if (!EnumDisplayDevices(null, i, ref dd, 0)) {
                break;
            }

            if ((dd.StateFlags & DISPLAY_DEVICE_ATTACHED) != 0) {
                string devName = dd.DeviceName;
                if (!devName.StartsWith(@"\\.\", StringComparison.Ordinal)) {
                    devName = @"\\.\" + devName;
                }

                DEVMODE dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                if (EnumDisplaySettings(devName, -1, ref dm)) {
                    mons.Add(new Monitor {
                        Name = devName,
                        W = dm.dmPelsWidth,
                        H = dm.dmPelsHeight,
                        X = dm.dmPositionX,
                        Y = dm.dmPositionY,
                        Primary = (dd.StateFlags & DISPLAY_DEVICE_PRIMARY) != 0,
                    });
                }
            }

            i++;
        }

        return mons;
    }

    private static Process? FindOverlay() {
        foreach (Process p in Process.GetProcessesByName(OverlayProcessNames[0])) {
            if (p.MainWindowHandle != IntPtr.Zero) {
                return p;
            }
        }

        return null;
    }

    private static bool TryLaunchOverlay(string b64Cfg, string localAppData) {
        string overlayExe = Path.Combine(localAppData, "oopz", "overlay", "oopz-overlay2.exe");
        if (!File.Exists(overlayExe)) {
            return false;
        }

        try {
            ProcessStartInfo psi = new ProcessStartInfo(overlayExe);
            psi.UseShellExecute = false;
            psi.WorkingDirectory = Path.GetDirectoryName(overlayExe);
            psi.EnvironmentVariables["XX_OVERLAY_CONFIG"] = b64Cfg;
            psi.EnvironmentVariables["XX_DEV_GAME_OVERLAY_MUTE"] =
                Path.Combine(localAppData, "oopz", "overlay", "mute.png");
            psi.EnvironmentVariables["XX_DEV_GAME_OVERLAY_FONT"] =
                Path.Combine(localAppData, "oopz", "data", "flutter_assets", "assets", "fonts", "OPlusSans3-Regular.ttf");
            psi.EnvironmentVariables["XX_DEV_GAME_OVERLAY_URL"] = "ws://127.0.0.1:10274";
            psi.EnvironmentVariables["XX_OVERLAY_MEMBER_CMD"] =
                Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"cmd\":\"members\",\"voice\":false,\"members\":[]}"));
            Process.Start(psi);
            return true;
        } catch {
            return false;
        }
    }

    private static List<KV> ParseInner(string s) {
        var list = new List<KV>();
        Regex rx = new Regex(
            "\"([a-zA-Z_][a-zA-Z0-9_]*)\"\\s*:\\s*(\"(?:[^\"\\\\]|\\\\.)*\"|true|false|null|[-+]?[0-9]*\\.?[0-9]+(?:[eE][-+]?[0-9]+)?)");
        foreach (Match m in rx.Matches(s)) {
            list.Add(new KV { Key = m.Groups[1].Value, Raw = m.Groups[2].Value });
        }

        return list;
    }

    private static KV? GetK(List<KV> list, string key) {
        foreach (KV kv in list) {
            if (kv.Key == key) {
                return kv;
            }
        }

        return null;
    }

    private static string JsonEscape(string s) {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string JsonUnescape(string raw) {
        if (raw.Length < 2 || raw[0] != '"') {
            return raw;
        }

        string body = raw.Substring(1, raw.Length - 2);
        return body.Replace("\\\\", "\\").Replace("\\\"", "\"");
    }
}