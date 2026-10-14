using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

// The account name the confirmation panel shows. Two hops, both verified live against
// the real account before this rewrite started:
//
//   1. %DSH_HOME%\.credentials.yaml (or %USERPROFILE%\.dsh\.credentials.yaml) holds a
//      grant under `deepseek-account-platform/default` carrying a `token` and an `issuer`.
//   2. GET {issuer}/auth-api/v0/users/current, authenticated with x-dsh-auth-token
//      (NOT a Bearer token) plus the five x-client-* headers, answers with the Platform
//      user; the display name sits at data.biz_data.id_profile.name.
//
// Three rules this file exists to keep:
//
//   * The film never waits on the network. LoadAsync pushes the work onto a background
//     thread and reports back through a callback; nothing here blocks the UI thread.
//   * No failure escapes to the caller. A missing file, an expired grant, an offline
//     machine or a malformed response all degrade into a DshAccount state the panel
//     already knows how to draw, so the launch always continues (SPEC 2.4).
//   * TLS 1.2 is switched on explicitly. .NET Framework 4.x still negotiates TLS 1.0 by
//     default and the Platform origin then refuses the request before it is sent, with an
//     error that reads like a local certificate problem (SPEC 5.4).

public static class AccountReader
{
    const int TimeoutMs = 5000;
    const string ClientVersion = "0.2.0-rc.2";
    const string DefaultIssuer = "https://platform.deepseek.com";
    const string GrantKey = "deepseek-account-platform/default:";

    //   3072 = Tls12, 768 = Tls11, 192 = Tls
    static AccountReader()
    {
        try
        {
            // Numeric literals on purpose: SecurityProtocolType.Tls12 is missing from
            // older mscorlib builds, and this single line is the whole reason the class
            // has a static constructor.
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)(3072 | 768 | 192);
        }
        catch (Exception ex)
        {
            // Losing this line means every request below fails with an opaque SSL error,
            // so the reason has to reach the log even though nothing can be done here.
            Note("tls", Detail(ex));
        }
    }

    /// Where DSH keeps its credential records: %DSH_HOME%\.credentials.yaml, else
    /// %USERPROFILE%\.dsh\.credentials.yaml. Never throws — `--check` prints this.
    public static string CredentialsPath()
    {
        try
        {
            string home = Environment.GetEnvironmentVariable("DSH_HOME");
            if (IsBlank(home))
            {
                home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (IsBlank(home)) home = Environment.GetEnvironmentVariable("USERPROFILE");
                if (IsBlank(home)) home = ".";
                home = Path.Combine(home, ".dsh");
            }
            return Path.Combine(home, ".credentials.yaml");
        }
        catch (Exception ex)
        {
            Note("credentials", "path: " + Detail(ex));
            return ".credentials.yaml";
        }
    }

    /// Pull `token` and `issuer` out of the deepseek-account-platform/default grant.
    /// A targeted line scan of a file DSH writes itself — not a YAML parser. Only the two
    /// scalars are read, and the block ends at the next key that is not nested deeper than
    /// the grant key itself (a column-0 top-level key, or a sibling record). An absent or
    /// unreadable file simply leaves both outputs null.
    public static void ReadGrant(out string token, out string issuer)
    {
        token = null;
        issuer = null;
        try
        {
            string path = CredentialsPath();
            if (!File.Exists(path)) return;

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            int start = GrantBlockStart(lines);
            if (start < 0) return;
            int blockIndent = Indent(lines[start]);

            for (int i = start + 1; i < lines.Length; i++)
            {
                string raw = lines[i];
                string text = raw.Trim();
                if (text.Length == 0) continue;
                if (text[0] == '#') continue;          // comment, at any indentation
                // The block is over at the next key that is not nested deeper than the
                // grant key: its own siblings (another record) and the column-0 top-level
                // keys both end it, and neither holds this grant's scalars.
                if (Indent(raw) <= blockIndent) break;

                if (text.StartsWith("token:", StringComparison.Ordinal))
                    token = Scalar(text.Substring(6));
                else if (text.StartsWith("issuer:", StringComparison.Ordinal))
                    issuer = Scalar(text.Substring(7));

                if (!IsBlank(token) && !IsBlank(issuer)) break;
            }
        }
        catch (Exception ex)
        {
            Note("credentials", Detail(ex));
        }
    }

    static int GrantBlockStart(string[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            string text = lines[i].Trim();
            if (text.Length == 0 || text[0] == '#') continue;
            if (text.StartsWith(GrantKey, StringComparison.Ordinal)) return i;
        }
        return -1;
    }

    static int Indent(string raw)
    {
        int count = 0;
        while (count < raw.Length && (raw[count] == ' ' || raw[count] == '\t')) count++;
        return count;
    }

    /// Drop one layer of matching quotes from a YAML scalar. DSH writes these plain, but
    /// quoted forms are legal and cost nothing to accept.
    static string Scalar(string value)
    {
        string text = value.Trim();
        if (text.Length >= 2)
        {
            char first = text[0];
            char last = text[text.Length - 1];
            if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                text = text.Substring(1, text.Length - 2);
        }
        return text;
    }

    /// Fill `state` in from the local grant and, when the Platform answers, its profile.
    /// Synchronous and bounded by TimeoutMs. Called directly by `--check`, because only a
    /// synchronous call exposes a TLS handshake failure.
    public static void Load(DshAccount state)
    {
        if (state == null)
        {
            Note("account", "Load called without a state object");
            return;
        }

        try
        {
            LoadCore(state);
        }
        catch (Exception ex)
        {
            // Nothing below is expected to throw; this net exists because an exception
            // crossing this boundary would land in the launch path.
            state.detail = Detail(ex);
            if (IsBlank(state.nameSource) || state.nameSource == "unavailable")
                state.nameSource = state.status == "signedIn" ? "profile-unavailable" : "no-local-grant";
            Note("account", "unexpected: " + state.detail);
        }
    }

    static void LoadCore(DshAccount state)
    {
        string token;
        string issuer;
        ReadGrant(out token, out issuer);

        if (IsBlank(token))
        {
            state.status = "signedOut";
            state.nameSource = "no-local-grant";
            Note("account", "no grant at " + CredentialsPath());
            return;
        }

        state.status = "signedIn";
        state.nameSource = "platform-profile";
        if (IsBlank(issuer)) issuer = DefaultIssuer;

        try
        {
            string name = FetchProfileName(issuer, token);
            if (!IsBlank(name))
            {
                state.displayName = name;
                Note("account", "name=" + name);
                return;
            }
            state.nameSource = "profile-empty";
            Note("account", "profile returned no name");
        }
        catch (Exception ex)
        {
            // Offline, expired grant, or the Platform refusing the call. The panel has a
            // state for exactly this, so the animation and the launch carry on.
            state.nameSource = "profile-unavailable";
            state.detail = Detail(ex);
            Note("account", "profile failed: " + state.detail);
        }
    }

    static string FetchProfileName(string issuer, string token)
    {
        string url = issuer.TrimEnd('/') + "/auth-api/v0/users/current";
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
        request.Method = "GET";
        request.Timeout = TimeoutMs;
        request.ReadWriteTimeout = TimeoutMs;
        request.AllowAutoRedirect = false;
        request.UserAgent = null;

        // Mirrors platformClientHeaders(): the bundle id is deliberately empty (a desktop
        // build is not a web bundle) and desktop-win replaces the web platform value.
        request.Headers["x-dsh-auth-token"] = token;
        request.Headers["x-client-bundle-id"] = "";
        request.Headers["x-client-platform"] = "desktop-win";
        request.Headers["x-client-version"] = ClientVersion;
        request.Headers["x-client-locale"] = "ja_JP";
        request.Headers["x-client-timezone-offset"] = TimezoneOffsetSeconds();

        using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
        using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
        {
            return NameFromJson(reader.ReadToEnd());
        }
    }

    static string TimezoneOffsetSeconds()
    {
        // The Platform wants the local UTC offset negated: a UTC+8 machine reports -28800.
        TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
        int seconds = -(int)offset.TotalSeconds;
        return seconds.ToString(CultureInfo.InvariantCulture);
    }

    static string NameFromJson(string body)
    {
        if (IsBlank(body)) return null;

        JavaScriptSerializer json = new JavaScriptSerializer();
        json.MaxJsonLength = 4 * 1024 * 1024;
        Dictionary<string, object> root = json.DeserializeObject(body) as Dictionary<string, object>;
        if (root == null) return null;

        Dictionary<string, object> data = Dig(root, "data");
        Dictionary<string, object> bizData = Dig(data, "biz_data");
        Dictionary<string, object> profile = Dig(bizData, "id_profile");
        if (profile == null) return null;

        object name = profile.ContainsKey("name") ? profile["name"] : null;
        return name as string;
    }

    static Dictionary<string, object> Dig(Dictionary<string, object> node, string key)
    {
        if (node == null || !node.ContainsKey(key)) return null;
        return node[key] as Dictionary<string, object>;
    }

    /// Run Load on a background thread and hand the filled state to `done`, which runs on
    /// that same background thread and may be null. The caller marshals back to the UI
    /// thread. Returns immediately and never throws.
    public static void LoadAsync(DshAccount state, Action<DshAccount> done)
    {
        string locale = (state == null || IsBlank(state.locale)) ? "ja-JP" : state.locale;
        try
        {
            Task.Run(delegate
            {
                DshAccount work = new DshAccount();
                work.locale = locale;
                Load(work);
                Report(done, work, "account");
            });
        }
        catch (Exception ex)
        {
            // Task.Run itself failing is not expected. Report it instead of letting it
            // escape, and still answer the caller so the panel does not sit on "unknown".
            string detail = Detail(ex);
            Note("account", "async start failed: " + detail);
            DshAccount fallback = state == null ? new DshAccount() : state;
            fallback.locale = locale;
            fallback.nameSource = "unavailable";
            fallback.detail = detail;
            Report(done, fallback, "account");
        }
    }

    static void Report(Action<DshAccount> done, DshAccount state, string evt)
    {
        if (done == null) return;
        try { done(state); }
        catch (Exception ex) { Note(evt, "callback failed: " + Detail(ex)); }
    }

    static void Note(string evt, string detail)
    {
        // The log path is documented as never throwing; keeping the guard local means a
        // logging bug can never turn into a failed account read.
        try { Program.Log(evt, detail); }
        catch (Exception) { }
    }

    /// Never hand back an empty diagnostic: a blank exception message is exactly what hid
    /// the TLS failure the first time round (SPEC 5.4).
    static string Detail(Exception ex)
    {
        if (ex == null) return "(no exception)";
        string message = ex.Message;
        if (!IsBlank(message)) return message;
        WebException web = ex as WebException;
        if (web != null) return "WebException " + web.Status.ToString();
        return ex.GetType().Name;
    }

    static bool IsBlank(string value)
    {
        return value == null || value.Trim().Length == 0;
    }
}
