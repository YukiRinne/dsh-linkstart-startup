// DeepSeek Harness - startup launcher.
//
// This program owns the screen first, starts DeepSeek Harness in the background,
// plays the 17.43 s opening film in front of it and then dissolves into the session
// window that finished loading behind it. DSH's own "Loading plugins..." screen
// still happens; it simply happens where it cannot be seen.
//
// Phases:  voice (1.17 s) -> film (17.43 s, dissolve over the last 0.7 s) -> exit.
//
// Structure note (SPEC section 5.2, the most expensive trap in the original): the
// body of Prepare() contains no `return` at all. Everything that has to happen on
// every run - starting DSH, loading the title artwork, starting the background
// account read, starting the frame clock and the safety watchdog - runs
// unconditionally at the top of Prepare(), before any geometry decision.
// In the original those lines sat *after* a probe branch that returned early, so
// with the default configuration the frame timer was never created and no panel or
// title was ever drawn: it looked like missing features, and it was dead code.
// Every branch now lives in a helper, so no branch can skip the setup work.
//
// Language level: the compiler is the in-box C# 5 csc.exe. Everything below stays
// inside that subset - no null-conditional member access, no string interpolation,
// no expression-bodied members, no automatic property initialisers, no tuple
// deconstruction and no awaiter-based asynchronous methods. Anonymous delegates
// are written out in full rather than as lambdas, which also keeps generic
// inference out of the picture.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

public static class Program
{
    /// Filled from launcher.json's "DshPath", or by the generic probe below when
    /// that key is empty. Never a machine-specific default: a fresh download has to
    /// work on somebody else's computer.
    public static string DshExe = "";

    /// True once DSH has really been started, so no failure path can start a second
    /// copy of it.
    static bool dshLaunched;

    /// The single-instance lock. Held in a static field so the handle cannot be
    /// collected while the film is playing.
    static Mutex instance;

    /// Parsed once in Main and shared with the window.
    internal static LauncherOptions Options;

    /// The process to look for when locating DSH's window. Derived from the
    /// executable name so a renamed or moved copy still works.
    public static string DshProcessName
    {
        get
        {
            try { return Path.GetFileNameWithoutExtension(DshExe); }
            catch { return ""; }
        }
    }

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            Options = LauncherOptions.Load();

            // Diagnostics come first and must not open a window, start DSH or take
            // the single-instance lock, so they stay usable while the launcher runs.
            if (HasArg(args, "--check")) { WriteCheck(); return; }
            if (HasArg(args, "--bounds")) { WriteBounds(); return; }

            // One launcher at a time. Two instances means two animation windows
            // covering each other, which reads exactly like "the animation stopped
            // working" - and that is what used to happen on a double click.
            bool first;
            instance = new Mutex(true, @"Local\DeepSeekHarnessStartup", out first);
            if (!first)
            {
                Log("already-running", "another launcher owns the screen; exiting");
                return;
            }

            Application app = new Application();
            // Explicit shutdown: the window does not exist yet, so OnMainWindowClose
            // would end the process the moment Run() is entered.
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            StartupWindow window = new StartupWindow(args);
            window.Closed += delegate(object sender, EventArgs e) { app.Shutdown(); };
            app.Startup += delegate(object sender, StartupEventArgs e) { window.Prepare(); };

            // Deliberately no window argument: the window is shown only once its
            // geometry is settled, so Run() must not show it for us.
            app.Run();
        }
        catch (Exception ex)
        {
            // Whatever went wrong, the user still wants their editor: record it,
            // start DSH anyway and get out of the way.
            Log("fatal", ex.ToString());
            TryLaunchDsh();
        }
    }

    internal static void Log(string evt, string detail)
    {
        // The log must never be the thing that breaks a launch.
        try
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "launcher.log"),
                DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture)
                    + " " + evt + " " + detail + Environment.NewLine,
                Encoding.UTF8);
        }
        catch { }
    }

    internal static bool TryLaunchDsh()
    {
        if (dshLaunched) return true;
        try
        {
            if (DshExe.Length == 0 || !File.Exists(DshExe))
            {
                Log("dsh-missing", DshExe.Length == 0 ? "(no DshPath, nothing found by the probe)" : DshExe);
                return false;
            }
            ProcessStartInfo startInfo = new ProcessStartInfo(DshExe);
            startInfo.WorkingDirectory = Path.GetDirectoryName(DshExe);
            startInfo.UseShellExecute = true;
            Process.Start(startInfo);
            dshLaunched = true;
            Log("dsh-launched", DshExe);
            return true;
        }
        catch (Exception ex)
        {
            Log("dsh-launch-failed", ex.Message);
            return false;
        }
    }

    /// --check: resolve the account and write the outcome to logs\check.txt. The
    /// launcher is a winexe, so there is no console to print to - and this is the
    /// mode that exposes the TLS 1.2 problem, because it calls Load synchronously
    /// and therefore actually performs the request.
    static void WriteCheck()
    {
        DshAccount state = new DshAccount { locale = "ja-JP" };
        string report;
        try
        {
            AccountReader.Load(state);
            report = String.Join(Environment.NewLine, new string[] {
                "credentials : " + AccountReader.CredentialsPath(),
                "exists      : " + (File.Exists(AccountReader.CredentialsPath()) ? "yes" : "no"),
                "status      : " + state.status,
                "nameSource  : " + state.nameSource,
                "displayName : " + (String.IsNullOrEmpty(state.displayName) ? "(none)" : state.displayName),
                "Title()     : " + state.Title(),
                "Name()      : " + state.Name(),
                "Lines()[0]  : " + state.Lines()[0],
                "Lines()[1]  : " + state.Lines()[1],
                "detail      : " + state.detail,
            });
        }
        catch (Exception ex)
        {
            // AccountReader is not supposed to throw; if it ever does, the report
            // has to say so instead of being left empty.
            report = "account read threw: " + ex;
            Log("fatal", "check: " + ex.Message);
        }
        WriteReport("check.txt", report);
    }

    /// --bounds: report DSH's client rectangle in launcher.json form, so detection
    /// can be skipped entirely on a fixed machine.
    static void WriteBounds()
    {
        string report;
        Rect? bounds = Win32.DshBounds();
        if (bounds.HasValue)
        {
            Rect client = bounds.Value;
            Rect cover = Geometry.Cover16x9(client);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("DSH client rectangle (the content area, not the outer frame):");
            sb.AppendLine(String.Format(CultureInfo.InvariantCulture,
                "  left {0}  top {1}  width {2}  height {3}   (display scale {4:F2})",
                (int)client.Left, (int)client.Top, (int)client.Width, (int)client.Height, Win32.Scale()));
            sb.AppendLine();
            sb.AppendLine("Paste into launcher.json to skip detection:");
            sb.AppendLine("  \"WindowBounds\": \"fixed\",");
            sb.AppendLine(String.Format(CultureInfo.InvariantCulture, "  \"WindowLeft\": {0},", (int)client.Left));
            sb.AppendLine(String.Format(CultureInfo.InvariantCulture, "  \"WindowTop\": {0},", (int)client.Top));
            sb.AppendLine(String.Format(CultureInfo.InvariantCulture, "  \"WindowWidth\": {0},", (int)client.Width));
            sb.AppendLine(String.Format(CultureInfo.InvariantCulture, "  \"WindowHeight\": {0}", (int)client.Height));
            sb.AppendLine();
            sb.AppendLine("The animation would then occupy (16:9, covering):");
            sb.AppendLine("  " + DescribeRect(cover));
            report = sb.ToString();
        }
        else
        {
            report = "No DeepSeek Harness window found." + Environment.NewLine
                + "Start DSH first, then run --bounds again." + Environment.NewLine
                + "Looked for process: " + (DshProcessName.Length == 0 ? "(unknown: no DshPath)" : DshProcessName)
                + Environment.NewLine
                + "DshExe: " + (DshExe.Length == 0 ? "(not found)" : DshExe);
        }
        WriteReport("bounds.txt", report);
    }

    static string DescribeRect(Rect r)
    {
        return String.Format(CultureInfo.InvariantCulture, "{0},{1} {2}x{3}",
            (int)r.Left, (int)r.Top, (int)r.Width, (int)r.Height);
    }

    static void WriteReport(string name, string text)
    {
        try
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, name), text, Encoding.UTF8);
        }
        catch (Exception ex) { Log("fatal", "report " + name + ": " + ex.Message); }
    }

    static bool HasArg(string[] args, string name)
    {
        if (args == null) return false;
        for (int i = 0; i < args.Length; i++)
            if (String.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}

/// Runtime configuration. Every value has a usable default and nothing in here
/// points at a developer machine: a missing file, a missing key or an illegal value
/// all fall back instead of throwing.
internal sealed class LauncherOptions
{
    public string DshPath = "";                 // empty -> DshProbe.Find()
    public string WindowBounds = "auto";        // "auto" | "fixed"
    public double WindowLeft = 0;               // only used by "fixed"
    public double WindowTop = 0;
    public double WindowWidth = 0;
    public double WindowHeight = 0;
    public string VideoFit = "aspect";          // "aspect" | "fill" | "fit" | "stretch"
    public double FadeLeadSeconds = 0.7;        // 17.43 - 0.7 = 16.73, the dissolve point
    public double FadeOutSeconds = 0.7;         // 17.43 - 16.73
    public bool MatchDshWindow = true;
    public double MatchWaitSeconds = 6.0;       // how long to wait for DSH's window
    public double ExtraHoldSeconds = 1.2;       // held after the film, before hand-over
    public double MaxHoldSeconds = 25.0;        // hard ceiling (SPEC 2.4)
    public bool AllowSkip = true;               // Escape closes

    /// True when the config pins the rectangle, so nothing has to be detected.
    public bool HasFixedBounds
    {
        get { return WindowBounds == "fixed" && WindowWidth >= 200 && WindowHeight >= 200; }
    }

    public static LauncherOptions Load()
    {
        LauncherOptions options = new LauncherOptions();
        try
        {
            Parse(options);
        }
        catch (Exception ex)
        {
            // A bad config is a nuisance, not a crash: the defaults stand.
            Program.Log("options", "launcher.json could not be used: " + ex.Message);
        }
        try
        {
            Program.DshExe = options.DshPath;
            if (Program.DshExe.Length == 0) Program.DshExe = DshProbe.Find();
        }
        catch (Exception ex)
        {
            Program.Log("dsh-default", "probe failed: " + ex.Message);
        }
        Program.Log("options", String.Format(CultureInfo.InvariantCulture,
            "bounds={0} fit={1} lead={2:F2} fade={3:F2} hold={4:F2} max={5:F2} match={6} wait={7:F2} skip={8} dsh={9}",
            options.WindowBounds, options.VideoFit, options.FadeLeadSeconds, options.FadeOutSeconds,
            options.ExtraHoldSeconds, options.MaxHoldSeconds, options.MatchDshWindow,
            options.MatchWaitSeconds, options.AllowSkip,
            Program.DshExe.Length == 0 ? "(none)" : Program.DshExe));
        return options;
    }

    static void Parse(LauncherOptions options)
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher.json");
        if (!File.Exists(path)) return;

        Dictionary<string, string> members = JsonScan.ObjectMembers(File.ReadAllText(path, Encoding.UTF8));

        options.DshPath = Member(members, "DshPath", options.DshPath).Trim();
        options.WindowBounds = Choice(members, "WindowBounds", options.WindowBounds,
            new string[] { "auto", "fixed" });
        options.WindowLeft = Finite(Number(members, "WindowLeft", options.WindowLeft), options.WindowLeft);
        options.WindowTop = Finite(Number(members, "WindowTop", options.WindowTop), options.WindowTop);
        options.WindowWidth = Finite(Number(members, "WindowWidth", options.WindowWidth), options.WindowWidth);
        options.WindowHeight = Finite(Number(members, "WindowHeight", options.WindowHeight), options.WindowHeight);
        options.VideoFit = Choice(members, "VideoFit", options.VideoFit,
            new string[] { "aspect", "fill", "fit", "stretch" });
        options.FadeLeadSeconds = NonNegative(Number(members, "FadeLeadSeconds", options.FadeLeadSeconds),
            options.FadeLeadSeconds);
        options.FadeOutSeconds = NonNegative(Number(members, "FadeOutSeconds", options.FadeOutSeconds),
            options.FadeOutSeconds);
        options.MatchDshWindow = Flag(members, "MatchDshWindow", options.MatchDshWindow);
        options.MatchWaitSeconds = NonNegative(Number(members, "MatchWaitSeconds", options.MatchWaitSeconds),
            options.MatchWaitSeconds);
        options.ExtraHoldSeconds = NonNegative(Number(members, "ExtraHoldSeconds", options.ExtraHoldSeconds),
            options.ExtraHoldSeconds);
        options.MaxHoldSeconds = Positive(Number(members, "MaxHoldSeconds", options.MaxHoldSeconds),
            options.MaxHoldSeconds);
        options.AllowSkip = Flag(members, "AllowSkip", options.AllowSkip);
    }

    static string Member(Dictionary<string, string> members, string key, string fallback)
    {
        string value;
        if (members != null && members.TryGetValue(key, out value) && value != null) return value;
        return fallback;
    }

    static double Number(Dictionary<string, string> members, string key, double fallback)
    {
        string raw = Member(members, key, null);
        if (raw == null) return fallback;
        double value;
        if (Double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return value;
        return fallback;
    }

    static bool Flag(Dictionary<string, string> members, string key, bool fallback)
    {
        string raw = Member(members, key, null);
        if (raw == null) return fallback;
        raw = raw.Trim();
        if (String.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)) return true;
        if (String.Equals(raw, "false", StringComparison.OrdinalIgnoreCase)) return false;
        return fallback;
    }

    static string Choice(Dictionary<string, string> members, string key, string fallback, string[] allowed)
    {
        string raw = Member(members, key, null);
        if (raw == null) return fallback;
        raw = raw.Trim().ToLowerInvariant();
        for (int i = 0; i < allowed.Length; i++)
            if (allowed[i] == raw) return raw;
        return fallback;
    }

    static double Finite(double value, double fallback)
    {
        return (Double.IsNaN(value) || Double.IsInfinity(value)) ? fallback : value;
    }

    static double NonNegative(double value, double fallback)
    {
        value = Finite(value, fallback);
        return value < 0 ? 0 : value;
    }

    static double Positive(double value, double fallback)
    {
        value = Finite(value, fallback);
        return value <= 0 ? fallback : value;
    }
}

/// A minimal reader for the flat JSON object that launcher.json is. Only top-level
/// scalars are collected; nested objects and arrays are skipped, so an inner key can
/// never shadow a top-level one. Anything malformed simply ends the scan, which is
/// exactly the required behaviour: a broken config falls back to the defaults.
internal static class JsonScan
{
    public static Dictionary<string, string> ObjectMembers(string json)
    {
        Dictionary<string, string> members =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (json == null) return members;

        int i = json.IndexOf('{');
        if (i < 0) return members;
        i++;

        while (i < json.Length)
        {
            while (i < json.Length && (char.IsWhiteSpace(json[i]) || json[i] == ',')) i++;
            if (i >= json.Length || json[i] == '}') break;
            if (json[i] != '"') break;

            string key = ReadString(json, ref i);
            if (key == null) break;

            while (i < json.Length && (char.IsWhiteSpace(json[i]) || json[i] == ':')) i++;
            if (i >= json.Length) break;

            string value = ReadValue(json, ref i);
            if (value != null && !members.ContainsKey(key)) members.Add(key, value);
        }
        return members;
    }

    static string ReadValue(string json, ref int i)
    {
        char c = json[i];
        if (c == '"') return ReadString(json, ref i);
        if (c == '{' || c == '[') { SkipComposite(json, ref i); return null; }

        int start = i;
        while (i < json.Length && json[i] != ',' && json[i] != '}' && json[i] != ']'
               && !char.IsWhiteSpace(json[i])) i++;
        string raw = json.Substring(start, i - start).Trim();
        return raw.Length == 0 ? null : raw;
    }

    static string ReadString(string json, ref int i)
    {
        if (i >= json.Length || json[i] != '"') return null;
        i++;
        StringBuilder sb = new StringBuilder();
        while (i < json.Length)
        {
            char c = json[i++];
            if (c == '"') return sb.ToString();
            if (c != '\\') { sb.Append(c); continue; }
            if (i >= json.Length) break;

            char escape = json[i++];
            if (escape == 'n') sb.Append('\n');
            else if (escape == 't') sb.Append('\t');
            else if (escape == 'r') sb.Append('\r');
            else if (escape == 'b') sb.Append('\b');
            else if (escape == 'f') sb.Append('\f');
            else if (escape == '"') sb.Append('"');
            else if (escape == '\\') sb.Append('\\');
            else if (escape == '/') sb.Append('/');
            else if (escape == 'u')
            {
                int code;
                if (i + 4 <= json.Length && Int32.TryParse(json.Substring(i, 4), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out code))
                {
                    sb.Append((char)code);
                    i += 4;
                }
            }
            else
            {
                // Not a JSON escape. Windows paths are written with single
                // backslashes all the time, so keep the character verbatim instead
                // of eating it.
                sb.Append('\\');
                sb.Append(escape);
            }
        }
        return null;
    }

    static void SkipComposite(string json, ref int i)
    {
        int depth = 0;
        while (i < json.Length)
        {
            char c = json[i];
            if (c == '"') { SkipString(json, ref i); continue; }
            i++;
            if (c == '{' || c == '[') depth++;
            else if (c == '}' || c == ']')
            {
                depth--;
                if (depth <= 0) return;
            }
        }
    }

    static void SkipString(string json, ref int i)
    {
        i++;
        while (i < json.Length)
        {
            if (json[i] == '\\') { i += 2; continue; }
            if (json[i] == '"') { i++; return; }
            i++;
        }
    }
}

/// Finds the DSH executable when launcher.json does not name one. The order is the
/// documented one, and every hit is logged as "dsh-default" so a support log says
/// which copy is in use.
internal static class DshProbe
{
    public static string Find()
    {
        string env = Environment.GetEnvironmentVariable("DSH_EXE");
        if (!String.IsNullOrEmpty(env))
        {
            if (File.Exists(env))
            {
                Program.Log("dsh-default", "DSH_EXE=" + env);
                return env;
            }
            Program.Log("dsh-default", "DSH_EXE is set but not a file: " + env);
        }

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] candidates = new string[] {
            Combine(local, "Programs\\DeepSeek Harness\\DeepSeek Harness.exe"),
            Combine(programFiles, "DeepSeek Harness\\DeepSeek Harness.exe"),
            Combine(local, "DeepSeek Harness\\DeepSeek Harness.exe"),
            Combine(AppDomain.CurrentDomain.BaseDirectory, "DeepSeek Harness.exe"),
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            if (candidates[i] == null) continue;
            if (File.Exists(candidates[i]))
            {
                Program.Log("dsh-default", candidates[i]);
                return candidates[i];
            }
        }

        Program.Log("dsh-default", "nothing in the standard locations; the animation plays without a launch");
        return "";
    }

    static string Combine(string root, string tail)
    {
        if (String.IsNullOrEmpty(root)) return null;
        return Path.Combine(root, tail);
    }
}

internal static class Win32
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    const int SM_CXSCREEN = 0;
    const int GWL_STYLE = -16;
    const int WS_VISIBLE = 0x10000000;

    // The client rectangle - the content area - and never the outer window
    // rectangle: DSH paints its content below its title bar, so matching the frame
    // instead left the whole animation one title bar (about 32 px) out of register
    // when the dissolve revealed the app. The outer-rectangle API is deliberately
    // not even imported here, so it cannot be used by accident.
    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int index);

    /// Whether Win32 itself considers the window on screen. WPF's IsVisible can be
    /// true while the HWND never received WS_VISIBLE - that is exactly the state a
    /// window is left in when Show() runs synchronously inside the Startup handler
    /// with Visibility.Hidden already set, and it is invisible to the log otherwise.
    public static bool IsReallyVisible(IntPtr hWnd)
    {
        return (GetWindowLong(hWnd, GWL_STYLE) & WS_VISIBLE) != 0;
    }

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    /// Physical pixels per WPF device-independent unit. GetSystemMetrics reports
    /// pixels, SystemParameters reports DIPs, so their ratio is the scale factor
    /// without needing a live window to ask.
    public static double Scale()
    {
        try
        {
            int pixels = GetSystemMetrics(SM_CXSCREEN);
            double dips = SystemParameters.PrimaryScreenWidth;
            if (pixels > 0 && dips > 0.5) return pixels / dips;
        }
        catch { }
        return 1.0;
    }

    /// The main window of a running DSH, or IntPtr.Zero.
    public static IntPtr DshWindow()
    {
        try
        {
            string name = Program.DshProcessName;
            if (String.IsNullOrEmpty(name)) return IntPtr.Zero;

            Process[] processes = Process.GetProcessesByName(name);
            for (int i = 0; i < processes.Length; i++)
            {
                IntPtr handle = processes[i].MainWindowHandle;
                try { processes[i].Dispose(); } catch { }
                if (handle != IntPtr.Zero && IsWindowVisible(handle)) return handle;
            }
        }
        catch { }
        return IntPtr.Zero;
    }

    public static bool HasDshWindow()
    {
        return DshWindow() != IntPtr.Zero;
    }

    /// The DSH window's *content* rectangle in DIPs, or null when DSH has no laid
    /// out window yet.
    public static Rect? DshBounds()
    {
        IntPtr handle = DshWindow();
        if (handle == IntPtr.Zero) return null;

        RECT client;
        if (!GetClientRect(handle, out client)) return null;
        int width = client.Right - client.Left;
        int height = client.Bottom - client.Top;
        if (width < 200 || height < 200) return null;       // not laid out yet

        POINT origin;
        origin.X = 0;
        origin.Y = 0;
        if (!ClientToScreen(handle, ref origin)) return null;

        double scale = Scale();
        return new Rect(origin.X / scale, origin.Y / scale, width / scale, height / scale);
    }
}

/// Remembers the DSH rectangle between runs, so the launcher can appear immediately
/// instead of waiting for DSH to create its window first.
internal static class GeometryCache
{
    static string FilePath()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "geometry.json");
    }

    public static Rect? Load()
    {
        try
        {
            string path = FilePath();
            if (!File.Exists(path)) return null;

            Dictionary<string, string> members = JsonScan.ObjectMembers(File.ReadAllText(path, Encoding.UTF8));
            double left = Cached(members, "Left");
            double top = Cached(members, "Top");
            double width = Cached(members, "Width");
            double height = Cached(members, "Height");
            if (width < 200 || height < 200) return null;

            Program.Log("geometry-cache", String.Format(CultureInfo.InvariantCulture,
                "hit {0},{1} {2}x{3}", (int)left, (int)top, (int)width, (int)height));
            return new Rect(left, top, width, height);
        }
        catch (Exception ex)
        {
            Program.Log("geometry-cache", "read failed: " + ex.Message);
            return null;
        }
    }

    public static void Save(Rect rect)
    {
        try
        {
            File.WriteAllText(FilePath(), String.Format(CultureInfo.InvariantCulture,
                "{{\n  \"Left\": {0},\n  \"Top\": {1},\n  \"Width\": {2},\n  \"Height\": {3}\n}}\n",
                rect.Left, rect.Top, rect.Width, rect.Height));
            Program.Log("geometry-cache", String.Format(CultureInfo.InvariantCulture,
                "saved {0},{1} {2}x{3}", (int)rect.Left, (int)rect.Top, (int)rect.Width, (int)rect.Height));
        }
        catch (Exception ex)
        {
            Program.Log("geometry-cache", "write failed: " + ex.Message);
        }
    }

    static double Cached(Dictionary<string, string> members, string key)
    {
        string raw;
        if (members == null || !members.TryGetValue(key, out raw) || raw == null) return 0;
        double value;
        if (Double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return value;
        return 0;
    }
}

internal static class Geometry
{
    const double Aspect = 1920.0 / 1080.0;

    /// The smallest 16:9 box that *covers* `rect`, centred on it. Taking the largest
    /// box that fits instead leaves a strip of whatever lies underneath uncovered,
    /// because a non-16:9 rectangle cannot be matched exactly.
    public static Rect Cover16x9(Rect rect)
    {
        double width = rect.Width;
        double height = rect.Height;
        if (width <= 0 || height <= 0) return rect;
        if (width / height < Aspect) width = height * Aspect;   // too narrow -> widen
        else height = width / Aspect;                           // too short  -> heighten
        return new Rect(rect.Left + (rect.Width - width) / 2,
                        rect.Top + (rect.Height - height) / 2,
                        width, height);
    }

    public static Rect ScreenBox()
    {
        return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
    }
}

internal sealed class StartupWindow : Window
{
    readonly LauncherOptions opt;
    readonly string baseDir = AppDomain.CurrentDomain.BaseDirectory;
    readonly Stopwatch clock = Stopwatch.StartNew();

    readonly MediaElement film = new MediaElement();
    readonly MediaElement voice = new MediaElement();
    readonly PanelLayer layer = new PanelLayer();

    // Timers are fields so that Closed can stop every one of them.
    DispatcherTimer frameTick;
    DispatcherTimer watchdog;
    DispatcherTimer probe;
    DispatcherTimer holdTick;
    DispatcherTimer voiceTimeout;
    DispatcherTimer frontGuard;
    DispatcherTimer showTimer;      // one-shot: keeps Show() out of the Startup handler

    bool dshStarted;
    bool shown;
    bool showRequested;
    bool filmStarted;
    bool holdStarted;
    bool fadeRunning;
    bool closed;
    bool frameErrorLogged;
    bool visibilityMismatchLogged;  // the WPF/Win32 visibility check reports once
    double holdStartedAt;

    /// When the window actually went on screen, or -1 while it is still hidden.
    /// The watchdog measures its ceiling from here, not from Prepare(): the film
    /// cannot start before Show(), so a slow first run (waiting up to
    /// MatchWaitSeconds for DSH's window) must not eat into the ceiling that is
    /// meant to protect the playback.
    double shownAt = -1;

    /// `args` is part of the contract: Main resolves the diagnostic switches before
    /// the window exists, and anything the window needs is in Program.Options.
    public StartupWindow(string[] args)
    {
        opt = Program.Options == null ? new LauncherOptions() : Program.Options;

        Title = "DeepSeek Harness";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // Provisional geometry only, for the case where something shows the window
        // before Prepare() resolved the real one. It is never the rectangle the user
        // sees in a normal run: every Show() path applies the final one first.
        ApplyBounds(ScreenTarget());

        // Do not let the window appear yet. A plain WPF window without
        // AllowsTransparency cannot be hidden by lowering Opacity - it still paints
        // a black rectangle - so the reliable sequence is Visibility.Hidden plus a
        // late Show(), once the geometry is settled.
        Visibility = Visibility.Hidden;

        // The film and the panels must use one and the same rule, or the artwork
        // drifts off the picture it was measured against.
        string fit = opt.VideoFit;
        layer.Fill = fit == "fill";
        layer.StretchMode = fit == "aspect" ? "fit" : fit;
        film.Stretch = fit == "fill" ? Stretch.UniformToFill
                     : fit == "stretch" ? Stretch.Fill
                     : Stretch.Uniform;

        film.LoadedBehavior = MediaState.Manual;
        film.UnloadedBehavior = MediaState.Manual;
        film.IsMuted = false;
        film.MediaEnded += OnFilmEnded;
        film.MediaFailed += OnFilmFailed;

        voice.LoadedBehavior = MediaState.Manual;
        voice.UnloadedBehavior = MediaState.Manual;
        voice.Volume = 1.0;
        voice.MediaEnded += OnVoiceEnded;
        voice.MediaFailed += OnVoiceFailed;

        // Dismiss control, top right: the animation never has to be sat through.
        Button close = new Button();
        close.Content = "✕";
        close.FontSize = 20;
        close.Width = 44;
        close.Height = 34;
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Margin = new Thickness(0, 10, 14, 0);
        close.Foreground = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255));
        close.Background = new SolidColorBrush(Color.FromArgb(38, 0, 0, 0));
        close.BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
        close.BorderThickness = new Thickness(1);
        close.Cursor = Cursors.Hand;
        Panel.SetZIndex(close, 100);
        close.Click += OnCloseClick;

        Grid root = new Grid();
        root.Children.Add(film);
        Panel.SetZIndex(layer, 50);     // the panels draw over the film
        root.Children.Add(layer);
        root.Children.Add(close);      // and the close button over both
        Content = root;

        Loaded += OnLoaded;            // fires after the explicit Show()
        Closed += OnClosed;
        if (opt.AllowSkip) PreviewKeyDown += OnPreviewKeyDown;
    }

    /// Everything that has to happen before anything is on screen: start DSH, load
    /// the artwork, kick off the account read, start the frame clock and the safety
    /// watchdog - and only then work out the geometry and Show().
    ///
    /// There is deliberately no `return` anywhere in this method. The unconditional
    /// work sits above every branch, and the branching itself lives in helpers, so
    /// no geometry shortcut can skip the setup the way the original did.
    public void Prepare()
    {
        Program.Log("begin", String.Format(CultureInfo.InvariantCulture,
            "screen={0}x{1} bounds={2} fit={3} lead={4:F2} hold={5:F2} max={6:F2}",
            (int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight,
            opt.WindowBounds, opt.VideoFit, opt.FadeLeadSeconds, opt.ExtraHoldSeconds, opt.MaxHoldSeconds));

        // DSH boots while the film plays, behind this window. Never wait for it.
        dshStarted = Program.TryLaunchDsh();

        LoadTitleImage();
        StartAccountRead();
        StartFrameClock();
        StartWatchdog();

        if (opt.HasFixedBounds)
        {
            // A pinned rectangle: DSH is never asked anything and the animation is
            // on screen immediately, on every run.
            Rect pinned = Geometry.Cover16x9(new Rect(opt.WindowLeft, opt.WindowTop,
                                                       opt.WindowWidth, opt.WindowHeight));
            ApplyBounds(pinned);
            Program.Log("geometry", "pinned " + Describe(pinned));
            RequestShow();
        }
        else
        {
            // "auto" with a rectangle from an earlier run: show at once and let the
            // probe below correct it if DSH has moved since.
            Rect? cached = opt.MatchDshWindow ? GeometryCache.Load() : null;
            if (cached.HasValue)
            {
                ApplyBounds(cached.Value);
                Program.Log("geometry", "cache " + Describe(cached.Value));
                RequestShow();
            }

            if (opt.MatchDshWindow)
            {
                StartProbe();
            }
            else if (!shown)
            {
                UseScreenGeometry("matching disabled; using the screen");
            }
        }
    }

    void LoadTitleImage()
    {
        // The title artwork, faded in by Panels over the blank stretch before the
        // film's own baked title arrives at frame 930.
        try
        {
            string path = Path.Combine(baseDir, "assets", "title.png");
            if (!File.Exists(path))
            {
                Program.Log("title-missing", path);
                return;
            }
            BitmapImage image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            layer.Panels.TitleImage = image;
        }
        catch (Exception ex) { Program.Log("title-failed", ex.Message); }
    }

    void StartAccountRead()
    {
        // Read in the background: the panels show whatever is ready when they
        // appear and never wait on the network.
        AccountReader.LoadAsync(new DshAccount { locale = "ja-JP" }, delegate(DshAccount s)
        {
            // LoadAsync hands the result back on a background thread. The panels
            // belong to the UI thread, so the hand-over goes through the dispatcher.
            try { Dispatcher.BeginInvoke((Action)delegate { layer.Panels.State = s; }); }
            catch (Exception ex) { Program.Log("account", "hand-over failed: " + ex.Message); }
            Program.Log("account-ready", s.status + " / " + s.nameSource);
        });
    }

    void StartFrameClock()
    {
        // The panels live on the film's own timeline: f = 90 + seconds * 60.
        frameTick = new DispatcherTimer();
        frameTick.Interval = TimeSpan.FromMilliseconds(16);
        frameTick.Tick += OnFrameTick;
        frameTick.Start();
    }

    void StartWatchdog()
    {
        // Ceiling on the life of the window, measured from the moment it went on
        // screen. Whatever fails - the film, the voice, the account, DSH itself, a
        // window that never appears - this is what guarantees the launcher cannot
        // strand the user on a still frame.
        //
        // Invariant: the worst *healthy* playback after Show() is voice 2.5s + film
        // 17.43s + dissolve 0.7s = 20.63s, comfortably under the 25s default, so
        // this net can never cut off a normal run - it only rescues a stuck one.
        // Before Show() the origin falls back to 0, which keeps the original
        // absolute ceiling; the window is guaranteed to be on screen within
        // MatchWaitSeconds, and after that the 25s applies from there.
        watchdog = new DispatcherTimer();
        watchdog.Interval = TimeSpan.FromMilliseconds(500);
        watchdog.Tick += OnWatchdogTick;
        watchdog.Start();
    }

    void StartProbe()
    {
        Stopwatch waited = Stopwatch.StartNew();
        probe = new DispatcherTimer();
        probe.Interval = TimeSpan.FromMilliseconds(100);
        probe.Tick += delegate(object sender, EventArgs args)
        {
            Rect? bounds = Win32.DshBounds();
            if (bounds.HasValue)
            {
                Rect client = bounds.Value;
                Rect target = Geometry.Cover16x9(client);
                StopTimer(probe);
                probe = null;
                Program.Log("geometry", String.Format(CultureInfo.InvariantCulture,
                    "matched DSH client area {0},{1} {2}x{3} -> {4}",
                    (int)client.Left, (int)client.Top, (int)client.Width, (int)client.Height,
                    Describe(target)));
                GeometryCache.Save(target);
                if (shown)
                {
                    AdjustBounds(target);
                }
                else
                {
                    ApplyBounds(target);
                    RequestShow();
                }
            }
            else if (waited.Elapsed.TotalSeconds >= opt.MatchWaitSeconds)
            {
                StopTimer(probe);
                probe = null;
                if (!shown)
                {
                    UseScreenGeometry(String.Format(CultureInfo.InvariantCulture,
                        "no DSH window after {0:F1}s; using the screen", opt.MatchWaitSeconds));
                }
            }
        };
        probe.Start();
    }

    Rect ScreenTarget()
    {
        Rect box = Geometry.ScreenBox();
        return opt.VideoFit == "aspect" ? Geometry.Cover16x9(box) : box;
    }

    void UseScreenGeometry(string reason)
    {
        Rect box = ScreenTarget();
        ApplyBounds(box);
        Program.Log("geometry", reason + " " + Describe(box));
        RequestShow();
    }

    /// Arrange for the window to go on screen, one dispatcher tick from now.
    ///
    /// Calling Show() directly from inside the Application.Startup handler - which
    /// is where Prepare() runs - leaves the HWND created but *not* visible: the
    /// window carries a Visibility.Hidden that was set in the constructor, and the
    /// synchronous Show() from that context never turns it into WS_VISIBLE. WPF's own
    /// IsVisible still reports True, so launcher.log looks perfectly healthy while
    /// the screen stays empty: the cache path ("second run onwards") and the fixed
    /// path simply never appeared.
    ///
    /// Measured on this machine with the same binary and config:
    ///   Show() from the Startup handler  -> WS_VISIBLE never set, 62/62 samples,
    ///                                       screen pixels 100% the window behind it
    ///   Show() from a dispatcher tick    -> WS_VISIBLE set, black window on screen
    ///
    /// So every path now goes through here, and the actually-working mechanism - a
    /// tick, exactly like the geometry probe already used - is the only way on
    /// screen. ShowWindow() itself is unchanged and still runs once.
    void RequestShow()
    {
        if (showRequested || shown || closed) return;
        showRequested = true;
        showTimer = new DispatcherTimer();
        showTimer.Interval = TimeSpan.FromMilliseconds(1);
        showTimer.Tick += delegate(object sender, EventArgs e)
        {
            StopTimer(showTimer);
            showTimer = null;
            ShowWindow();
        };
        showTimer.Start();
    }

    /// Put the window on screen at its final rectangle, so the first frame the user
    /// sees is already in the right place. Showing first and moving afterwards is
    /// what produced a full-screen black flash.
    void ShowWindow()
    {
        if (shown) return;
        shown = true;
        shownAt = clock.Elapsed.TotalSeconds;
        Program.Log("show", Describe(new Rect(Left, Top, Width, Height)));
        Show();                     // Loaded -> Reveal()
        BringToFront();
        StartFrontGuard();
    }

    void BringToFront()
    {
        // Starting DSH activates its existing window, and that foreground change can
        // leave this window behind it. Take the front back explicitly.
        try
        {
            Topmost = true;
            Activate();
            Focus();
        }
        catch (Exception ex) { Program.Log("front-failed", ex.Message); }
    }

    void StartFrontGuard()
    {
        // DSH finishing its own startup is a second chance for it to jump in front.
        frontGuard = new DispatcherTimer();
        frontGuard.Interval = TimeSpan.FromMilliseconds(500);
        frontGuard.Tick += delegate(object sender, EventArgs args)
        {
            if (!IsVisible)
            {
                StopTimer(frontGuard);
                frontGuard = null;
                return;
            }
            try { if (!Topmost) Topmost = true; } catch { }

            // Diagnostic only, no recovery: report the disagreement once. A window
            // whose Show() ran inside the Startup handler satisfies WPF's IsVisible
            // while the HWND never got WS_VISIBLE, so the animation plays to an empty
            // screen and launcher.log still claims visible=True. Nothing here changes
            // the control flow - it just makes that state impossible to miss.
            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                if (!visibilityMismatchLogged && IsVisible && handle != IntPtr.Zero
                    && !Win32.IsReallyVisible(handle))
                {
                    visibilityMismatchLogged = true;
                    Program.Log("front-failed", "WPF reports visible but Win32 WS_VISIBLE is clear");
                }
            }
            catch { }
        };
        frontGuard.Start();
    }

    void ApplyBounds(Rect rect)
    {
        Left = rect.Left;
        Top = rect.Top;
        Width = rect.Width;
        Height = rect.Height;
    }

    /// Correct an already-visible window after the probe found DSH elsewhere.
    void AdjustBounds(Rect rect)
    {
        if (fadeRunning)
        {
            Program.Log("geometry", "late match ignored while handing over");
            return;
        }
        if (Math.Abs(Left - rect.Left) < 1.5 && Math.Abs(Top - rect.Top) < 1.5
            && Math.Abs(Width - rect.Width) < 1.5 && Math.Abs(Height - rect.Height) < 1.5)
        {
            Program.Log("geometry", "cache already matches the DSH window");
            return;
        }
        Program.Log("geometry", "adjusting to " + Describe(rect));
        ApplyBounds(rect);
        BringToFront();
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        Reveal();
    }

    /// Start the voice, then the film. Runs once the window is on screen.
    void Reveal()
    {
        Program.Log("window", String.Format(CultureInfo.InvariantCulture,
            "visible={0} opacity={1} topmost={2} state={3}", IsVisible, Opacity, Topmost, WindowState));
        Program.Log("reveal", Describe(new Rect(Left, Top, Width, Height)));

        string path = Path.Combine(baseDir, "assets", "link-start.wav");
        if (!File.Exists(path))
        {
            Program.Log("voice-missing", path);
            BeginFilm();
            return;
        }
        try
        {
            voice.Source = new Uri(path, UriKind.Absolute);
            voice.Play();
            // A clip that never reports MediaEnded must not stall the launch.
            voiceTimeout = new DispatcherTimer();
            voiceTimeout.Interval = TimeSpan.FromSeconds(2.5);
            voiceTimeout.Tick += OnVoiceTimeout;
            voiceTimeout.Start();
        }
        catch (Exception ex)
        {
            Program.Log("voice-failed", ex.Message);
            BeginFilm();
        }
    }

    void OnVoiceEnded(object sender, RoutedEventArgs e)
    {
        StopTimer(voiceTimeout);
        voiceTimeout = null;
        BeginFilm();
    }

    void OnVoiceFailed(object sender, ExceptionRoutedEventArgs e)
    {
        StopTimer(voiceTimeout);
        voiceTimeout = null;
        Program.Log("voice-failed", e.ErrorException == null ? "(no detail)" : e.ErrorException.Message);
        BeginFilm();
    }

    void OnVoiceTimeout(object sender, EventArgs e)
    {
        StopTimer(voiceTimeout);
        voiceTimeout = null;
        BeginFilm();
    }

    void BeginFilm()
    {
        if (filmStarted) return;
        filmStarted = true;
        Program.Log("intro", "t=" + Sec(clock.Elapsed.TotalSeconds));

        string path = Path.Combine(baseDir, "assets", "intro_dsh.mp4");
        if (!File.Exists(path))
        {
            Program.Log("film-missing", path);
            BeginHold();
            return;
        }
        try
        {
            film.Source = new Uri(path, UriKind.Absolute);
            film.Play();
        }
        catch (Exception ex)
        {
            Program.Log("film-failed", ex.Message);
            BeginHold();
        }
    }

    void OnFilmFailed(object sender, ExceptionRoutedEventArgs e)
    {
        Program.Log("film-failed", e.ErrorException == null ? "(no detail)" : e.ErrorException.Message);
        BeginHold();
    }

    void OnFilmEnded(object sender, RoutedEventArgs e)
    {
        // The dissolve normally begins before this fires; when it is already running
        // there is nothing left to hand over.
        if (fadeRunning) return;
        Program.Log("film-ended", "t=" + Sec(clock.Elapsed.TotalSeconds));
        BeginHold();
    }

    /// Hold whatever is on screen - the final blue frame, or nothing but the black
    /// background when the film could not play - until DSH is there, then dissolve.
    void BeginHold()
    {
        if (holdStarted) return;
        holdStarted = true;
        holdStartedAt = clock.Elapsed.TotalSeconds;

        try
        {
            Duration duration = film.NaturalDuration;
            if (duration.HasTimeSpan && duration.TimeSpan > TimeSpan.FromMilliseconds(80))
                film.Position = duration.TimeSpan - TimeSpan.FromMilliseconds(60);
            film.Pause();
        }
        catch (Exception ex) { Program.Log("hold-failed", "freeze: " + ex.Message); }

        holdTick = new DispatcherTimer();
        holdTick.Interval = TimeSpan.FromMilliseconds(100);
        holdTick.Tick += OnHoldTick;
        holdTick.Start();
    }

    void OnHoldTick(object sender, EventArgs e)
    {
        if (!ReadyToClose()) return;
        StopTimer(holdTick);
        holdTick = null;
        StartFade();
    }

    bool ReadyToClose()
    {
        double held = clock.Elapsed.TotalSeconds - holdStartedAt;

        // Never hand over to nothing: while DSH has no window at all, keep holding.
        // The ceiling still applies, and the watchdog enforces it from the other
        // side as well.
        if (dshStarted && !Win32.HasDshWindow() && held < opt.MaxHoldSeconds) return false;

        if (held >= opt.ExtraHoldSeconds) return true;
        return held >= opt.MaxHoldSeconds;
    }

    void OnFrameTick(object sender, EventArgs e)
    {
        try
        {
            double position = film.Position.TotalSeconds;
            layer.Panels.SourceFrame = 90 + position * 60;
            layer.InvalidateVisual();

            // The dissolve is triggered by the film's own position, so re-cutting
            // the clip moves the hand-over with it instead of leaving a stale timer
            // behind. NaturalDuration is only meaningful once the media is open.
            if (!fadeRunning && opt.FadeLeadSeconds > 0.01 && position > 0.5
                && film.NaturalDuration.HasTimeSpan
                && position >= film.NaturalDuration.TimeSpan.TotalSeconds - opt.FadeLeadSeconds)
            {
                StartFade();
            }
        }
        catch (Exception ex)
        {
            if (!frameErrorLogged)
            {
                frameErrorLogged = true;
                Program.Log("film-failed", "frame clock: " + ex.Message);
            }
        }
    }

    void OnWatchdogTick(object sender, EventArgs e)
    {
        if (fadeRunning || closed) return;
        // Measured from Show(): a healthy playback needs at most 20.63s from there,
        // so reaching 25s means something is stuck. While the window is still hidden
        // the origin is 0, i.e. the original absolute ceiling from Prepare().
        double origin = shownAt >= 0 ? shownAt : 0.0;
        if (clock.Elapsed.TotalSeconds - origin < opt.MaxHoldSeconds) return;
        StopTimer(watchdog);
        watchdog = null;
        Program.Log("hold-failed", String.Format(CultureInfo.InvariantCulture,
            "MaxHoldSeconds={0:F2}s since show (t={1:F2}) without a hand-over; closing",
            opt.MaxHoldSeconds, clock.Elapsed.TotalSeconds));
        StartFade();
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        SkipToEnd();
    }

    void OnCloseClick(object sender, RoutedEventArgs e)
    {
        SkipToEnd();
    }

    void OnFadeCompleted(object sender, EventArgs e)
    {
        Program.Log("fade-done", "t=" + Sec(clock.Elapsed.TotalSeconds));
        ShutDown();
    }

    /// Dissolve the window into whatever is beneath it, and close once it is
    /// invisible so nothing snaps.
    void StartFade()
    {
        if (fadeRunning || closed) return;
        fadeRunning = true;             // first, so a second animation cannot start
        StopTimer(watchdog);
        watchdog = null;
        StopTimer(holdTick);
        holdTick = null;

        if (opt.FadeOutSeconds <= 0.01)
        {
            Program.Log("fade-out", "0.00s at t=" + Sec(clock.Elapsed.TotalSeconds));
            Program.Log("fade-done", "t=" + Sec(clock.Elapsed.TotalSeconds));
            ShutDown();
            return;
        }
        try
        {
            // The animation wants a created window; it is only ever started after
            // Loaded, from the frame clock or from the hold, never before Show().
            DoubleAnimation fade = new DoubleAnimation(1.0, 0.0,
                new Duration(TimeSpan.FromSeconds(opt.FadeOutSeconds)));
            fade.Completed += OnFadeCompleted;
            BeginAnimation(Window.OpacityProperty, fade);
            Program.Log("fade-out", Sec(opt.FadeOutSeconds) + "s at t=" + Sec(clock.Elapsed.TotalSeconds));
        }
        catch (Exception ex)
        {
            Program.Log("fade-failed", ex.Message);
            ShutDown();
        }
    }

    void SkipToEnd()
    {
        Program.Log("skip", "t=" + Sec(clock.Elapsed.TotalSeconds));
        if (!dshStarted) dshStarted = Program.TryLaunchDsh();
        ShutDown();
    }

    void ShutDown()
    {
        if (closed) return;
        closed = true;
        try { Close(); }
        catch (Exception ex) { Program.Log("fatal", "close: " + ex.Message); }
    }

    void OnClosed(object sender, EventArgs e)
    {
        try { film.Stop(); } catch { }
        try { voice.Stop(); } catch { }

        StopTimer(frameTick);
        frameTick = null;
        StopTimer(watchdog);
        watchdog = null;
        StopTimer(probe);
        probe = null;
        StopTimer(holdTick);
        holdTick = null;
        StopTimer(voiceTimeout);
        voiceTimeout = null;
        StopTimer(frontGuard);
        frontGuard = null;
        StopTimer(showTimer);
        showTimer = null;
    }

    static void StopTimer(DispatcherTimer timer)
    {
        if (timer == null) return;
        try { timer.Stop(); } catch { }
    }

    /// Seconds, formatted the same way whatever the machine's regional settings are:
    /// the log is parsed mechanically.
    static string Sec(double seconds)
    {
        return seconds.ToString("F2", CultureInfo.InvariantCulture);
    }

    static string Describe(Rect rect)
    {
        return String.Format(CultureInfo.InvariantCulture, "{0},{1} {2}x{3}",
            (int)rect.Left, (int)rect.Top, (int)rect.Width, (int)rect.Height);
    }
}
