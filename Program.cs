// Prompt Train Script - Kitsune Trains converter.
// Turns the user's OWN download of "Kitsune Trains 7" (Foxunitone, gta5-mods) into FiveM train packs
// for prompt_train_script. No GTA install, OpenIV or CodeWalker needed: the mod's dlc.rpf is unencrypted.
//   Double-click: finds the zip next to the exe or in Downloads.   Drag & drop: the zip (or the unzipped folder).
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using CodeWalker.GameFiles;

static class Program
{
    const int MaxVehicles = 12;     // loco + cars per train; prompt_train_script tracks up to 15
    const int FirstIndexGuess = 31; // vanilla configs + Polar + bullet; the script scans +-40 around it

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.Title = "Prompt Train Script - Kitsune Trains converter";
        try { return Run(args); }
        catch (Exception ex)
        {
            Say("");
            Say("Something went wrong: " + ex.Message);
            Say("Send a screenshot of this window to Prompt Studio support.");
            Say(ex.ToString());
            Pause();
            return 1;
        }
    }

    record Car(string Model, string Vert, bool Flip, int Repeat);
    record Config(string Name, string Gap, List<Car> Cars);

    static int Run(string[] args)
    {
        Say("=== Prompt Train Script - Kitsune Trains converter ===");
        Say("Makes FiveM train packs from your own copy of Kitsune Trains 7 (by Foxunitone, gta5-mods.com).");
        Say("");

        // 1. find the mod
        var exeDir = AppContext.BaseDirectory;
        var source = FindSource(args, exeDir);
        if (source == null)
        {
            Say("Could not find Kitsune Trains 7.");
            Say("1. Download Kitsune Trains 7 from gta5-mods.com (you get a .zip file).");
            Say("2. Put the .zip next to this program, or drag the .zip onto this program.");
            Pause();
            return 1;
        }
        Say("Found: " + source);

        var work = Path.Combine(Path.GetTempPath(), "PromptTrainConverter");
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);

        string rpfPath, trainsXmlPath, kitsuneCfgPath;
        if (File.Exists(source) && source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(source);
            rpfPath = Unzip(zip, "kitsunetrains/dlc.rpf", work, "dlc.rpf", true);
            trainsXmlPath = Unzip(zip, "track data/trains.xml", work, "trains.xml", false);
            kitsuneCfgPath = Unzip(zip, "kitsunetrainconfigs.xml", work, "KitsuneTrainConfigs.xml", false);
        }
        else
        {
            rpfPath = FindFile(source, @"kitsunetrains\dlc.rpf");
            trainsXmlPath = FindFile(source, @"track data\trains.xml");
            kitsuneCfgPath = FindFile(source, "kitsunetrainconfigs.xml");
        }
        if (rpfPath == null || trainsXmlPath == null)
            throw new Exception("this does not look like Kitsune Trains 7 (kitsunetrains\\dlc.rpf or TRACK DATA\\trains.xml is missing)");

        // 2. read the mod
        Say("Reading the train models...");
        var rpf = new RpfFile(rpfPath, "dlc.rpf");
        rpf.ScanStructure(null, null);
        var files = new Dictionary<string, (RpfFile Rpf, RpfFileEntry Entry)>(StringComparer.OrdinalIgnoreCase);
        var paths = new Dictionary<string, (RpfFile Rpf, RpfFileEntry Entry)>(StringComparer.OrdinalIgnoreCase);
        foreach (var archive in Walk(rpf))
            foreach (var e in archive.AllEntries ?? new List<RpfEntry>())
                if (e is RpfFileEntry fe && !fe.NameLower.EndsWith(".rpf"))
                {
                    files.TryAdd(fe.Name, (archive, fe));
                    paths[fe.Path] = (archive, fe);
                }

        string Text(string pathEnd)
        {
            var hit = paths.FirstOrDefault(p => p.Key.EndsWith(pathEnd, StringComparison.OrdinalIgnoreCase));
            if (hit.Value.Entry == null) return null;
            return Encoding.UTF8.GetString(hit.Value.Rpf.ExtractFile(hit.Value.Entry));
        }

        var vehiclesMeta = Text(@"common\data\levels\gta5\vehicles.meta") ?? throw new Exception("vehicles.meta not found in the mod");
        var carcols = Text(@"common\data\ai\carcols.meta");
        var carvariations = Text(@"common\data\ai\carvariations.meta");

        var items = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var gameNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(vehiclesMeta, @"<Item>\s*<modelName>[\s\S]*?</Item>\s*(?=<Item>\s*<modelName>|</InitDatas>)"))
        {
            var model = Regex.Match(m.Value, @"<modelName>([^<]+)</modelName>").Groups[1].Value;
            items[model] = m.Value;
            gameNames[model] = Regex.Match(m.Value, @"<gameName>([^<]*)</gameName>").Groups[1].Value;
        }

        var labels = new Dictionary<uint, string>();
        var gxt = paths.FirstOrDefault(p => p.Key.EndsWith(@"americandlc.rpf\global.gxt2", StringComparison.OrdinalIgnoreCase));
        if (gxt.Value.Entry != null)
        {
            var g = new Gxt2File();
            g.Load(gxt.Value.Rpf.ExtractFile(gxt.Value.Entry), gxt.Value.Entry);
            foreach (var t in g.TextEntries ?? Array.Empty<Gxt2Entry>()) labels[t.Hash] = t.Text;
        }

        var scripted = new HashSet<uint>();
        if (kitsuneCfgPath != null)
            foreach (Match m in Regex.Matches(File.ReadAllText(kitsuneCfgPath), @"<ModelHash>(-?\d+)</ModelHash>"))
                scripted.Add(unchecked((uint)long.Parse(m.Groups[1].Value)));

        var configs = ParseConfigs(File.ReadAllText(trainsXmlPath))
            .Where(c => c.Cars.All(car => files.ContainsKey(car.Model + ".yft") && items.ContainsKey(car.Model)))
            .ToList();
        if (configs.Count == 0) throw new Exception("no trains found in the mod's TRACK DATA\\trains.xml");

        string Label(Config c)
        {
            var loco = c.Cars[0].Model;
            var gn = gameNames.TryGetValue(loco, out var n) ? n : loco;
            return labels.TryGetValue(Joaat(gn), out var l) && !string.IsNullOrWhiteSpace(l) ? l : gn;
        }

        // 3. pick
        Say("");
        Say($"{configs.Count} trains available:");
        for (int i = 0; i < configs.Count; i++)
        {
            var c = configs[i];
            var count = c.Cars.Sum(x => x.Repeat);
            var flag = scripted.Contains(Joaat(c.Cars[0].Model)) ? " *" : "";
            Say($"  {i + 1,3}. {Label(c)}  ({count} vehicles){flag}");
        }
        Say("");
        Say("  * = its wheels are animated by the mod's singleplayer script; in FiveM they may not turn.");
        Say($"  Trains longer than {MaxVehicles} vehicles are shortened.");
        Say("");

        List<Config> picked = null;
        while (picked == null)
        {
            Console.Write("Type the numbers you want (e.g. 3, 12, 20-25) or 'all', then Enter: ");
            var line = Console.ReadLine();
            if (line == null) { Say(""); Say("Nothing picked - closing."); return 1; }
            picked = ParsePick(line, configs);
            if (picked == null) Say("Not understood, try again.");
        }

        // 4. build
        var outRoot = Path.Combine(File.Exists(source) ? Path.GetDirectoryName(source) : source, "[kt_trains]");
        if (Directory.Exists(outRoot))
        {
            var old = outRoot + "_old_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.Move(outRoot, old);
            Say("Your previous [kt_trains] folder was renamed to " + Path.GetFileName(old));
        }

        var trains = picked.Select(c => (Config: c, Cars: Shorten(c.Cars))).ToList();
        var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in trains)
            foreach (var m in t.Cars.Select(x => x.Model).Distinct(StringComparer.OrdinalIgnoreCase))
                usage[m] = usage.GetValueOrDefault(m) + 1;

        var neededModels = trains.SelectMany(t => t.Cars.Select(x => x.Model)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var modelsDir = Path.Combine(outRoot, "kt_models");
        Directory.CreateDirectory(Path.Combine(modelsDir, "stream"));
        Directory.CreateDirectory(Path.Combine(modelsDir, "data"));
        Say("");
        Say($"Converting {neededModels.Count} models...");
        var toStream = neededModels.SelectMany(m => new[] { m + ".yft", m + "_hi.yft", m + ".ytd" }).Append("kitsunegen.ytd");
        foreach (var name in toStream)
        {
            if (!files.TryGetValue(name, out var f)) continue;
            var data = f.Rpf.ExtractFile(f.Entry) ?? throw new Exception("could not read " + name);
            if (f.Entry is RpfResourceFileEntry rfe)
                data = ResourceBuilder.AddResourceHeader(rfe, ResourceBuilder.Compress(data));
            File.WriteAllBytes(Path.Combine(modelsDir, "stream", f.Entry.Name), data);
        }
        var residentTxd = files.ContainsKey("kitsunegen.ytd") ? "kitsunegen" : null;
        File.WriteAllText(Path.Combine(modelsDir, "data", "vehicles.meta"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<CVehicleModelInfo__InitDataList>\n" +
            (residentTxd != null ? $"  <residentTxd>{residentTxd}</residentTxd>\n" : "  <residentTxd>vehshare</residentTxd>\n") +
            "  <residentAnims />\n  <InitDatas>\n  " + string.Join("\n  ", neededModels.Select(m => items[m])) +
            "\n  </InitDatas>\n  <txdRelationships />\n</CVehicleModelInfo__InitDataList>\n");
        File.WriteAllText(Path.Combine(modelsDir, "data", "carcols.meta"), carcols ?? "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<CVehicleModelInfoVarGlobal />\n");
        File.WriteAllText(Path.Combine(modelsDir, "data", "carvariations.meta"), carvariations ?? "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<CVehicleModelInfoVariation>\n  <variationData />\n</CVehicleModelInfoVariation>\n");
        File.WriteAllText(Path.Combine(modelsDir, "fxmanifest.lua"),
@"fx_version 'cerulean'
game 'gta5'

name 'Kitsune Trains - models'
author 'Kitsune Trains 7 by Foxunitone (gta5-mods.com); converted on your PC for prompt_train_script'
description 'Models for the kt_* train packs next to this folder. Do not re-upload.'

files { 'data/*.meta' }

data_file 'VEHICLE_METADATA_FILE'  'data/vehicles.meta'
data_file 'CARCOLS_FILE'           'data/carcols.meta'
data_file 'VEHICLE_VARIATION_FILE' 'data/carvariations.meta'
");

        var list = new StringBuilder();
        for (int i = 0; i < trains.Count; i++)
        {
            var (c, cars) = trains[i];
            var key = "kt_" + Regex.Replace(c.Name.ToLowerInvariant(), "[^a-z0-9_]", "_");
            var label = Label(c);
            var models = cars.Select(x => x.Model).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var signature = models.Skip(1).FirstOrDefault(m => usage[m] == 1) ?? models.FirstOrDefault(m => usage[m] == 1) ?? models[0];
            var size = cars.Sum(x => x.Repeat);
            var dir = Path.Combine(outRoot, key);
            Directory.CreateDirectory(dir);

            File.WriteAllText(Path.Combine(dir, "trains.xml"), TrainsXml(key, c.Gap, cars));
            File.WriteAllText(Path.Combine(dir, "train_pack.lua"),
$@"-- Train pack for prompt_train_script 2.x. Kitsune Trains 7 by Foxunitone, converted on your PC.
-- Put it on a line in prompt_train_script/config/trains.lua -> fleet, e.g. lossantos = {{ {key} = 100 }}
return {{
    key           = '{key}',
    label         = '{Lua(label)}',
    signature     = '{signature}',
    consistSize   = {size},
    expectedIndex = {FirstIndexGuess + i},
    models        = {{ {string.Join(", ", models.Select(m => $"'{m}'"))} }}, -- locomotive first
}}
");
            File.WriteAllText(Path.Combine(dir, "fxmanifest.lua"),
$@"fx_version 'cerulean'
game 'gta5'
lua54 'yes'

name '{Lua(label)} (Kitsune Trains)'
author 'Kitsune Trains 7 by Foxunitone (gta5-mods.com); converted on your PC for prompt_train_script'
description 'Train pack for prompt_train_script. Do not re-upload.'

dependency 'kt_models'

prompt_train_pack 'train_pack.lua'

files {{ 'trains.xml', 'train_pack.lua' }}

data_file 'TRAINCONFIGS_FILE' 'trains.xml'
");
            var flag = scripted.Contains(Joaat(cars[0].Model)) ? "  *" : "";
            list.AppendLine($"  {key,-24} {label} ({size} vehicles){flag}");
        }

        File.WriteAllText(Path.Combine(outRoot, "HOW_TO_INSTALL.txt"),
$@"Kitsune Trains for Prompt Train Script
Trains: Kitsune Trains 7 by Foxunitone (gta5-mods.com). This folder was made on your PC from
your own download. Please do not re-upload it - point people to the mod and this converter instead.

1. Copy this whole [kt_trains] folder into your server's resources folder.
2. In server.cfg add this line ABOVE the line that starts prompt_train_script:
       ensure [kt_trains]
3. Put trains on a line in prompt_train_script/config/trains.lua -> fleet, for example:
       lossantos = {{ {Key(trains[0].Config)} = 100 }},
   or mix several (the numbers are chances):
       lossantos = {{ bullet = 40, {Key(trains[0].Config)} = 60 }},
4. Restart the whole server.

Your trains (the first column is the name to use in fleet):
{list}
* = its wheels were animated by the mod's singleplayer script; in FiveM they may not turn.
These trains have no doors, seats or interior.
");

        try { Directory.Delete(work, true); } catch { }

        Say("");
        Say("Done! Your trains are in:");
        Say("  " + outRoot);
        Say("Open HOW_TO_INSTALL.txt in that folder for the next steps.");
        Pause();
        return 0;
    }

    static string Key(Config c) => "kt_" + Regex.Replace(c.Name.ToLowerInvariant(), "[^a-z0-9_]", "_");

    static string FindSource(string[] args, string exeDir)
    {
        if (args.Length > 0 && (File.Exists(args[0]) || Directory.Exists(args[0]))) return Path.GetFullPath(args[0]);

        var places = new[] { exeDir, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") };
        foreach (var dir in places.Where(Directory.Exists))
        {
            foreach (var zip in Directory.GetFiles(dir, "*.zip").OrderByDescending(File.GetLastWriteTime))
            {
                try
                {
                    using var z = ZipFile.OpenRead(zip);
                    if (z.Entries.Any(e => e.FullName.Replace('\\', '/').EndsWith("kitsunetrains/dlc.rpf", StringComparison.OrdinalIgnoreCase)))
                        return zip;
                }
                catch { }
            }
            foreach (var sub in Directory.GetDirectories(dir))
                if (FindFile(sub, @"kitsunetrains\dlc.rpf") != null) return sub;
        }
        return null;
    }

    static string FindFile(string root, string endsWith)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true })
                .FirstOrDefault(f => f.EndsWith(endsWith, StringComparison.OrdinalIgnoreCase));
        }
        catch { return null; }
    }

    static string Unzip(ZipArchive zip, string endsWith, string work, string outName, bool big)
    {
        var entry = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').EndsWith(endsWith, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return null;
        var dst = Path.Combine(work, outName);
        if (big) Say($"Unpacking {entry.Name} ({entry.Length / 1048576} MB) - this takes a minute...");
        using var input = entry.Open();
        using var output = File.Create(dst);
        var buffer = new byte[4 << 20];
        long done = 0; int lastPct = -1; int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
            done += read;
            var pct = (int)(done * 100 / Math.Max(1, entry.Length));
            if (big && pct / 10 != lastPct / 10) { Console.Write($"\r  {pct}%   "); lastPct = pct; }
        }
        if (big) Say("\r  100%   ");
        return dst;
    }

    static IEnumerable<RpfFile> Walk(RpfFile f)
    {
        yield return f;
        foreach (var c in f.Children ?? new List<RpfFile>())
            foreach (var x in Walk(c)) yield return x;
    }

    static List<Config> ParseConfigs(string xml)
    {
        var list = new List<Config>();
        foreach (var block in Regex.Split(xml, @"<train_config\b").Skip(1))
        {
            var body = block.Split("</train_config>")[0];
            var name = Regex.Match(body, @"name\s*=\s*""([^""]+)""").Groups[1].Value;
            var gap = Regex.Match(body, @"carriage_gap\s*=\s*""([^""]+)""");
            var cars = new List<Car>();
            foreach (Match c in Regex.Matches(body, @"<carriage\b[\s\S]*?/>"))
            {
                string A(string attr) => Regex.Match(c.Value, attr + @"\s*=\s*""([^""]*)""").Groups[1].Value;
                int.TryParse(A("repeat_count"), out var repeat);
                cars.Add(new Car(A("model_name"), A("carriage_vert_offset") is { Length: > 0 } v ? v : "1.0",
                    A("flip_model_dir").Equals("true", StringComparison.OrdinalIgnoreCase), Math.Max(1, repeat)));
            }
            if (cars.Count > 0) list.Add(new Config(name, gap.Success ? gap.Groups[1].Value : "0.0", cars));
        }
        return list;
    }

    static List<Config> ParsePick(string input, List<Config> configs)
    {
        input = input?.Replace("\uFEFF", "").Trim();
        if (string.IsNullOrWhiteSpace(input)) return null;
        if (input.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)) return configs.ToList();
        var picked = new List<Config>();
        foreach (var part in input.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var range = part.Split('-');
            if (!int.TryParse(range[0], out var a)) return null;
            var b = a;
            if (range.Length == 2 && !int.TryParse(range[1], out b)) return null;
            for (var i = Math.Min(a, b); i <= Math.Max(a, b); i++)
            {
                if (i < 1 || i > configs.Count) return null;
                if (!picked.Contains(configs[i - 1])) picked.Add(configs[i - 1]);
            }
        }
        return picked.Count > 0 ? picked : null;
    }

    // keep the locomotive(s) and the end car (caboose / end-of-train device), trim the middle
    static List<Car> Shorten(List<Car> cars)
    {
        if (cars.Sum(c => c.Repeat) <= MaxVehicles) return cars;
        var last = cars.Count > 1 && cars[^1].Repeat == 1 ? cars[^1] : null;
        var budget = MaxVehicles - (last != null ? 1 : 0);
        var result = new List<Car>();
        var used = 0;
        foreach (var c in last != null ? cars.Take(cars.Count - 1) : cars)
        {
            if (used >= budget) break;
            var take = Math.Min(c.Repeat, budget - used);
            result.Add(c with { Repeat = take });
            used += take;
        }
        if (last != null) result.Add(last);
        return result;
    }

    static string TrainsXml(string key, string gap, List<Car> cars)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version = \"1.0\" encoding = \"UTF-8\"?>");
        sb.AppendLine("<train_configs version = \"1\">");
        sb.AppendLine("\t<train_config");
        sb.AppendLine($"\t\tname = \"{key}_config0\"");
        sb.AppendLine("\t\tpopulate_train_dist = \"40.0\"");
        sb.AppendLine("\t\tannounce_stations = \"false\"");
        sb.AppendLine("\t\tdoors_beep = \"false\"");
        sb.AppendLine("\t\tcarriages_hang = \"false\"");
        sb.AppendLine("\t\tcarriages_swing = \"false\"");
        sb.AppendLine("\t\tlink_tracks_with_adjacent_stations = \"true\"");
        sb.AppendLine("\t\tno_random_spawn = \"true\"");
        sb.AppendLine($"\t\tcarriage_gap = \"{gap}\">");
        foreach (var c in cars)
        {
            sb.AppendLine();
            sb.AppendLine("\t\t<carriage");
            sb.AppendLine($"\t\t\tmodel_name = \"{c.Model}\"");
            sb.AppendLine("\t\t\tmax_peds_per_carriage = \"0\"");
            sb.AppendLine($"\t\t\tflip_model_dir = \"{(c.Flip ? "true" : "false")}\"");
            sb.AppendLine("\t\t\tdo_interior_lights = \"true\"");
            sb.AppendLine($"\t\t\tcarriage_vert_offset = \"{c.Vert}\"");
            sb.AppendLine($"\t\t\trepeat_count = \"{c.Repeat}\" />");
        }
        sb.AppendLine("\t</train_config>");
        sb.AppendLine("</train_configs>");
        return sb.ToString();
    }

    static uint Joaat(string s)
    {
        uint h = 0;
        foreach (var ch in Encoding.UTF8.GetBytes(s.ToLowerInvariant()))
        {
            h += ch; h += h << 10; h ^= h >> 6;
        }
        h += h << 3; h ^= h >> 11; h += h << 15;
        return h;
    }

    static string Lua(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");
    static void Say(string s) => Console.WriteLine(s);
    static void Pause() { Console.WriteLine(); Console.Write("Press Enter to close..."); Console.ReadLine(); }
}
