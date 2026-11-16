// PadGestures — custom edge gestures for Windows Precision Touchpads (Raw Input) with a Material 3 style WPF UI.
// Свои жесты по краям тачпада Windows (Raw Input) + интерфейс WPF в стиле Material 3.
// Build / сборка: build.cmd
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using WF = System.Windows.Forms;
using SD = System.Drawing;
using UIA = System.Windows.Automation;

[assembly: AssemblyTitle("PadGestures")]
[assembly: AssemblyProduct("PadGestures")]
[assembly: AssemblyDescription("Custom edge gestures for Windows Precision Touchpads")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace PadGestures
{
    // ======================= Язык / Language =======================
    static class L
    {
        public static bool Ru = true;
        public static string T(string ru, string en) { return Ru ? ru : en; }

        public static void Set(string lang)
        {
            if (lang == "ru") Ru = true;
            else if (lang == "en") Ru = false;
            else Ru = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";
        }
    }

    // ======================= Настройки / Settings =======================
    class ZoneCfg
    {
        public string Name, Glyph;
        public bool Horizontal;
        public int Action;        // 0 off, 1 seek, 2 volume, 3 tracks, 4 scroll, 5 brightness
        public int SizePct;       // edge zone width, % of touchpad
        public double StepPct;    // finger travel per step, % of touchpad
        public int SeekSec;       // seconds per seek step
        public int VolStep = 2;   // volume % per step
        public int BrightStep = 5; // brightness % per step
        public bool Invert;

        public string Title
        {
            get
            {
                switch (Name)
                {
                    case "top": return L.T("Верхний край", "Top edge");
                    case "bottom": return L.T("Нижний край", "Bottom edge");
                    case "left": return L.T("Левый край", "Left edge");
                    default: return L.T("Правый край", "Right edge");
                }
            }
        }
    }

    class Config
    {
        public ZoneCfg[] Zones;
        public bool Enabled = true;
        public bool FreezeCursor = true;
        public bool Accel = true;
        public string Lang = "auto";      // auto | ru | en

        public static string FilePath { get { return System.IO.Path.Combine(Program.DataDir, "settings.ini"); } }

        public Config()
        {
            Zones = new ZoneCfg[] {
                new ZoneCfg { Name = "top",    Glyph = "", Horizontal = true,  Action = 1, SizePct = 15, StepPct = 7, SeekSec = 15 },
                new ZoneCfg { Name = "bottom", Glyph = "", Horizontal = true,  Action = 0, SizePct = 15, StepPct = 7, SeekSec = 30 },
                new ZoneCfg { Name = "left",   Glyph = "", Horizontal = false, Action = 2, SizePct = 10, StepPct = 7, SeekSec = 15 },
                new ZoneCfg { Name = "right",  Glyph = "", Horizontal = false, Action = 5, SizePct = 10, StepPct = 7, SeekSec = 15 },
            };
        }

        public void Load()
        {
            string path = FilePath;
            // перенос старых настроек, лежавших рядом с exe / migrate old settings next to the exe
            string old = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PadGestures.ini");
            if (!File.Exists(path) && File.Exists(old)) path = old;
            if (!File.Exists(path)) return;
            try
            {
                var m = new Dictionary<string, string>();
                foreach (var line in File.ReadAllLines(path))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) m[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
                Enabled = I(m, "enabled", 1) == 1;
                FreezeCursor = I(m, "freezeCursor", 1) == 1;
                Accel = I(m, "accel", 1) == 1;
                string lang; if (m.TryGetValue("lang", out lang)) Lang = lang;
                foreach (var z in Zones)
                {
                    z.Action = I(m, z.Name + ".action", z.Action);
                    z.SizePct = I(m, z.Name + ".size", z.SizePct);
                    z.SeekSec = I(m, z.Name + ".seek", z.SeekSec);
                    z.VolStep = I(m, z.Name + ".vol", z.VolStep);
                    z.BrightStep = I(m, z.Name + ".bright", z.BrightStep);
                    z.Invert = I(m, z.Name + ".invert", 0) == 1;
                    z.StepPct = D(m, z.Name + ".step", z.StepPct);
                }
            }
            catch { }
        }

        static int I(Dictionary<string, string> m, string k, int d) { string s; int v; return m.TryGetValue(k, out s) && int.TryParse(s, out v) ? v : d; }
        static double D(Dictionary<string, string> m, string k, double d) { string s; double v; return m.TryGetValue(k, out s) && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out v) ? v : d; }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Program.DataDir);
                var sb = new StringBuilder();
                sb.AppendLine("version=3");
                sb.AppendLine("enabled=" + (Enabled ? 1 : 0));
                sb.AppendLine("freezeCursor=" + (FreezeCursor ? 1 : 0));
                sb.AppendLine("accel=" + (Accel ? 1 : 0));
                sb.AppendLine("lang=" + Lang);
                foreach (var z in Zones)
                {
                    sb.AppendLine(z.Name + ".action=" + z.Action);
                    sb.AppendLine(z.Name + ".size=" + z.SizePct);
                    sb.AppendLine(z.Name + ".step=" + z.StepPct.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine(z.Name + ".seek=" + z.SeekSec);
                    sb.AppendLine(z.Name + ".vol=" + z.VolStep);
                    sb.AppendLine(z.Name + ".bright=" + z.BrightStep);
                    sb.AppendLine(z.Name + ".invert=" + (z.Invert ? 1 : 0));
                }
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch { }
        }
    }

    // ======================= WinAPI =======================
    static class Native
    {
        public const int WM_INPUT = 0x00FF;
        public const uint RID_INPUT = 0x10000003;
        public const uint RIDI_PREPARSEDDATA = 0x20000005;
        public const uint RIDI_DEVICENAME = 0x20000007;
        public const uint RIDEV_INPUTSINK = 0x00000100;
        public const uint RIM_TYPEHID = 2;
        public const int HIDP_STATUS_SUCCESS = 0x00110000;

        [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTDEVICE { public ushort usUsagePage; public ushort usUsage; public uint dwFlags; public IntPtr hwndTarget; }
        [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTHEADER { public uint dwType; public uint dwSize; public IntPtr hDevice; public IntPtr wParam; }
        [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTDEVICELIST { public IntPtr hDevice; public uint dwType; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);
        [DllImport("user32.dll")] public static extern uint GetRawInputData(IntPtr hRawInput, uint cmd, IntPtr data, ref uint size, uint headerSize);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetRawInputDeviceInfoW")] public static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint cmd, IntPtr data, ref uint size);
        [DllImport("user32.dll")] public static extern uint GetRawInputDeviceList(IntPtr list, ref uint count, uint size);
        [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll", EntryPoint = "ClipCursor")] public static extern bool ClipCursorRect(ref RECT r);
        [DllImport("user32.dll", EntryPoint = "ClipCursor")] public static extern bool ClipCursorNone(IntPtr zero);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr pre, out HIDP_CAPS caps);
        [DllImport("hid.dll")] public static extern int HidP_GetValueCaps(int reportType, IntPtr caps, ref ushort len, IntPtr pre);
        [DllImport("hid.dll")] public static extern int HidP_GetUsageValue(int reportType, ushort page, ushort link, ushort usage, out uint value, IntPtr pre, byte[] report, uint len);
        [DllImport("hid.dll")] public static extern int HidP_GetUsages(int reportType, ushort page, ushort link, [Out] ushort[] list, ref uint len, IntPtr pre, byte[] report, uint len2);
    }

    // ======================= Тачпад / Touchpad =======================
    class PadDevice
    {
        public IntPtr Handle;
        public string Name = "";
        public IntPtr Pre;
        public bool IsTouchpad;
        public List<ushort> Links = new List<ushort>();
        public int XMin, XMax = 1, YMin, YMax = 1;

        public static PadDevice Create(IntPtr hDevice)
        {
            var d = new PadDevice { Handle = hDevice };
            uint size = 0;
            Native.GetRawInputDeviceInfo(hDevice, Native.RIDI_DEVICENAME, IntPtr.Zero, ref size);
            if (size > 0)
            {
                IntPtr nb = Marshal.AllocHGlobal((int)size * 2 + 2);
                if (Native.GetRawInputDeviceInfo(hDevice, Native.RIDI_DEVICENAME, nb, ref size) != unchecked((uint)-1))
                    d.Name = Marshal.PtrToStringUni(nb);
                Marshal.FreeHGlobal(nb);
            }
            size = 0;
            Native.GetRawInputDeviceInfo(hDevice, Native.RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
            if (size == 0) return d;
            d.Pre = Marshal.AllocHGlobal((int)size);
            if (Native.GetRawInputDeviceInfo(hDevice, Native.RIDI_PREPARSEDDATA, d.Pre, ref size) == unchecked((uint)-1)) return d;

            Native.HIDP_CAPS caps;
            if (Native.HidP_GetCaps(d.Pre, out caps) != Native.HIDP_STATUS_SUCCESS) return d;
            if (caps.UsagePage != 0x0D || caps.Usage != 0x05) return d;   // digitizer / touch pad
            ushort n = caps.NumberInputValueCaps;
            if (n == 0) return d;
            const int VC = 72;   // sizeof(HIDP_VALUE_CAPS)
            IntPtr vb = Marshal.AllocHGlobal(VC * n);
            try
            {
                if (Native.HidP_GetValueCaps(0, vb, ref n, d.Pre) != Native.HIDP_STATUS_SUCCESS) return d;
                bool xs = false, ys = false;
                for (int i = 0; i < n; i++)
                {
                    IntPtr p = vb + i * VC;
                    ushort page = (ushort)Marshal.ReadInt16(p, 0);
                    ushort link = (ushort)Marshal.ReadInt16(p, 6);
                    int lmin = Marshal.ReadInt32(p, 40), lmax = Marshal.ReadInt32(p, 44);
                    ushort usage = (ushort)Marshal.ReadInt16(p, 56);
                    if (page != 0x01) continue;
                    if (usage == 0x30) { if (!d.Links.Contains(link)) d.Links.Add(link); if (!xs) { d.XMin = lmin; d.XMax = lmax; xs = true; } }
                    else if (usage == 0x31 && !ys) { d.YMin = lmin; d.YMax = lmax; ys = true; }
                }
                d.IsTouchpad = d.Links.Count > 0 && d.XMax > d.XMin && d.YMax > d.YMin;
            }
            finally { Marshal.FreeHGlobal(vb); }
            return d;
        }

        public static List<PadDevice> Enumerate()
        {
            var res = new List<PadDevice>();
            uint count = 0, sz = (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICELIST));
            Native.GetRawInputDeviceList(IntPtr.Zero, ref count, sz);
            if (count == 0) return res;
            IntPtr buf = Marshal.AllocHGlobal((int)(sz * count));
            try
            {
                uint got = Native.GetRawInputDeviceList(buf, ref count, sz);
                if (got == unchecked((uint)-1)) return res;
                for (int i = 0; i < got; i++)
                {
                    var e = (Native.RAWINPUTDEVICELIST)Marshal.PtrToStructure(buf + i * (int)sz, typeof(Native.RAWINPUTDEVICELIST));
                    if (e.dwType != Native.RIM_TYPEHID) continue;
                    var d = PadDevice.Create(e.hDevice);
                    if (d.IsTouchpad) res.Add(d);
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return res;
        }
    }

    // ======================= Перемотка через ползунок плеера / Seek via the player's slider =======================
    // Браузеры отдают ползунок видео (YouTube, Google Диск…) через UI Automation — ставим его сразу на нужное время.
    // Плеер пока перематывает — игнорирует новые команды и обновляет своё значение с задержкой,
    // поэтому цель копим сами, отправляем не чаще раза в 200 мс и после свайпа проверяем результат.
    class SliderSeeker
    {
        readonly object lk = new object();
        readonly AutoResetEvent ev = new AutoResetEvent(false);
        double pendingSec; IntPtr pendingHwnd; bool prepare;
        UIA.AutomationElement cached; IntPtr cachedHwnd; double unit = 1;
        IntPtr missHwnd; DateTime missUntil;
        double target = double.NaN; DateTime targetAt;
        bool dirty; DateTime lastSent, verifyAt = DateTime.MaxValue; int retries;

        public Action<double> Fallback;   // ползунка нет — клавишами / no slider — use keys
        public Action<string> Info;       // «57:43 / 3:21:29»

        public SliderSeeker() { new Thread(Loop) { IsBackground = true }.Start(); }   // MTA thread

        public void Prepare(IntPtr hwnd) { lock (lk) { pendingHwnd = hwnd; prepare = true; } ev.Set(); }
        public void Add(double sec, IntPtr hwnd) { lock (lk) { pendingSec += sec; pendingHwnd = hwnd; } ev.Set(); }

        void Loop()
        {
            while (true)
            {
                int wait = -1;
                var n0 = DateTime.UtcNow;
                if (dirty) wait = Math.Max(0, 200 - (int)(n0 - lastSent).TotalMilliseconds);
                else if (verifyAt != DateTime.MaxValue) wait = Math.Max(0, (int)(verifyAt - n0).TotalMilliseconds);
                ev.WaitOne(wait);

                double sec; IntPtr hwnd; bool prep;
                lock (lk) { sec = pendingSec; pendingSec = 0; hwnd = pendingHwnd; prep = prepare; prepare = false; }
                try
                {
                    var now = DateTime.UtcNow;
                    if (prep && (double.IsNaN(target) || (now - targetAt).TotalSeconds > 3)) { cached = null; GetSlider(hwnd, true); }
                    if (sec != 0)
                    {
                        var el = GetSlider(hwnd, false);
                        if (el == null) { var f = Fallback; if (f != null) f(sec); continue; }
                        var rv = Range(el);
                        double max = rv.Current.Maximum;
                        double cur = (!double.IsNaN(target) && (now - targetAt).TotalSeconds < 3) ? target : rv.Current.Value;
                        target = Math.Max(0, Math.Min(max - unit, cur + sec * unit));
                        targetAt = now; dirty = true; retries = 0;
                        var inf = Info; if (inf != null) inf(Fmt(target / unit) + " / " + Fmt(max / unit));
                    }
                    now = DateTime.UtcNow;
                    if (dirty && (now - lastSent).TotalMilliseconds >= 200 && cached != null)
                    {
                        Range(cached).SetValue(target);
                        lastSent = now; dirty = false; verifyAt = now.AddMilliseconds(900);
                        Program.Log("SET " + target);
                    }
                    else if (!dirty && verifyAt != DateTime.MaxValue && now >= verifyAt && cached != null)
                    {
                        double v = Range(cached).Current.Value;
                        bool ok = Math.Abs(v - target) < 4 * unit;
                        if (!ok && retries < 3) { retries++; Range(cached).SetValue(target); lastSent = now; verifyAt = now.AddMilliseconds(900); }
                        else verifyAt = DateTime.MaxValue;
                    }
                }
                catch (Exception ex)
                {
                    Program.Log("SLIDER error " + ex.Message);
                    cached = null; dirty = false; verifyAt = DateTime.MaxValue;
                    if (sec != 0) { var f = Fallback; if (f != null) f(sec); }
                }
            }
        }

        static UIA.RangeValuePattern Range(UIA.AutomationElement el)
        {
            return (UIA.RangeValuePattern)el.GetCurrentPattern(UIA.RangeValuePattern.Pattern);
        }

        UIA.AutomationElement GetSlider(IntPtr hwnd, bool force)
        {
            if (!force && cached != null && cachedHwnd == hwnd)
            {
                try { var n = cached.Current.Name; return cached; } catch { cached = null; }
            }
            if (!force && hwnd == missHwnd && DateTime.UtcNow < missUntil) return null;
            var found = Find(hwnd);
            if (found == null && force) { Thread.Sleep(250); found = Find(hwnd); }   // Chromium builds its a11y tree on first request
            if (found == null) { missHwnd = hwnd; missUntil = DateTime.UtcNow.AddSeconds(3); return null; }
            cached = found; cachedHwnd = hwnd; missHwnd = IntPtr.Zero;
            return found;
        }

        UIA.AutomationElement Find(IntPtr hwnd)
        {
            var root = UIA.AutomationElement.FromHandle(hwnd);
            var all = root.FindAll(UIA.TreeScope.Descendants,
                new UIA.PropertyCondition(UIA.AutomationElement.ControlTypeProperty, UIA.ControlType.Slider));
            UIA.AutomationElement best = null; int bestScore = -1;
            foreach (UIA.AutomationElement e in all)
            {
                object pat;
                if (!e.TryGetCurrentPattern(UIA.RangeValuePattern.Pattern, out pat)) continue;
                var rv = (UIA.RangeValuePattern)pat;
                if (rv.Current.IsReadOnly || rv.Current.Maximum <= 1) continue;
                string n = (e.Current.Name ?? "").ToLowerInvariant();
                if (n.Contains("громк") || n.Contains("volume") || n.Contains("звук")) continue;
                int score = 0;
                if (System.Text.RegularExpressions.Regex.IsMatch(n, "воспроизвед|перемот|прогресс|seek|progress|playback|time|время|видео|video")) score += 5;
                if (!e.Current.IsOffscreen) score += 2;
                if (rv.Current.Maximum > 30) score += 1;
                if (score > bestScore) { bestScore = score; best = e; }
            }
            if (best != null) unit = DetectUnit(best);
            return best;
        }

        // значение в секундах или миллисекундах? / slider value in seconds or milliseconds?
        static double DetectUnit(UIA.AutomationElement el)
        {
            double max = 0; string text = "";
            try { max = Range(el).Current.Maximum; } catch { }
            try { text = ((UIA.ValuePattern)el.GetCurrentPattern(UIA.ValuePattern.Pattern)).Current.Value; } catch { }
            double d = ParseDuration(text);
            if (d > 0) return max / d > 100 ? 1000 : 1;
            return max > 200000 ? 1000 : 1;
        }

        static double ParseDuration(string t)
        {
            if (string.IsNullOrEmpty(t)) return -1;
            t = t.ToLowerInvariant();
            int i = Math.Max(Math.Max(t.LastIndexOf(" из "), t.LastIndexOf(" of ")), t.LastIndexOf("/"));
            string part = i >= 0 ? t.Substring(i) : t;
            var m = System.Text.RegularExpressions.Regex.Match(part, @"(\d+):(\d{2})(?::(\d{2}))?");
            if (m.Success)
            {
                double a = double.Parse(m.Groups[1].Value), b = double.Parse(m.Groups[2].Value);
                return m.Groups[3].Success ? a * 3600 + b * 60 + double.Parse(m.Groups[3].Value) : a * 60 + b;
            }
            double s = 0; bool any = false;
            foreach (System.Text.RegularExpressions.Match w in System.Text.RegularExpressions.Regex.Matches(part, @"(\d+)\s*([^\d\s,]+)"))
            {
                double n = double.Parse(w.Groups[1].Value); string u = w.Groups[2].Value;
                if (u.StartsWith("ч") || u.StartsWith("h")) { s += n * 3600; any = true; }
                else if (u.StartsWith("мин") || u.StartsWith("min")) { s += n * 60; any = true; }
                else if (u.StartsWith("с") || u.StartsWith("sec")) { s += n; any = true; }
            }
            return any ? s : -1;
        }

        static string Fmt(double sec)
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, sec));
            return ts.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)ts.TotalHours, ts.Minutes, ts.Seconds) : string.Format("{0}:{1:00}", ts.Minutes, ts.Seconds);
        }
    }

    // ======================= Громкость / Volume (Core Audio) =======================
    // Клавиши громкости меняют её только по 2 % — для шага в 1 % ставим уровень напрямую через IAudioEndpointVolume.
    static class Volume
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int mask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out int count);
            [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid ctx);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
            [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint ch, float levelDb, ref Guid ctx);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
            [PreserveSig] int GetChannelVolumeLevel(uint ch, out float levelDb);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        static IAudioEndpointVolume Endpoint()
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(0 /*render*/, 1 /*multimedia*/, out dev) != 0 || dev == null) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            object o;
            if (dev.Activate(ref iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out o) != 0) return null;
            return (IAudioEndpointVolume)o;
        }

        // меняет громкость на delta процентов, возвращает новое значение (-1 — не получилось)
        public static int Change(int delta)
        {
            try
            {
                var ep = Endpoint();
                if (ep == null) return -1;
                float cur; ep.GetMasterVolumeLevelScalar(out cur);
                int pct = (int)Math.Round(cur * 100) + delta;
                pct = Math.Max(0, Math.Min(100, pct));
                var ctx = Guid.Empty;
                ep.SetMasterVolumeLevelScalar(pct / 100f, ref ctx);
                bool muted; ep.GetMute(out muted);
                if (muted && delta > 0) ep.SetMute(false, ref ctx);
                return pct;
            }
            catch (Exception ex) { Program.Log("VOLUME error " + ex.Message); return -1; }
        }
    }

    // ======================= Яркость / Brightness (WMI) =======================
    // WMI медленный: копим шаги и применяем не чаще раза в 80 мс. После свайпа проверяем значение:
    // ASUS ScreenXpert синхронизирует яркость экранов и может с задержкой вернуть старое значение — тогда ставим заново.
    class BrightnessControl
    {
        readonly object lk = new object();
        readonly AutoResetEvent ev = new AutoResetEvent(false);
        int pendingDelta; int target = -1, applied = -1; DateTime targetAt, lastSet, verifyAt = DateTime.MaxValue; int verifies;
        public Action<int> Info;

        public BrightnessControl() { new Thread(Loop) { IsBackground = true }.Start(); }

        public void Add(int delta) { lock (lk) pendingDelta += delta; ev.Set(); }

        void Loop()
        {
            while (true)
            {
                var n0 = DateTime.UtcNow;
                int wait = -1;
                if (target >= 0 && target != applied) wait = Math.Max(0, 80 - (int)(n0 - lastSet).TotalMilliseconds);
                else if (verifyAt != DateTime.MaxValue) wait = Math.Max(0, (int)(verifyAt - n0).TotalMilliseconds);
                ev.WaitOne(wait);
                int d; lock (lk) { d = pendingDelta; pendingDelta = 0; }
                try
                {
                    var now = DateTime.UtcNow;
                    if (d != 0)
                    {
                        // пока идёт жест — считаем от своей цели, а не от (запаздывающего) значения системы
                        int cur = (target >= 0 && (now - targetAt).TotalSeconds < 3) ? target : Read();
                        if (cur < 0) continue;
                        target = Math.Max(0, Math.Min(100, cur + d)); targetAt = now; verifies = 0;
                        var inf = Info; if (inf != null) inf(target);
                    }
                    if (target >= 0 && target != applied && (now - lastSet).TotalMilliseconds >= 80)
                    {
                        Apply(target);
                        applied = target; lastSet = DateTime.UtcNow; verifyAt = lastSet.AddMilliseconds(500);
                    }
                    else if (target == applied && verifyAt != DateTime.MaxValue && now >= verifyAt)
                    {
                        int real = Read();
                        if (real >= 0 && real != target && verifies < 4)
                        {
                            Program.Log("BRIGHT bounced " + real + " -> " + target);
                            verifies++; Apply(target); lastSet = DateTime.UtcNow; verifyAt = lastSet.AddMilliseconds(600);
                        }
                        else verifyAt = DateTime.MaxValue;
                    }
                }
                catch (Exception ex) { Program.Log("BRIGHT error " + ex.Message); applied = target; verifyAt = DateTime.MaxValue; }
            }
        }

        static void Apply(int t)
        {
            using (var cls = new System.Management.ManagementClass(@"root\WMI", "WmiMonitorBrightnessMethods", null))
                foreach (System.Management.ManagementObject o in cls.GetInstances())
                    using (o) o.InvokeMethod("WmiSetBrightness", new object[] { (uint)0, (byte)t });
        }

        static int Read()
        {
            using (var s = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
                foreach (System.Management.ManagementObject o in s.Get())
                    using (o) return Convert.ToInt32(o["CurrentBrightness"]);
            return -1;
        }
    }

    // ======================= Распознавание жестов / Gesture engine =======================
    class TouchState
    {
        public int Mode;          // 0 idle, 1 gesture, 2 ignore until lift
        public ZoneCfg Zone;
        public uint Id;
        public double SX, SY, LX, LY, Acc;
        public bool Locked;
        public DateTime Last, LastMove;
        public int FrameCount;
    }

    class GestureEngine
    {
        readonly Config cfg;
        readonly Dictionary<IntPtr, PadDevice> devices = new Dictionary<IntPtr, PadDevice>();
        readonly Dictionary<IntPtr, TouchState> states = new Dictionary<IntPtr, TouchState>();
        readonly ushort[] usageBuf = new ushort[64];
        readonly uint myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        bool clipped;
        DateTime lastReport;

        public double PadAspect = 1.64;
        public List<Point> Fingers = new List<Point>();
        public bool Touching;
        public ZoneCfg ActiveZone;
        public bool Dirty;
        public event Action<string> Fired;
        public readonly SliderSeeker Seeker = new SliderSeeker();
        public readonly BrightnessControl Bright = new BrightnessControl();

        const int SecPerPress = 5;   // запасная перемотка стрелками (YouTube и большинство плееров: 5 с)
        const int KeyGapMs = 60;

        public GestureEngine(Config c)
        {
            cfg = c;
            Seeker.Fallback = sec => KeySeek(sec);
            new Thread(() => { foreach (var a in keyQueue.GetConsumingEnumerable()) a(); }) { IsBackground = true }.Start();
        }

        void KeySeek(double sec)
        {
            int dir = sec > 0 ? 1 : -1;
            int presses = Math.Min(12, Math.Max(1, (int)Math.Round(Math.Abs(sec) / SecPerPress)));
            if (pending > 0) return;   // не копим очередь — без «дрифта» / no backlog, no drift
            for (int i = 0; i < presses; i++) Key(dir > 0 ? (byte)0x27 : (byte)0x25, true);
        }

        public void OnRawInput(IntPtr lParam)
        {
            uint size = 0, hdr = (uint)Marshal.SizeOf(typeof(Native.RAWINPUTHEADER));
            Native.GetRawInputData(lParam, Native.RID_INPUT, IntPtr.Zero, ref size, hdr);
            if (size == 0) return;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (Native.GetRawInputData(lParam, Native.RID_INPUT, buf, ref size, hdr) != size) return;
                var h = (Native.RAWINPUTHEADER)Marshal.PtrToStructure(buf, typeof(Native.RAWINPUTHEADER));
                if (h.dwType != Native.RIM_TYPEHID) return;
                int sizeHid = Marshal.ReadInt32(buf, (int)hdr), count = Marshal.ReadInt32(buf, (int)hdr + 4);
                PadDevice dev;
                if (!devices.TryGetValue(h.hDevice, out dev)) { dev = PadDevice.Create(h.hDevice); devices[h.hDevice] = dev; }
                if (!dev.IsTouchpad) return;
                for (int i = 0; i < count; i++)
                {
                    var report = new byte[sizeHid];
                    Marshal.Copy(buf + (int)hdr + 8 + i * sizeHid, report, 0, sizeHid);
                    Process(dev, report);
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        void Process(PadDevice d, byte[] r)
        {
            TouchState st;
            if (!states.TryGetValue(d.Handle, out st)) { st = new TouchState(); states[d.Handle] = st; }
            uint len = (uint)r.Length;
            var now = DateTime.UtcNow;
            lastReport = now;
            if ((now - st.Last).TotalMilliseconds > 250) { End(st); st.FrameCount = 0; }
            st.Last = now;

            uint cc;
            if (Native.HidP_GetUsageValue(0, 0x0D, 0, 0x54, out cc, d.Pre, r, len) == Native.HIDP_STATUS_SUCCESS && cc > 0) st.FrameCount = (int)cc;

            double xr = d.XMax - d.XMin, yr = d.YMax - d.YMin;
            PadAspect = xr / yr;

            bool anyTip = false, found = false;
            double fx = 0, fy = 0, tx = 0, ty = 0; uint fid = 0;
            var pts = new List<Point>();
            foreach (ushort link in d.Links)
            {
                uint x, y, id;
                if (Native.HidP_GetUsageValue(0, 0x01, link, 0x30, out x, d.Pre, r, len) != Native.HIDP_STATUS_SUCCESS) continue;
                if (Native.HidP_GetUsageValue(0, 0x01, link, 0x31, out y, d.Pre, r, len) != Native.HIDP_STATUS_SUCCESS) continue;
                if (Native.HidP_GetUsageValue(0, 0x0D, link, 0x51, out id, d.Pre, r, len) != Native.HIDP_STATUS_SUCCESS) id = link;
                uint ul = (uint)usageBuf.Length; bool tip = false;
                if (Native.HidP_GetUsages(0, 0x0D, link, usageBuf, ref ul, d.Pre, r, len) == Native.HIDP_STATUS_SUCCESS)
                    for (int k = 0; k < ul; k++) if (usageBuf[k] == 0x42) tip = true;
                if (!tip) continue;
                double nx = Clamp((x - d.XMin) / xr), ny = Clamp((y - d.YMin) / yr);
                pts.Add(new Point(nx, ny));
                if (!anyTip) { fx = nx; fy = ny; fid = id; anyTip = true; }
                if (st.Mode == 1 && id == st.Id) { tx = nx; ty = ny; found = true; }
            }

            Dirty = true;
            if (!anyTip) { End(st); Touching = false; return; }
            Touching = true; Fingers = pts;

            if (st.FrameCount >= 2) { if (st.Mode == 1) End(st); st.Mode = 2; return; }   // several fingers — not ours

            if (st.Mode == 0)
            {
                var z = cfg.Enabled ? FindZone(fx, fy) : null;
                if (z == null) { st.Mode = 2; return; }
                st.Mode = 1; st.Zone = z; st.Id = fid; st.SX = st.LX = fx; st.SY = st.LY = fy; st.Acc = 0; st.Locked = false; st.LastMove = now;
                ActiveZone = z;
                if (cfg.FreezeCursor) Freeze();
                if (z.Action == 1 && !OwnWindowActive()) Seeker.Prepare(Native.GetForegroundWindow());
                return;
            }
            if (st.Mode != 1 || !found) return;

            var zone = st.Zone;
            double dx = tx - st.LX, dy = ty - st.LY;
            double dt = Math.Max(0.004, (now - st.LastMove).TotalSeconds);
            st.LX = tx; st.LY = ty; st.LastMove = now;
            double along = zone.Horizontal ? dx : -dy;   // right / up = +
            st.Acc += along;

            if (!st.Locked)
            {
                double ta = zone.Horizontal ? Math.Abs(tx - st.SX) : Math.Abs(ty - st.SY);
                double tp = zone.Horizontal ? Math.Abs(ty - st.SY) : Math.Abs(tx - st.SX);
                if (tp > 0.06 && tp > ta * 1.2) { End(st); st.Mode = 2; return; }   // moved inward — regular cursor move
                if (ta > 0.015) st.Locked = true;
            }
            if (!st.Locked) return;

            double speed = Math.Abs(along) / dt;   // touchpad widths per second
            int mult = 1;
            if (cfg.Accel && speed > 0.9)
            {
                mult = 1 + (int)((speed - 0.9) / 0.6);
                int cap = zone.Action == 1 ? 8 : 3;
                if (mult > cap) mult = cap;
            }
            double step = Math.Max(0.01, zone.StepPct / 100.0);
            while (st.Acc >= step) { Fire(zone, +1, mult); st.Acc -= step; }
            while (st.Acc <= -step) { Fire(zone, -1, mult); st.Acc += step; }
        }

        void End(TouchState st)
        {
            st.Mode = 0; st.Locked = false; st.Zone = null;
            ActiveZone = null;
            Unfreeze();
        }

        public void Watchdog()
        {
            if ((DateTime.UtcNow - lastReport).TotalMilliseconds > 300)
            {
                if (clipped) Unfreeze();
                if (Touching) { Touching = false; ActiveZone = null; Dirty = true; }
            }
        }

        void Freeze()
        {
            Native.POINT p;
            if (!Native.GetCursorPos(out p)) return;
            var rc = new Native.RECT { L = p.X, T = p.Y, R = p.X + 1, B = p.Y + 1 };
            clipped = Native.ClipCursorRect(ref rc);
        }

        public void Unfreeze()
        {
            if (!clipped) return;
            Native.ClipCursorNone(IntPtr.Zero);
            clipped = false;
        }

        ZoneCfg FindZone(double x, double y)
        {
            ZoneCfg t = cfg.Zones[0], b = cfg.Zones[1], l = cfg.Zones[2], rr = cfg.Zones[3];
            if (t.Action != 0 && y <= t.SizePct / 100.0) return t;
            if (b.Action != 0 && y >= 1 - b.SizePct / 100.0) return b;
            if (l.Action != 0 && x <= l.SizePct / 100.0) return l;
            if (rr.Action != 0 && x >= 1 - rr.SizePct / 100.0) return rr;
            return null;
        }

        static double Clamp(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        public static string FmtAmount(int sec)
        {
            if (sec < 60) return sec + " " + L.T("сек", "s");
            return (sec / 60) + " " + L.T("мин", "min") + (sec % 60 != 0 ? " " + (sec % 60) + " " + L.T("сек", "s") : "");
        }

        bool OwnWindowActive()
        {
            uint pid;
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
            return pid == myPid;
        }

        void Fire(ZoneCfg z, int dir, int mult)
        {
            if (z.Invert) dir = -dir;
            // в окно настроек не шлём перемотку/прокрутку; громкость, яркость и треки — системные, шлём всегда
            bool send = !OwnWindowActive() || z.Action == 2 || z.Action == 3 || z.Action == 5;
            string text = "";
            switch (z.Action)
            {
                case 1:
                    if (send) Seeker.Add(z.SeekSec * mult * dir, Native.GetForegroundWindow());
                    text = (dir > 0 ? "⏩  +" : "⏪  −") + FmtAmount(z.SeekSec * mult);
                    break;
                case 2:
                    {
                        int v = Volume.Change(Math.Max(1, z.VolStep) * Math.Min(mult, 3) * dir);
                        if (v < 0) { Key(dir > 0 ? (byte)0xAF : (byte)0xAE, false, 5); v = -1; }   // запасной путь — клавишами
                        text = (dir > 0 ? "🔊  " : "🔉  ") + L.T("Громкость", "Volume") + (v >= 0 ? " " + v + " %" : "");
                        break;
                    }
                case 5:
                    Bright.Add(Math.Max(1, z.BrightStep) * Math.Min(mult, 3) * dir);
                    text = "☀  " + (dir > 0 ? L.T("Ярче", "Brighter") : L.T("Темнее", "Dimmer"));
                    break;
                case 3:
                    Key(dir > 0 ? (byte)0xB0 : (byte)0xB1, false);
                    text = dir > 0 ? "⏭  " + L.T("Следующий трек", "Next track") : "⏮  " + L.T("Предыдущий трек", "Previous track");
                    break;
                case 4:
                    if (send) Native.mouse_event(z.Horizontal ? 0x1000u : 0x0800u, 0, 0, dir * 120 * Math.Min(mult, 3), UIntPtr.Zero);
                    text = "↕  " + L.T("Прокрутка", "Scroll");
                    break;
            }
            Program.Log("FIRE " + z.Name + " " + text + " send=" + send);
            if (!send) text += "  · " + L.T("в этом окне только показ", "preview only in this window");
            var f = Fired; if (f != null) f(text);
        }

        // нажатия — из фоновой очереди с небольшой паузой / key presses from a background queue
        readonly System.Collections.Concurrent.BlockingCollection<Action> keyQueue = new System.Collections.Concurrent.BlockingCollection<Action>();
        int pending;

        void Key(byte vk, bool ext) { Key(vk, ext, KeyGapMs); }

        void Key(byte vk, bool ext, int gapMs)
        {
            uint e = ext ? 1u : 0u;
            byte scan = (byte)Native.MapVirtualKey(vk, 0);   // real scan code: browsers fill event.code from it
            Interlocked.Increment(ref pending);
            keyQueue.Add(() =>
            {
                Native.keybd_event(vk, scan, e, UIntPtr.Zero);
                Thread.Sleep(10);
                Native.keybd_event(vk, scan, e | 2u, UIntPtr.Zero);
                Thread.Sleep(gapMs);
                Interlocked.Decrement(ref pending);
            });
        }
    }

    // ======================= Тема: цвета Windows → палитра Material 3 / Theme =======================
    static class Theme
    {
        public static bool Dark;
        public static double Hue, Sat;
        public static Dictionary<string, Color> C = new Dictionary<string, Color>();

        public static void Load()
        {
            Dark = true;
            Color accent = Color.FromRgb(0x3A, 0x7B, 0xD5);
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    if (k != null) { var v = k.GetValue("AppsUseLightTheme"); if (v is int) Dark = (int)v == 0; }
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                    if (k != null)
                    {
                        var v = k.GetValue("AccentColor");
                        if (v is int) { uint a = unchecked((uint)(int)v); accent = Color.FromRgb((byte)(a & 0xFF), (byte)((a >> 8) & 0xFF), (byte)((a >> 16) & 0xFF)); }
                    }
            }
            catch { }
            double l;
            ToHsl(accent, out Hue, out Sat, out l);
            double s = Sat < 0.08 ? 0.12 : Math.Min(0.85, Math.Max(0.45, Sat));
            double h = Hue, t = Hue + 60;
            C.Clear();
            if (Dark)
            {
                C["Bg"] = Hsl(h, .14, .075); C["Surface"] = Hsl(h, .16, .115); C["SurfaceHigh"] = Hsl(h, .16, .15); C["SurfaceHighest"] = Hsl(h, .15, .19);
                C["Primary"] = Hsl(h, s, .80); C["OnPrimary"] = Hsl(h, s, .17);
                C["PrimaryContainer"] = Hsl(h, s * .65, .30); C["OnPrimaryContainer"] = Hsl(h, s, .91);
                C["SecondaryContainer"] = Hsl(h, .28, .27); C["OnSecondaryContainer"] = Hsl(h, .4, .90);
                C["Tertiary"] = Hsl(t, .55, .80); C["TertiaryContainer"] = Hsl(t, .4, .30);
                C["OnSurface"] = Hsl(h, .12, .93); C["OnSurfaceVariant"] = Hsl(h, .10, .74);
                C["Outline"] = Hsl(h, .08, .56); C["OutlineVariant"] = Hsl(h, .10, .30);
            }
            else
            {
                C["Bg"] = Hsl(h, .40, .975); C["Surface"] = Hsl(h, .34, .945); C["SurfaceHigh"] = Hsl(h, .28, .915); C["SurfaceHighest"] = Hsl(h, .24, .885);
                C["Primary"] = Hsl(h, s, .40); C["OnPrimary"] = Colors.White;
                C["PrimaryContainer"] = Hsl(h, .85, .88); C["OnPrimaryContainer"] = Hsl(h, .7, .16);
                C["SecondaryContainer"] = Hsl(h, .38, .86); C["OnSecondaryContainer"] = Hsl(h, .4, .16);
                C["Tertiary"] = Hsl(t, .5, .40); C["TertiaryContainer"] = Hsl(t, .7, .88);
                C["OnSurface"] = Hsl(h, .15, .10); C["OnSurfaceVariant"] = Hsl(h, .10, .32);
                C["Outline"] = Hsl(h, .08, .47); C["OutlineVariant"] = Hsl(h, .14, .80);
            }
        }

        public static Color Zone(int i, bool container)
        {
            double h = Hue + new double[] { 0, 60, 150, 260 }[i];
            if (Dark) return container ? Hsl(h, .40, .30) : Hsl(h, .60, .78);
            return container ? Hsl(h, .75, .86) : Hsl(h, .55, .40);
        }

        public static void Apply(ResourceDictionary rd)
        {
            foreach (var kv in C) { var b = new SolidColorBrush(kv.Value); b.Freeze(); rd[kv.Key] = b; }
        }

        public static void ToHsl(Color c, out double h, out double s, out double l)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2; h = 0; s = 0;
            if (max == min) return;
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0); else if (max == g) h = (b - r) / d + 2; else h = (r - g) / d + 4;
            h *= 60;
        }

        public static Color Hsl(double h, double s, double l)
        {
            h = ((h % 360) + 360) % 360 / 360.0;
            double r, g, b;
            if (s <= 0) r = g = b = l;
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
                r = Hue2(p, q, h + 1.0 / 3); g = Hue2(p, q, h); b = Hue2(p, q, h - 1.0 / 3);
            }
            return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
        }

        static double Hue2(double p, double q, double t)
        {
            if (t < 0) t += 1; if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
    }

    // ======================= Автозапуск / Autostart =======================
    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "PadGestures";

        public static string ExePath { get { return Assembly.GetExecutingAssembly().Location; } }
        static string Command { get { return "\"" + ExePath + "\" /tray"; } }

        public static bool IsOn()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(Name) != null;
        }

        public static void Set(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(Name, Command);
                else k.DeleteValue(Name, false);
            }
        }

        // Если программу перенесли — автозапуск сам переключается на текущее место exe.
        // If the program was moved, autostart is re-pointed to the current exe location.
        public static void Heal()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    var v = k.GetValue(Name) as string;
                    if (v != null && v != Command) k.SetValue(Name, Command);
                }
            }
            catch { }
        }
    }

    // ======================= Приложение / Application =======================
    class App
    {
        readonly Config cfg = new Config();
        GestureEngine eng;
        Window win;
        Canvas pad;
        TextBlock status, actionText, titleText, previewTitle, previewHint;
        Border actionPill;
        UniformGrid zonesHost;
        StackPanel globalHost;
        CheckBox master;
        WF.NotifyIcon tray;
        HwndSource sink;
        int padCount;
        string lastFired = "";

        public void Run(bool startHidden)
        {
            cfg.Load();
            L.Set(cfg.Lang);
            Autostart.Heal();
            eng = new GestureEngine(cfg);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Theme.Load();
            Theme.Apply(app.Resources);

            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Ui.xaml"))
                win = (Window)XamlReader.Load(s);
            pad = (Canvas)win.FindName("PadCanvas");
            status = (TextBlock)win.FindName("StatusText");
            actionText = (TextBlock)win.FindName("ActionText");
            actionPill = (Border)win.FindName("ActionPill");
            zonesHost = (UniformGrid)win.FindName("ZonesHost");
            globalHost = (StackPanel)win.FindName("GlobalHost");
            titleText = (TextBlock)win.FindName("TitleText");
            previewTitle = (TextBlock)win.FindName("PreviewTitle");
            previewHint = (TextBlock)win.FindName("PreviewHint");
            master = (CheckBox)win.FindName("MasterSwitch");
            try { win.Icon = AppIconSource(); } catch { }

            // WPF на точном тачпаде крутит вдвое быстрее обычных программ — приводим к норме
            var scroller = (ScrollViewer)win.FindName("Scroller");
            scroller.PreviewMouseWheel += (s, e) =>
            {
                scroller.ScrollToVerticalOffset(scroller.VerticalOffset - e.Delta * 0.45);
                e.Handled = true;
            };
            master.IsChecked = cfg.Enabled;
            master.Checked += delegate { cfg.Enabled = true; Save(); UpdateStatus(); };
            master.Unchecked += delegate { cfg.Enabled = false; Save(); UpdateStatus(); };

            win.SourceInitialized += delegate { ApplyTitleBar(); };
            win.SizeChanged += delegate { zonesHost.Columns = win.ActualWidth < 900 ? 1 : 2; };
            win.Closing += (s, e) => { e.Cancel = true; win.Hide(); };

            padCount = PadDevice.Enumerate().Count;
            RebuildTexts();

            // скрытое окно получает ввод тачпада, даже когда настройки закрыты
            sink = new HwndSource(new HwndSourceParameters("PadGesturesSink") { Width = 0, Height = 0, WindowStyle = 0 });
            sink.AddHook(Hook);
            var rid = new[] { new Native.RAWINPUTDEVICE { usUsagePage = 0x0D, usUsage = 0x05, dwFlags = Native.RIDEV_INPUTSINK, hwndTarget = sink.Handle } };
            Native.RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICE)));

            eng.Fired += t => { lastFired = t; ShowAction(t); };
            eng.Seeker.Info = info => win.Dispatcher.BeginInvoke(new Action(() => ShowAction(lastFired + "  ·  " + info)));
            eng.Bright.Info = v => win.Dispatcher.BeginInvoke(new Action(() => ShowAction("☀  " + L.T("Яркость", "Brightness") + " " + v + " %")));
            var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            int tick = 0;
            timer.Tick += delegate
            {
                if (++tick % 6 == 0) eng.Watchdog();
                if (eng.Dirty && win.IsVisible) { eng.Dirty = false; DrawPad(); }
            };
            timer.Start();

            SystemEvents.UserPreferenceChanged += (s, e) => win.Dispatcher.BeginInvoke(new Action(Retheme));

            CreateTray();
            DrawPad();
            if (!startHidden) win.Show();
            app.Run();
        }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_INPUT) { try { eng.OnRawInput(lParam); } catch { } }
            return IntPtr.Zero;
        }

        void Save() { cfg.Save(); eng.Dirty = true; }

        // все тексты интерфейса на текущем языке / all UI texts in the current language
        void RebuildTexts()
        {
            win.Title = L.T("Жесты тачпада", "Touchpad Gestures");
            titleText.Text = win.Title;
            master.Content = L.T("Включено", "Enabled");
            previewTitle.Text = L.T("Живой просмотр", "Live preview");
            previewHint.Text = L.T(
                "Коснитесь тачпада: точки покажут пальцы, зона подсветится. Начните касание у края и ведите палец вдоль него. В этом окне перемотка только показывается — проверяйте на видео.",
                "Touch the touchpad: dots show your fingers and the zone lights up. Start at an edge and slide along it. Seeking is only previewed in this window — try it on a video.");
            UpdateStatus();
            BuildZones();
            BuildGlobal();
            DrawPad();
            if (tray != null) BuildTrayMenu();
        }

        void UpdateStatus()
        {
            string s = padCount > 0 ? L.T("Тачпад найден", "Touchpad found") : L.T("Тачпад не найден — подключите клавиатуру", "Touchpad not found — connect the keyboard");
            s += cfg.Enabled ? L.T(" · жесты работают", " · gestures on") : L.T(" · на паузе", " · paused");
            status.Text = s;
        }

        void Retheme()
        {
            Theme.Load();
            Theme.Apply(Application.Current.Resources);
            ApplyTitleBar();
            BuildZones();
            DrawPad();
        }

        void ApplyTitleBar()
        {
            var h = new WindowInteropHelper(win).Handle;
            if (h == IntPtr.Zero) return;
            int dark = Theme.Dark ? 1 : 0;
            Native.DwmSetWindowAttribute(h, 20, ref dark, 4);
            var bg = Theme.C["Bg"]; int cap = bg.R | (bg.G << 8) | (bg.B << 16);
            Native.DwmSetWindowAttribute(h, 35, ref cap, 4);
            var fg = Theme.C["OnSurface"]; int txt = fg.R | (fg.G << 8) | (fg.B << 16);
            Native.DwmSetWindowAttribute(h, 36, ref txt, 4);
        }

        // ---------- элементы интерфейса / UI helpers ----------
        static TextBlock Text(string t, double size, FontWeight w, string brush)
        {
            var tb = new TextBlock { Text = t, FontSize = size, FontWeight = w, TextWrapping = TextWrapping.Wrap };
            tb.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return tb;
        }

        FrameworkElement SliderRow(string title, double min, double max, double val, double tick, Func<double, string> fmt, Action<double> set)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            var head = new DockPanel();
            var valueText = Text(fmt(val), 14, FontWeights.SemiBold, "Primary");
            DockPanel.SetDock(valueText, Dock.Right);
            head.Children.Add(valueText);
            head.Children.Add(Text(title, 14, FontWeights.Normal, "OnSurfaceVariant"));
            sp.Children.Add(head);
            var sl = new Slider { Minimum = min, Maximum = max, Value = val, TickFrequency = tick, LargeChange = tick, SmallChange = tick };
            sl.Style = (Style)win.FindResource("M3Slider");
            sl.ValueChanged += (s, e) => { valueText.Text = fmt(sl.Value); set(sl.Value); Save(); };
            sp.Children.Add(sl);
            return sp;
        }

        FrameworkElement SwitchRow(string title, string sub, bool val, Action<bool> set)
        {
            var cb = new CheckBox { IsChecked = val, Margin = new Thickness(0, 12, 0, 0) };
            cb.Style = (Style)win.FindResource("M3Switch");
            var sp = new StackPanel();
            sp.Children.Add(Text(title, 15, FontWeights.SemiBold, "OnSurface"));
            if (sub != null) { var t = Text(sub, 13, FontWeights.Normal, "OnSurfaceVariant"); t.Margin = new Thickness(0, 2, 0, 0); sp.Children.Add(t); }
            cb.Content = sp;
            cb.Checked += delegate { set(true); };
            cb.Unchecked += delegate { set(false); };
            return cb;
        }

        WrapPanel Chips(string group, string[] items, int selected, Action<int> set)
        {
            var wp = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            for (int i = 0; i < items.Length; i++)
            {
                int idx = i;
                var rb = new RadioButton { Content = items[i], GroupName = group, IsChecked = i == selected };
                rb.Style = (Style)win.FindResource("Chip");
                rb.Checked += delegate { set(idx); };
                wp.Children.Add(rb);
            }
            return wp;
        }

        // порядок чипов → код действия / chip order → action code
        static readonly List<int> ActionOrder = new List<int> { 0, 1, 2, 5, 3, 4 };
        static string[] ActionNames
        {
            get
            {
                return new[] { L.T("Выкл", "Off"), L.T("Перемотка", "Seek"), L.T("Громкость", "Volume"),
                               L.T("Яркость", "Brightness"), L.T("Треки", "Tracks"), L.T("Прокрутка", "Scroll") };
            }
        }
        static string ActionName(int code) { int i = ActionOrder.IndexOf(code); return i < 0 ? "" : ActionNames[i]; }
        static readonly double[] SpeedSteps = { 22, 18, 15, 12, 10, 8.5, 7, 6, 5, 4, 3.2, 2.5 };

        static int SpeedFromStep(double step)
        {
            int best = 0;
            for (int i = 0; i < SpeedSteps.Length; i++) if (Math.Abs(SpeedSteps[i] - step) < Math.Abs(SpeedSteps[best] - step)) best = i;
            return best + 1;
        }

        void BuildZones()
        {
            zonesHost.Children.Clear();
            for (int i = 0; i < cfg.Zones.Length; i++)
            {
                var z = cfg.Zones[i];
                int zi = i;
                var card = new Border();
                card.Style = (Style)win.FindResource("Card");
                var root = new StackPanel();
                card.Child = root;

                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                var icon = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Theme.Zone(zi, true)), Margin = new Thickness(0, 0, 14, 0) };
                icon.Child = new TextBlock { Text = z.Glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Theme.Zone(zi, false)) };
                head.Children.Add(icon);
                var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var tt = Text(z.Title, 20, FontWeights.SemiBold, "OnSurface");
                tt.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
                titles.Children.Add(tt);
                titles.Children.Add(Text(z.Horizontal ? L.T("свайп влево / вправо", "swipe left / right") : L.T("свайп вверх / вниз", "swipe up / down"), 13, FontWeights.Normal, "OnSurfaceVariant"));
                head.Children.Add(titles);
                root.Children.Add(head);

                var details = new StackPanel();
                var seekRow = SliderRow(L.T("Перемотка за шаг", "Seek per step"), 5, 300, z.SeekSec, 5, v => GestureEngine.FmtAmount((int)v), v => z.SeekSec = (int)v);
                var volRow = SliderRow(L.T("Громкость за шаг", "Volume per step"), 1, 20, z.VolStep, 1, v => (int)v + " %", v => z.VolStep = (int)v);
                var brightRow = SliderRow(L.T("Яркость за шаг", "Brightness per step"), 1, 25, z.BrightStep, 1, v => (int)v + " %", v => z.BrightStep = (int)v);
                Action<int> showRows = a =>
                {
                    details.Visibility = a == 0 ? Visibility.Collapsed : Visibility.Visible;
                    seekRow.Visibility = a == 1 ? Visibility.Visible : Visibility.Collapsed;
                    volRow.Visibility = a == 2 ? Visibility.Visible : Visibility.Collapsed;
                    brightRow.Visibility = a == 5 ? Visibility.Visible : Visibility.Collapsed;
                };

                root.Children.Add(Chips("zone_" + z.Name, ActionNames, ActionOrder.IndexOf(z.Action), ci =>
                {
                    z.Action = ActionOrder[ci]; Save();
                    showRows(z.Action);
                }));

                details.Children.Add(seekRow);
                details.Children.Add(volRow);
                details.Children.Add(brightRow);
                details.Children.Add(SliderRow(L.T("Скорость", "Speed"), 1, SpeedSteps.Length, SpeedFromStep(z.StepPct), 1,
                    v => ((int)v).ToString(), v => z.StepPct = SpeedSteps[(int)v - 1]));
                details.Children.Add(SliderRow(L.T("Ширина зоны у края", "Edge zone width"), 3, 40, z.SizePct, 1, v => (int)v + " %", v => z.SizePct = (int)v));
                details.Children.Add(SwitchRow(L.T("Наоборот", "Reverse direction"), null, z.Invert, v => { z.Invert = v; Save(); }));
                showRows(z.Action);
                root.Children.Add(details);
                zonesHost.Children.Add(card);
            }
        }

        void BuildGlobal()
        {
            globalHost.Children.Clear();
            var h = Text(L.T("Общие", "General"), 20, FontWeights.SemiBold, "OnSurface");
            h.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
            globalHost.Children.Add(h);

            globalHost.Children.Add(SwitchRow(L.T("Не двигать курсор во время жеста", "Keep the cursor still during a gesture"),
                L.T("Пока ведёте палец по краю, указатель стоит на месте", "While you slide along an edge, the pointer doesn't move"),
                cfg.FreezeCursor, v => { cfg.FreezeCursor = v; Save(); }));
            globalHost.Children.Add(SwitchRow(L.T("Ускорение", "Acceleration"),
                L.T("Чем быстрее свайп, тем больше шаг", "The faster you swipe, the bigger the step"),
                cfg.Accel, v => { cfg.Accel = v; Save(); }));
            globalHost.Children.Add(SwitchRow(L.T("Запускать вместе с Windows", "Start with Windows"),
                L.T("Работает в фоне, иконка в трее. Запуск из: ", "Runs in the background, icon in the tray. Starts from: ") + Autostart.ExePath,
                Autostart.IsOn(), Autostart.Set));

            var langLabel = Text(L.T("Язык", "Language"), 14, FontWeights.Normal, "OnSurfaceVariant");
            langLabel.Margin = new Thickness(0, 16, 0, 6);
            globalHost.Children.Add(langLabel);
            string[] langs = { "auto", "ru", "en" };
            int sel = Array.IndexOf(langs, cfg.Lang); if (sel < 0) sel = 0;
            globalHost.Children.Add(Chips("lang", new[] { L.T("Как в Windows", "System"), "Русский", "English" }, sel, i =>
            {
                cfg.Lang = langs[i]; L.Set(cfg.Lang); Save();
                win.Dispatcher.BeginInvoke(new Action(RebuildTexts));   // не пересобираем во время клика по чипу
            }));

            var about = Text(L.T(
                "PadGestures " + Program.Version + " · неофициальная программа, не связана с ASUS и Microsoft. Используйте на свой риск. Настройки: " + Program.DataDir,
                "PadGestures " + Program.Version + " · unofficial, not affiliated with ASUS or Microsoft. Use at your own risk. Settings: " + Program.DataDir),
                12, FontWeights.Normal, "OnSurfaceVariant");
            about.Margin = new Thickness(0, 18, 0, 0);
            about.Opacity = 0.8;
            globalHost.Children.Add(about);
        }

        // ---------- живой просмотр / live preview ----------
        void DrawPad()
        {
            double W = 600, H = Math.Round(W / Math.Max(1.0, eng.PadAspect));
            pad.Width = W; pad.Height = H;
            pad.Children.Clear();
            pad.Clip = new RectangleGeometry(new Rect(0, 0, W, H), 30, 30);
            pad.Children.Add(new Rectangle { Width = W, Height = H, Fill = (Brush)win.FindResource("SurfaceHighest") });

            for (int i = 0; i < 4; i++)
            {
                var z = cfg.Zones[i];
                if (z.Action == 0) continue;
                double zs = z.SizePct / 100.0;
                Rect r;
                if (i == 0) r = new Rect(0, 0, W, H * zs);
                else if (i == 1) r = new Rect(0, H - H * zs, W, H * zs);
                else if (i == 2) r = new Rect(0, 0, W * zs, H);
                else r = new Rect(W - W * zs, 0, W * zs, H);
                bool active = eng.ActiveZone == z;
                var rect = new Rectangle { Width = r.Width, Height = r.Height, Fill = new SolidColorBrush(active ? Theme.Zone(i, false) : Theme.Zone(i, true)), Opacity = active ? 0.85 : 0.75, RadiusX = 10, RadiusY = 10 };
                Canvas.SetLeft(rect, r.X); Canvas.SetTop(rect, r.Y);
                pad.Children.Add(rect);
                var label = new TextBlock { Text = ActionName(z.Action), FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(active ? Theme.Zone(i, true) : Theme.Zone(i, false)) };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double lx = r.X + (r.Width - label.DesiredSize.Width) / 2, ly = r.Y + (r.Height - label.DesiredSize.Height) / 2;
                if (!z.Horizontal)
                {
                    label.LayoutTransform = new RotateTransform(i == 2 ? -90 : 90);
                    lx = r.X + (r.Width - label.DesiredSize.Height) / 2; ly = r.Y + (r.Height - label.DesiredSize.Width) / 2;
                }
                Canvas.SetLeft(label, lx); Canvas.SetTop(label, ly);
                pad.Children.Add(label);
            }

            if (eng.Touching)
            {
                var fingers = eng.Fingers;
                foreach (var f in fingers)
                {
                    double fx = f.X * W, fy = f.Y * H;
                    var halo = new Ellipse { Width = 64, Height = 64, Fill = (Brush)win.FindResource("Primary"), Opacity = 0.22 };
                    Canvas.SetLeft(halo, fx - 32); Canvas.SetTop(halo, fy - 32);
                    pad.Children.Add(halo);
                    var dot = new Ellipse { Width = 28, Height = 28, Fill = (Brush)win.FindResource("Primary") };
                    Canvas.SetLeft(dot, fx - 14); Canvas.SetTop(dot, fy - 14);
                    pad.Children.Add(dot);
                }
                if (fingers.Count > 1)
                {
                    string word = L.Ru ? (fingers.Count < 5 ? " пальца" : " пальцев") : " fingers";
                    var cnt = Text(fingers.Count + word, 16, FontWeights.SemiBold, "OnSurfaceVariant");
                    Canvas.SetRight(cnt, 22); Canvas.SetBottom(cnt, 16);
                    pad.Children.Add(cnt);
                }
            }
        }

        void ShowAction(string text)
        {
            actionText.Text = text;
            var fade = new DoubleAnimationUsingKeyFrames();
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1400))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1800))));
            actionPill.BeginAnimation(UIElement.OpacityProperty, fade);
            var st = (ScaleTransform)actionPill.RenderTransform;
            var pop = new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.8 } };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }

        // ---------- иконка и трей / icon and tray ----------
        static SD.Icon AppIcon()
        {
            try { return SD.Icon.ExtractAssociatedIcon(Autostart.ExePath); } catch { return SD.SystemIcons.Application; }
        }

        static ImageSource AppIconSource()
        {
            using (var ic = AppIcon())
                return Imaging.CreateBitmapSourceFromHIcon(ic.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }

        void CreateTray()
        {
            tray = new WF.NotifyIcon { Icon = AppIcon(), Text = "PadGestures", Visible = true };
            tray.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) ShowWin(); };
            BuildTrayMenu();
        }

        void BuildTrayMenu()
        {
            var menu = new WF.ContextMenuStrip();
            menu.Items.Add(L.T("Настройки", "Settings"), null, delegate { ShowWin(); });
            menu.Items.Add(L.T("Пауза / продолжить", "Pause / resume"), null, delegate { master.IsChecked = !(master.IsChecked == true); });
            menu.Items.Add(L.T("Выход", "Exit"), null, delegate
            {
                eng.Unfreeze();
                tray.Visible = false;
                Application.Current.Shutdown();
            });
            tray.ContextMenuStrip = menu;
            tray.Text = L.T("Жесты тачпада", "Touchpad Gestures");
        }

        void ShowWin()
        {
            win.Show();
            if (win.WindowState == WindowState.Minimized) win.WindowState = WindowState.Normal;
            win.Activate();
            DrawPad();
        }
    }

    static class Program
    {
        public const string Version = "1.0.0";
        public static string DataDir { get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PadGestures"); } }

        static StreamWriter logW;
        public static void Log(string s) { if (logW != null) lock (logW) logW.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + s); }

        [STAThread]
        static void Main(string[] args)
        {
            bool tray = false, check = false;
            foreach (var a in args)
            {
                if (a == "/tray") tray = true;
                if (a == "/check") check = true;
                if (a == "/log")
                {
                    Directory.CreateDirectory(DataDir);
                    logW = new StreamWriter(System.IO.Path.Combine(DataDir, "PadGestures.log"), false) { AutoFlush = true };
                }
            }
            if (check)
            {
                var sb = new StringBuilder();
                foreach (var d in PadDevice.Enumerate()) sb.AppendLine(d.Name + " | contacts=" + d.Links.Count + " X=" + d.XMin + ".." + d.XMax + " Y=" + d.YMin + ".." + d.YMax);
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(System.IO.Path.Combine(DataDir, "check.txt"), sb.Length == 0 ? "no touchpads" : sb.ToString());
                return;
            }
            bool created;
            using (var mtx = new Mutex(true, "PadGestures_single_instance", out created))
            {
                if (!created)
                {
                    L.Set("auto");
                    MessageBox.Show(L.T("Жесты тачпада уже запущены — иконка в трее.", "Touchpad Gestures is already running — see the tray icon."), "PadGestures");
                    return;
                }
                try { new App().Run(tray); }
                catch (Exception ex)
                {
                    Native.ClipCursorNone(IntPtr.Zero);
                    try { Directory.CreateDirectory(DataDir); File.WriteAllText(System.IO.Path.Combine(DataDir, "crash.txt"), ex.ToString()); } catch { }
                    MessageBox.Show(ex.ToString(), "PadGestures — error");
                }
            }
        }
    }
}
