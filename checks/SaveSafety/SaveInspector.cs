using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using NGUInjector;

internal static class SaveInspector
{
    private static int Main(string[] args)
    {
        var files = new List<SaveIdentity>();
        var errors = new List<object>();
        SaveIdentity longest = null;
        bool ambiguousLongest = false;
        string inputPath = "";
        try
        {
            if (args.Length == 0)
                throw new ArgumentException("Usage: SaveInspector --directory <existing root> OR <file> [<file> ...].");

            var paths = new List<string>();
            if (args[0] == "--directory")
            {
                if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
                    throw new ArgumentException("--directory requires exactly one existing root and cannot be combined with file paths.");
                inputPath = args[1];
                inputPath = Path.GetFullPath(inputPath);
                if (!Directory.Exists(inputPath))
                    throw new DirectoryNotFoundException("Save directory does not exist.");
                foreach (string path in Directory.EnumerateFiles(inputPath, "*.txt", SearchOption.TopDirectoryOnly))
                {
                    if (paths.Count == SaveSafetyCatalog.MaxCandidates)
                        throw new InvalidDataException("Save candidate limit exceeded (4096 files maximum).");
                    paths.Add(path);
                }
            }
            else
            {
                if (args.Length > SaveSafetyCatalog.MaxCandidates)
                    throw new ArgumentException("Save candidate limit exceeded (4096 files maximum).");
                foreach (string path in args)
                {
                    if (path.StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException("Unknown or misplaced option: " + path + ".");
                    paths.Add(path);
                }
            }

            if (paths.Count == 0)
                throw new InvalidDataException("No save files were supplied or found.");

            foreach (string path in paths)
            {
                string fullPath = path;
                try
                {
                    if (string.IsNullOrWhiteSpace(path))
                        throw new ArgumentException("A save file path is required.");
                    fullPath = Path.GetFullPath(path);
                    if (!File.Exists(fullPath))
                        throw new FileNotFoundException("Save file does not exist or is not a regular file.");
                    files.Add(SaveSafetyCatalog.Read(fullPath, SaveSafetyCatalog.NativeVersion));
                }
                catch (Exception ex)
                {
                    errors.Add(new { path = fullPath, message = ex.Message });
                }
            }

            longest = SaveSafetyCatalog.SelectLatest(files, null, out ambiguousLongest);
        }
        catch (Exception ex)
        {
            errors.Add(new { path = inputPath, message = ex.Message });
        }

        bool success = errors.Count == 0 && !ambiguousLongest;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            version = 1,
            success,
            files = files.ConvertAll(Metadata),
            longest = Metadata(longest),
            ambiguousLongest,
            errors
        }));
        return success ? 0 : 1;
    }

    private static object Metadata(SaveIdentity identity)
    {
        if (identity == null) return null;
        return new
        {
            path = identity.Path,
            sha256 = identity.Sha256,
            totalPlaytimeSeconds = identity.TotalPlaytimeSeconds.ToString("R", CultureInfo.InvariantCulture),
            rebirthSeconds = identity.RebirthSeconds.ToString("R", CultureInfo.InvariantCulture),
            rebirth = identity.Rebirth.ToString(CultureInfo.InvariantCulture),
            version = identity.Version,
            lastTime = identity.LastTime,
            playerName = identity.PlayerName
        };
    }
}
