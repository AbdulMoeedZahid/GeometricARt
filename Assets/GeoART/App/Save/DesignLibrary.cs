using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using GeoART.Contracts;
using UnityEngine;

namespace GeoART.App.Save
{
    /// <summary>What the designs list shows for one saved file. <see cref="Id"/> is the file name without ".json".</summary>
    public sealed class DesignEntry
    {
        public string Id { get; }
        public string Name { get; }
        public DateTime SavedUtc { get; }
        public int LayerCount { get; }
        public int ShapeCount { get; }
        public GridKind GridKind { get; }
        public int GridN { get; }

        public DesignEntry(string id, string name, DateTime savedUtc, int layerCount, int shapeCount, GridKind gridKind, int gridN)
        {
            Id = id; Name = name; SavedUtc = savedUtc; LayerCount = layerCount; ShapeCount = shapeCount;
            GridKind = gridKind; GridN = gridN;
        }

        internal static DesignEntry From(string id, string name, DateTime savedUtc, Board b)
        {
            int shapes = 0;
            foreach (var layer in b.Layers) shapes += layer.Shapes.Count;
            return new DesignEntry(id, name, savedUtc, b.Layers.Count, shapes, b.GridKind, b.GridN);
        }
    }

    /// <summary>
    /// Saved designs as one JSON file each in a folder (by default <see cref="DefaultRoot"/>). File name = id =
    /// "design-yyyyMMdd-HHmmss-fff" in UTC, with "-2", "-3"... added if that name is taken. A save is written to
    /// "&lt;id&gt;.json.tmp" first and then renamed, so a crash halfway never leaves a cut-off design.
    /// No method throws: IO problems come back as false plus a short message the UI can show. A file that does not
    /// parse is left alone and skipped in <see cref="List"/> with one warning.
    /// </summary>
    public sealed class DesignLibrary
    {
        const string Extension = ".json";
        const string TempExtension = ".tmp";
        static readonly Regex DefaultNamePattern = new Regex(@"^Design (\d+)$", RegexOptions.CultureInvariant);
        static readonly Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>
        /// When set, DesignsPanel uses this folder instead of <see cref="DefaultRoot"/>. Only for automated checks, so they
        /// do not touch the user's real saves. Cleared when Play Mode starts.
        /// </summary>
        public static string LibraryRootOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => LibraryRootOverride = null;

        /// <summary>Windows Editor: %USERPROFILE%\AppData\LocalLow\GeoART Team\Geometric ARt\Designs. iOS: Documents/Designs.</summary>
        public static string DefaultRoot => Path.Combine(Application.persistentDataPath, "Designs");

        public string Root { get; }

        readonly HashSet<string> _warned = new HashSet<string>();   // broken files already reported, with their write time

        public DesignLibrary(string rootDirectory)
        {
            if (string.IsNullOrEmpty(rootDirectory)) throw new ArgumentException("A folder is needed.", nameof(rootDirectory));
            Root = rootDirectory;
        }

        /// <summary>Every readable design, newest first (same time: higher id first). Creates the folder if it is missing.</summary>
        public IReadOnlyList<DesignEntry> List()
        {
            var entries = new List<DesignEntry>();
            string[] files;
            try
            {
                Directory.CreateDirectory(Root);
                files = Directory.GetFiles(Root);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Designs: cannot read the folder {Root}: {e.Message}");
                return entries;
            }

            foreach (string path in files)
            {
                if (!string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase)) continue;
                string id = Path.GetFileNameWithoutExtension(path);
                if (TryRead(path, id, out _, out DesignEntry entry, out string error)) entries.Add(entry);
                else WarnOnce(path, error);
            }

            entries.Sort((a, b) =>
            {
                int byTime = b.SavedUtc.CompareTo(a.SavedUtc);
                return byTime != 0 ? byTime : string.CompareOrdinal(b.Id, a.Id);
            });
            return entries;
        }

        /// <summary>"Design N", one more than the highest "Design k" in the folder.</summary>
        public string NextDefaultName()
        {
            int max = 0;
            foreach (var e in List())
            {
                var m = DefaultNamePattern.Match(e.Name ?? "");
                if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int k))
                    max = Math.Max(max, k);
            }
            return "Design " + (max + 1).ToString(CultureInfo.InvariantCulture);
        }

        public bool TrySave(Board board, out DesignEntry entry, out string error, string name = null)
        {
            entry = null;
            error = null;
            if (board == null)
            {
                error = "Nothing to save.";
                return false;
            }
            string tmp = null;
            try
            {
                Directory.CreateDirectory(Root);
                if (string.IsNullOrEmpty(name)) name = NextDefaultName();

                DateTime now = DateTime.UtcNow;
                now = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);   // what the file keeps
                string baseId = "design-" + now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
                string id = baseId;
                for (int n = 2; File.Exists(PathFor(id)) || File.Exists(PathFor(id) + TempExtension); n++)
                    id = baseId + "-" + n.ToString(CultureInfo.InvariantCulture);

                string json = DesignSerializer.ToJson(board, name, now);
                string path = PathFor(id);
                tmp = path + TempExtension;
                File.WriteAllText(tmp, json, Utf8);
                File.Move(tmp, path);
                tmp = null;
                entry = DesignEntry.From(id, name, now, board);
                return true;
            }
            catch (Exception e)
            {
                error = "Could not save: " + e.Message;
                if (tmp != null) TryDeleteFile(tmp);
                return false;
            }
        }

        public bool TryLoad(string id, out Board board, out string error)
        {
            board = null;
            if (!CheckId(id, out error)) return false;
            string path = PathFor(id);
            if (!File.Exists(path))
            {
                error = "That design is gone (file not found).";
                return false;
            }
            return TryRead(path, id, out board, out _, out error);
        }

        public bool TryDelete(string id, out string error)
        {
            if (!CheckId(id, out error)) return false;
            string path = PathFor(id);
            try
            {
                if (!File.Exists(path))
                {
                    error = "That design is gone (file not found).";
                    return false;
                }
                File.Delete(path);
                return true;
            }
            catch (Exception e)
            {
                error = "Could not delete: " + e.Message;
                return false;
            }
        }

        /// <summary>Ids only ever come from our own file names, but never let one point outside the folder.</summary>
        public static bool IsValidId(string id) =>
            !string.IsNullOrEmpty(id) && id.Trim() == id && !id.Contains("..") && id.IndexOf('/') < 0 && id.IndexOf('\\') < 0
            && id.IndexOf(':') < 0 && id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

        static bool CheckId(string id, out string error)
        {
            error = IsValidId(id) ? null : "Not a valid design id.";
            return error == null;
        }

        string PathFor(string id) => Path.Combine(Root, id + Extension);

        static bool TryRead(string path, string id, out Board board, out DesignEntry entry, out string error)
        {
            board = null;
            entry = null;
            string json;
            try
            {
                json = File.ReadAllText(path, Utf8);
            }
            catch (Exception e)
            {
                error = "Could not read the file: " + e.Message;
                return false;
            }
            if (!DesignSerializer.TryParse(json, out DesignFile file, out error)) return false;
            try
            {
                board = DesignSerializer.ToBoard(file.board);
            }
            catch (Exception e)
            {
                error = "The file is damaged: " + e.Message;
                return false;
            }
            if (!DesignSerializer.TryParseUtc(file.savedUtc, out DateTime saved))
                saved = SafeWriteTime(path);
            entry = DesignEntry.From(id, string.IsNullOrEmpty(file.name) ? id : file.name, saved, board);
            return true;
        }

        void WarnOnce(string path, string error)
        {
            string key = path + "|" + SafeWriteTime(path).Ticks.ToString(CultureInfo.InvariantCulture);
            if (_warned.Add(key))
                Debug.LogWarning($"Designs: skipped {Path.GetFileName(path)}: {error}");
        }

        static DateTime SafeWriteTime(string path)
        {
            try { return File.GetLastWriteTimeUtc(path); }
            catch (Exception) { return DateTime.MinValue; }
        }

        static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception) { /* left over .tmp files are ignored by List */ }
        }
    }
}
