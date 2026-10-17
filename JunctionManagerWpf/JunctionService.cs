using System.Diagnostics;
using System.IO;

namespace JunctionManager;

public static class JunctionService
{
    const string Everyone = "*S-1-1-0";

    public static int Run(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args)
        { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
        p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    public static (int ExitCode, string Output) RunWithOutput(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args)
        { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        string combined = $"{stderr}\n{stdout}".Trim();
        return (p.ExitCode, combined);
    }

    static bool TryAttrs(string path, out FileAttributes a)
    {
        try { a = File.GetAttributes(path); return true; } catch { a = 0; return false; }
    }

    static string Norm(string? p)
    {
        if (string.IsNullOrEmpty(p)) return "";
        if (p.StartsWith(@"\??\")) p = p[4..];
        return Path.GetFullPath(p).TrimEnd('\\').ToLowerInvariant();
    }

    public static string GetStatus(JunctionEntry e)
    {
        if (!TryAttrs(e.Source, out var a))
            return Directory.Exists(e.Target) ? "Link missing" : "Target missing";
        if (!a.HasFlag(FileAttributes.ReparsePoint)) return "Unlinked";   // real folder, e.g. after an update
        var actual = new DirectoryInfo(e.Source).LinkTarget;
        if (Norm(actual) != Norm(e.Target)) return "Wrong target";
        return Directory.Exists(e.Target) ? "OK" : "Target missing";
    }

    /// Creates or repairs the junction. Returns an error message, or null on success.
    public static string? Link(JunctionEntry e, IProgress<CopyProgress>? progress = null)
    {
        try
        {
            var src = e.Source.Trim().TrimEnd('\\');
            var tgt = e.Target.Trim().TrimEnd('\\');
            if (Norm(src) == Norm(tgt)) return "Source and target are the same.";
            if (Norm(tgt).StartsWith(Norm(src) + "\\")) return "Target cannot be inside the source.";

            if (TryAttrs(src, out var a))
            {
                if (a.HasFlag(FileAttributes.ReparsePoint))
                {
                    if (GetStatus(e) == "OK") { ApplyFlags(e); return null; }
                    Unlink(e);                                   // wrong/broken link: drop it and relink
                }
                else
                {
                    // real folder at source: merge it into target (newer files win), then remove it
                    Directory.CreateDirectory(tgt);
                    var rc = RobocopyMove(src, tgt, progress);
                    if (rc >= 8) return $"Copy failed (robocopy code {rc}). Close the app and try again.";
                    if (Directory.Exists(src))
                        try { Directory.Delete(src, true); }
                        catch { return "Source still has files in use. Close the app and retry."; }
                }
            }
            if (!Directory.Exists(tgt)) return "Target folder does not exist.";

            progress?.Report(new CopyProgress(100, "Creating junction...", true));
            var parent = Path.GetDirectoryName(src);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            var (rcLink, outLink) = RunWithOutput("cmd", $"/c mklink /J \"{src}\" \"{tgt}\"");
            if (rcLink != 0) return string.IsNullOrWhiteSpace(outLink) ? "mklink failed." : outLink;
            ApplyFlags(e);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    static int RobocopyMove(string src, string tgt, IProgress<CopyProgress>? progress)
    {
        src = src.Trim().TrimEnd('\\');
        tgt = tgt.Trim().TrimEnd('\\');
        var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        try
        {
            foreach (var f in new DirectoryInfo(src).EnumerateFiles("*", SearchOption.AllDirectories))
            { sizes[f.FullName] = f.Length; total += f.Length; }
        }
        catch { }

        var psi = new ProcessStartInfo("robocopy",
            $"\"{src}\" \"{tgt}\" /E /MOVE /IS /IT /R:1 /W:1 /NP /NC /NS /NDL /NJH /NJS")
        { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
        using var p = Process.Start(psi)!;

        long done = 0; int n = 0;
        var clock = Stopwatch.StartNew(); long lastReport = 0;
        string? line;
        while ((line = p.StandardOutput.ReadLine()) != null)
        {
            var path = line.Trim();
            if (path.Length == 0) continue;
            n++;
            if (sizes.TryGetValue(path, out var sz)) done += sz;
            if (clock.ElapsedMilliseconds - lastReport < 100) continue;
            lastReport = clock.ElapsedMilliseconds;

            double secs = Math.Max(clock.Elapsed.TotalSeconds, 0.1);
            double rate = done / secs;
            var eta = rate > 0 ? TimeSpan.FromSeconds((total - done) / rate) : TimeSpan.Zero;
            double pct = total > 0 ? 100.0 * done / total : 100.0 * n / Math.Max(sizes.Count, 1);
            progress?.Report(new CopyProgress(pct,
                $"{n:N0} / {sizes.Count:N0} files  •  {done / 1048576:N0} / {total / 1048576:N0} MB  •  {rate / 1048576:N0} MB/s  •  {eta:m\\:ss} left"));
        }
        p.WaitForExit();
        progress?.Report(new CopyProgress(100, "Files moved"));
        return p.ExitCode;
    }

    public static void ApplyFlags(JunctionEntry e)
    {
        var tgt = e.Target.Trim().TrimEnd('\\');
        if (!string.IsNullOrWhiteSpace(tgt) && Directory.Exists(tgt))
        {
            Run("attrib", $"{(e.Hidden ? "+h" : "-h")} \"{tgt}\"");
            if (e.Protected) Run("icacls", $"\"{tgt}\" /deny {Everyone}:(DE)");
            else Run("icacls", $"\"{tgt}\" /remove:d {Everyone}");
        }

        // Clean up any old flags previously set on the junction source link
        var src = e.Source.Trim().TrimEnd('\\');
        if (!string.IsNullOrWhiteSpace(src) && Directory.Exists(src))
        {
            Run("attrib", $"-h /L \"{src}\"");
            Run("icacls", $"\"{src}\" /remove:d {Everyone} /L");
        }
    }

    /// Removes only the link; target data is untouched.
    public static string? Unlink(JunctionEntry e)
    {
        try
        {
            var src = e.Source.Trim().TrimEnd('\\');
            if (TryAttrs(src, out var a) && a.HasFlag(FileAttributes.ReparsePoint))
            {
                Run("icacls", $"\"{src}\" /remove:d {Everyone} /L");
                Run("attrib", $"-h /L \"{src}\"");
                Directory.Delete(src);
            }
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }
}

public record CopyProgress(double Percent, string Text, bool Indeterminate = false);
