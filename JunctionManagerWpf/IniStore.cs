using System.IO;
using System.Text;

namespace JunctionManager;

public class AppConfig
{
    public string Theme { get; set; } = "System";
    public bool MinimizeToTray { get; set; } = false;
}

public static class IniStore
{
    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JunctionManager");
    public static readonly string FilePath = Path.Combine(Dir, "junctions.ini");

    public static AppConfig Config { get; set; } = new();

    public static List<JunctionEntry> Load()
    {
        var list = new List<JunctionEntry>();
        if (!File.Exists(FilePath)) return list;
        JunctionEntry? cur = null;
        bool inSettings = false;

        foreach (var raw in File.ReadAllLines(FilePath))
        {
            var line = raw.Trim();
            if (line.StartsWith("["))
            {
                if (line.Equals("[Settings]", StringComparison.OrdinalIgnoreCase))
                {
                    cur = null;
                    inSettings = true;
                    continue;
                }
                cur = new JunctionEntry();
                list.Add(cur);
                inSettings = false;
                continue;
            }

            var i = line.IndexOf('=');
            if (i < 1) continue;
            var key = line[..i].Trim().ToLowerInvariant();
            var val = line[(i + 1)..].Trim();

            if (inSettings)
            {
                switch (key)
                {
                    case "theme": Config.Theme = val; break;
                    case "minimizetotray": Config.MinimizeToTray = val != "0"; break;
                }
                continue;
            }

            if (cur == null) continue;
            switch (key)
            {
                case "name": cur.Name = val; break;
                case "source": cur.Source = val; break;
                case "target": cur.Target = val; break;
                case "hidden": cur.Hidden = val == "1"; break;
                case "protected": cur.Protected = val == "1"; break;
                case "watch": cur.Watch = val == "1"; break;
            }
        }
        return list.Where(e => e.Source != "" && e.Target != "").ToList();
    }

    public static void Save(IEnumerable<JunctionEntry> items)
    {
        Directory.CreateDirectory(Dir);
        if (File.Exists(FilePath)) File.SetAttributes(FilePath, FileAttributes.Normal);
        var sb = new StringBuilder();

        sb.AppendLine("[Settings]");
        sb.AppendLine($"Theme={Config.Theme}");
        sb.AppendLine($"MinimizeToTray={(Config.MinimizeToTray ? 1 : 0)}");
        sb.AppendLine();

        int n = 1;
        foreach (var e in items)
        {
            sb.AppendLine($"[Junction{n++}]");
            sb.AppendLine($"Name={e.Name}");
            sb.AppendLine($"Source={e.Source}");
            sb.AppendLine($"Target={e.Target}");
            sb.AppendLine($"Hidden={(e.Hidden ? 1 : 0)}");
            sb.AppendLine($"Protected={(e.Protected ? 1 : 0)}");
            sb.AppendLine($"Watch={(e.Watch ? 1 : 0)}");
            sb.AppendLine();
        }
        File.WriteAllText(FilePath, sb.ToString());
        File.SetAttributes(FilePath, FileAttributes.Hidden);
    }
}
