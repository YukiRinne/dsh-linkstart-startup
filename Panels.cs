using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

// Vector overlay layer for the launcher: the three information panels the film leaves
// blank, plus the fade-in of the welcome title before the film's own baked title
// arrives at frame 930.
//
// Everything here draws in the 1920x1080 design coordinate system that the artwork was
// measured in. PanelLayer applies exactly the same framing rule the video element uses,
// so the panels land where they were measured for every window size.
//
// Reconstructed from the behavioural specification (§3.3-3.6) with the legacy vector
// layer (dsh-launcher/Panels.cs) as the behavioural baseline for the details the spec
// does not pin down: text sizes, inner strokes, the triangle, and the input-box
// keystroke animation.

public sealed class DshAccount
{
    public string displayName = "";
    public string locale = "ja-JP";
    public string status = "unknown";     // signedIn | signedOut | apiKey | unknown
    public string nameSource = "unavailable";
    public string detail = "";

    public static string Clean(string s, int max)
    {
        if (String.IsNullOrWhiteSpace(s)) return "";
        System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (!Char.IsControl(c)) sb.Append(c);
        }
        string clean = sb.ToString().Trim();
        if (clean.Length > max) clean = clean.Substring(0, max).TrimEnd() + "…";
        return clean;
    }

    public string Language()
    {
        string l = locale == null ? "" : locale.ToLowerInvariant();
        if (l.StartsWith("zh")) return "Chinese";
        if (l.StartsWith("ja")) return "Japanese";
        if (l.StartsWith("ko")) return "Korean";
        if (l.StartsWith("en")) return "English";
        if (l.StartsWith("fr")) return "French";
        if (l.StartsWith("de")) return "German";
        if (l.StartsWith("es")) return "Spanish";
        return String.IsNullOrWhiteSpace(l) ? "Unknown" : Clean(locale, 24);
    }

    // Wording carried over from the legacy state object, with the product name
    // substituted for DeepSeek Harness.
    public string Title()
    {
        if (status == "signedIn") return "ローカルアカウント";
        if (status == "signedOut") return "ローカル未ログイン";
        if (status == "apiKey") return "ローカルAPI設定";
        return "ローカル状態未確認";
    }

    public string[] Lines()
    {
        if (status == "signedIn") return new string[] {
            "端末に保存されたアカウントです。",
            "オンライン状態はDeepSeek Harnessで確認してください。" };
        if (status == "signedOut") return new string[] {
            "端末にログイン情報が見つかりません。",
            "DeepSeek Harnessを開いてログインしますか？" };
        if (status == "apiKey") return new string[] {
            "端末にAPI接続の設定があります。",
            "オンライン状態はDeepSeek Harnessで確認してください。" };
        return new string[] {
            "端末の保存状態を確認できませんでした。",
            "オンライン状態はDeepSeek Harnessで確認してください。" };
    }

    public string Name()
    {
        if (status == "signedOut") return "未ログイン";
        return String.IsNullOrWhiteSpace(displayName) ? "アカウント名未取得" : Clean(displayName, 40);
    }
}

public sealed class Panels
{
    // Source-video timeline frame number: f = 90 + seconds * 60.
    public double SourceFrame = 0;
    public DshAccount State = new DshAccount();
    public ImageSource TitleImage;        // may be null

    // ---- small numeric helpers ------------------------------------------------

    static double Clamp(double n) { return Math.Max(0, Math.Min(1, n)); }

    static double Smooth(double n) { n = Clamp(n); return n * n * (3 - 2 * n); }

    static Color Col(string hex)
    {
        return (Color)ColorConverter.ConvertFromString(hex);
    }

    static SolidColorBrush Brush(string hex)
    {
        return new SolidColorBrush(Col(hex));
    }

    static Color Mix(Color a, Color b, double t)
    {
        t = Clamp(t);
        return Color.FromRgb((byte)(a.R + (b.R - a.R) * t),
                             (byte)(a.G + (b.G - a.G) * t),
                             (byte)(a.B + (b.B - a.B) * t));
    }

    // ---- drawing primitives ---------------------------------------------------

    // The three-layer panel recipe from §3.5: drop shadow, body with edge stroke,
    // then a hairline highlight inset by half the edge width.
    static void Panel(DrawingContext d, Rect r, double radius, Brush fill, double edge)
    {
        Rect shadow = r;
        shadow.Offset(0, 2.0);
        d.DrawRoundedRectangle(null, new Pen(Brush("#55808080"), edge + 3), shadow, radius, radius);

        d.DrawRoundedRectangle(fill, new Pen(Brush("#929697"), edge), r, radius, radius);

        Rect inner = r;
        inner.Inflate(-edge / 2, -edge / 2);
        double innerRadius = Math.Max(0, radius - edge / 2);
        d.DrawRoundedRectangle(null, new Pen(Brush("#32FFFFFF"), 1.3), inner, innerRadius, innerRadius);
    }

    static FormattedText Text(string text, double size, bool japanese, Brush fill)
    {
        return new FormattedText(text, CultureInfo.GetCultureInfo(japanese ? "ja-JP" : "en-US"),
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily(japanese ? "Yu Gothic" : "Segoe UI Light"),
                         FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            size, fill, 1.0);
    }

    static void CenterText(DrawingContext d, string text, Rect rect, double size, bool japanese, double maxWidth)
    {
        FormattedText t = Text(text, size, japanese, Brush("#F7FAF9"));
        if (t.Width > maxWidth)
        {
            size *= maxWidth / t.Width;
            t = Text(text, size, japanese, Brush("#F7FAF9"));
        }
        d.DrawText(t, new Point(rect.X + (rect.Width - t.Width) / 2,
                                rect.Y + (rect.Height - t.Height) / 2 - 1));
    }

    static void Triangle(DrawingContext d)
    {
        StreamGeometry g = new StreamGeometry();
        using (StreamGeometryContext c = g.Open())
        {
            c.BeginFigure(new Point(958, 379), true, true);
            c.LineTo(new Point(997, 402), true, false);
            c.LineTo(new Point(958, 425), true, false);
        }
        g.Freeze();
        d.DrawGeometry(Brush("#F8FAF7"), null, g);
    }

    // ---- language panel: frames 545..621 -------------------------------------

    void DrawLanguage(DrawingContext d, double f)
    {
        if (f < 545 || f >= 621) return;
        double fade = 1 - Smooth((f - 613) / 5);
        if (fade <= 0) return;
        d.PushOpacity(fade);

        double grow = Smooth((f - 545) / 10);
        d.PushOpacity(Math.Max(.08, grow));
        d.PushTransform(new ScaleTransform(1, Math.Max(.018, grow), 636.5, 370));
        Panel(d, new Rect(334, 315, 605, 110), 30, Brush("#0482FD"), 4);
        CenterText(d, "Language", new Rect(354, 319, 565, 100), 76, false, 540);
        d.Pop();
        d.Pop();

        if (f >= 570)
        {
            double open = Smooth((f - 570) / 12);
            double select = Smooth((f - 585) / 5);
            d.PushOpacity(Math.Max(.05, open));
            d.PushTransform(new ScaleTransform(1, Math.Max(.018, open), 1156.5, 402));
            Panel(d, new Rect(854, 347, 605, 110), 30,
                  new SolidColorBrush(Mix(Col("#0086EE"), Col("#00D3D9"), select)), 4);
            Triangle(d);
            CenterText(d, State.Language(), new Rect(1020, 352, 403, 100), 72, false, 390);
            d.Pop();
            d.Pop();
        }

        d.Pop();
    }

    // ---- login panel: frames 625..713 ----------------------------------------

    static readonly int[] AccountKeyFrames = { 640, 645, 650, 655, 660, 663, 665, 668, 670 };
    static readonly int[] PasswordKeyFrames = { 683, 688, 690, 693, 695, 700 };

    static void Label(DrawingContext d, string s, double x, double y, double size, double width)
    {
        FormattedText text = new FormattedText(s, CultureInfo.GetCultureInfo("en-US"),
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            size, Brush("#F7FBFA"), 1.0);
        // Stretched horizontally to the measured label width rather than scaled as a block.
        d.PushTransform(new ScaleTransform(width / text.Width, 1, x, y));
        d.DrawText(text, new Point(x, y));
        d.Pop();
    }

    // Typed-character damping: each key frame pops in and settles.
    static void Input(DrawingContext d, double y, int[] events, double f)
    {
        Rect box = new Rect(983, y, 374, 48);
        d.DrawRectangle(Brush("#F9FBFC"), new Pen(Brush("#CCBCC9CD"), 1.5), box);
        d.DrawLine(new Pen(Brush("#5790999E"), 1.4), new Point(984, y + 1), new Point(1356, y + 1));
        d.DrawLine(new Pen(Brush("#FFE8FFFF"), 1.3), new Point(984, y + 47), new Point(1356, y + 47));

        for (int i = 0; i < events.Length; i++)
        {
            if (f < events[i]) break;
            double alpha = .55 + .45 * Smooth((f - events[i]) / .65);
            d.PushOpacity(alpha);

            Point c = new Point(1000.5 + i * 26.75, y + 25.5);
            Pen p = new Pen(Brush("#353937"), 3.0);
            p.StartLineCap = PenLineCap.Round;
            p.EndLineCap = PenLineCap.Round;
            for (int j = 0; j < 3; j++)
            {
                double angle = Math.PI / 2 + j * Math.PI / 3;
                double dx = 9.3 * Math.Cos(angle);
                double dy = 9.3 * Math.Sin(angle);
                d.DrawLine(p, new Point(c.X - dx, c.Y - dy), new Point(c.X + dx, c.Y + dy));
            }
            d.Pop();
        }
    }

    void DrawLogin(DrawingContext d, double f)
    {
        if (f < 625 || f >= 713) return;
        double opacity = Smooth((f - 624) / 6) * (1 - Smooth((f - 702) / 11));
        if (opacity <= 0) return;
        d.PushOpacity(opacity);

        Rect panel = new Rect(483, 361, 954, 361);
        Rect shadow = new Rect(483, 363, 954, 361);
        d.DrawRoundedRectangle(null, new Pen(Brush("#44808788"), 10), shadow, 43, 43);
        d.DrawRoundedRectangle(new LinearGradientBrush(Col("#0784B8"), Col("#037FB3"), 90),
                               new Pen(Brush("#939A9D"), 7), panel, 43, 43);
        d.DrawRoundedRectangle(null, new Pen(Brush("#44EBFCFF"), 1.4), new Rect(487, 365, 946, 353), 39, 39);

        Label(d, "Log in_::", 561, 393, 76, 314);
        Label(d, ":account", 978, 432, 48, 204);
        Label(d, ":password", 978, 556, 48, 242);
        Input(d, 491, AccountKeyFrames, f);
        Input(d, 615, PasswordKeyFrames, f);

        d.Pop();
    }

    // ---- confirmation panel: frames 713..823 ---------------------------------

    void DrawConfirm(DrawingContext d, double f)
    {
        if (f < 713 || f >= 823) return;
        double opacity = Smooth((f - 713) / 5) * (1 - Smooth((f - 805) / 15));
        if (opacity <= 0) return;
        d.PushOpacity(opacity);

        Panel(d, new Rect(616, 219, 693, 103), 39, Brush("#0083B7"), 7);
        Panel(d, new Rect(310, 347, 1306, 557), 52,
              new LinearGradientBrush(Col("#0083B7"), Col("#007FB3"), 90), 7);

        CenterText(d, State.Title(), new Rect(645, 221, 635, 97), 61, true, 625);

        string[] lines = State.Lines();
        double size = 56;
        foreach (string line in lines)
        {
            FormattedText t = Text(line, size, true, Brush("#F7FAF9"));
            if (t.Width > 984) size *= 984 / t.Width;
        }
        for (int li = 0; li < lines.Length; li++)
        {
            FormattedText line = Text(lines[li], size, true, Brush("#F7FAF9"));
            d.DrawText(line, new Point(473, 383 + li * 76 + (75 - line.Height) / 2 - 1));
        }

        d.DrawRectangle(Brush("#00CBDE"), null, new Rect(737, 627, 447, 66));
        CenterText(d, State.Name(), new Rect(749, 627, 423, 66), 54, false, 415);

        // YES lights up as the confirmation is accepted; NO stays in its resting tint.
        double selected = Smooth((f - 753) / 2);
        d.DrawRoundedRectangle(new SolidColorBrush(Mix(Col("#05ACCD"), Col("#00EBE7"), selected)),
                               null, new Rect(614, 788, 201, 53), 26.5, 26.5);
        d.DrawRoundedRectangle(Brush("#05ACCD"), null, new Rect(1105, 788, 204, 53), 26.5, 26.5);
        CenterText(d, "YES", new Rect(614, 784, 201, 61), 55, false, 185);
        CenterText(d, "NO", new Rect(1105, 784, 204, 61), 55, false, 190);

        d.Pop();
    }

    // ---- welcome title fade-in: frame 853 ramp, hand-off at 930 ---------------
    // Sampled curve from the original timeline, copied verbatim (§3.4).
    static readonly double[] FadeFrames = { 852, 853, 855, 858, 860, 863, 865, 868, 870, 873, 875, 878, 881, 883, 885, 942 };
    static readonly double[] FadeOpacity = { 0, .035967, .074795, .115770, .165673, .244212, .325668, .400337,
                                              .485093, .572516, .664800, .752496, .846940, .922395, 1, 1 };

    public static double WelcomeOpacity(double f)
    {
        if (f < FadeFrames[1]) return 0;
        for (int i = 1; i < FadeFrames.Length; i++)
        {
            if (f < FadeFrames[i])
            {
                double t = (f - FadeFrames[i - 1]) / (FadeFrames[i] - FadeFrames[i - 1]);
                return FadeOpacity[i - 1] + (FadeOpacity[i] - FadeOpacity[i - 1]) * t;
            }
        }
        return 1;
    }

    // The film's own title appears fully-formed within one frame at 930, at the same
    // position, so handing over while our copy is at full opacity is seamless.
    const double TitleHandoff = 930;

    public void Render(DrawingContext d)
    {
        double f = SourceFrame;

        if (TitleImage != null && f < TitleHandoff)
        {
            double op = WelcomeOpacity(f);
            if (op > 0)
            {
                d.PushOpacity(op);
                d.DrawImage(TitleImage, new Rect(0, 0, 1920, 1080));
                d.Pop();
            }
        }

        // Mutually exclusive frame windows, so draw order does not matter.
        DrawLanguage(d, f);
        DrawLogin(d, f);
        DrawConfirm(d, f);
    }
}

public sealed class PanelLayer : FrameworkElement
{
    public Panels Panels = new Panels();
    // Mirrors the video's crop behaviour: true = UniformToFill, false = Uniform.
    public bool Fill = false;
    // "fit" | "fill" | "stretch" - must match the video's Stretch or the panels
    // will not sit where they were measured.
    public string StretchMode = "fit";

    public PanelLayer()
    {
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext d)
    {
        base.OnRender(d);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        if (StretchMode == "stretch")
        {
            // Non-uniform: one scale per axis, matching a video Stretch of Fill.
            d.PushTransform(new ScaleTransform(ActualWidth / 1920.0, ActualHeight / 1080.0));
            Panels.Render(d);
            d.Pop();
            return;
        }

        // Same framing rule the video applies to a 1920x1080 source.
        double s = Fill ? Math.Max(ActualWidth / 1920.0, ActualHeight / 1080.0)
                        : Math.Min(ActualWidth / 1920.0, ActualHeight / 1080.0);
        double dx = (ActualWidth - 1920 * s) / 2;
        double dy = (ActualHeight - 1080 * s) / 2;

        d.PushTransform(new TranslateTransform(dx, dy));
        d.PushTransform(new ScaleTransform(s, s));
        Panels.Render(d);
        d.Pop();
        d.Pop();
    }
}
