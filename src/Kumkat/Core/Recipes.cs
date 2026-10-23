using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Kumkat.Core
{
    internal sealed class RecipeFile
    {
        public List<RecipeGroup> Groups { get; set; } = new List<RecipeGroup>();
    }

    internal sealed class RecipeGroup
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public List<string> Extensions { get; set; } = new List<string>();
        public List<RecipeAction> Actions { get; set; } = new List<RecipeAction>();
    }

    internal sealed class RecipeAction
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string? Sub { get; set; }
        public bool Tool { get; set; }
        public string Ext { get; set; } = "";
        public string? Suffix { get; set; }
        public string Engine { get; set; } = "";
        public List<string>? Args { get; set; }
        public string? Via { get; set; }
    }

    /// <summary>Radyal menüdeki bir dilim.</summary>
    internal sealed class MenuEntry
    {
        public MenuEntry(RecipeGroup group, RecipeAction action, bool enabled, string? disabledReason)
        {
            Group = group;
            Action = action;
            Enabled = enabled;
            DisabledReason = disabledReason;
        }

        public RecipeGroup Group { get; }
        public RecipeAction Action { get; }
        public bool Enabled { get; }
        public string? DisabledReason { get; }
        public string Label => Action.Label;
        public string? Sub => Action.Sub;
        public bool IsTool => Action.Tool;
    }

    internal sealed class RecipeBook
    {
        public const string SameExt = "{same}";

        // Görsel araçlarında "{same}" için ffmpeg'in yazabildiği uzantılar; diğerleri JPG'ye düşer.
        private static readonly HashSet<string> SameImageWritable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "png", "jpg", "jpeg", "bmp", "tif", "tiff", "webp"
        };

        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "jpeg", "jpg" }, { "jfif", "jpg" }, { "tif", "tiff" }, { "htm", "html" }, { "heif", "heic" },
        };

        public RecipeFile File { get; }

        private RecipeBook(RecipeFile file) { File = file; }

        /// <summary>%AppData%\Kumkat\recipes.json varsa onu, yoksa gömülü tarifleri yükler.</summary>
        public static RecipeBook Load()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };

            string userFile = UserRecipesPath;
            if (System.IO.File.Exists(userFile))
            {
                try
                {
                    var f = JsonSerializer.Deserialize<RecipeFile>(System.IO.File.ReadAllText(userFile), options);
                    if (f != null && f.Groups.Count > 0) return new RecipeBook(f);
                }
                catch (Exception ex)
                {
                    Log.Error("recipes-user", ex);
                }
            }

            string json = ReadEmbeddedRecipes();
            var file = JsonSerializer.Deserialize<RecipeFile>(json, options) ?? new RecipeFile();
            return new RecipeBook(file);
        }

        public static string UserRecipesPath => Path.Combine(AppPaths.DataDir, "recipes.json");

        public static string ReadEmbeddedRecipes()
        {
            using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Kumkat.recipes.json");
            if (s == null) throw new InvalidOperationException("Gömülü recipes.json bulunamadı.");
            using var reader = new StreamReader(s);
            return reader.ReadToEnd();
        }

        public static string ExtOf(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

        public static string Canonical(string ext)
        {
            ext = ext.ToLowerInvariant();
            return Aliases.TryGetValue(ext, out var c) ? c : ext;
        }

        public RecipeGroup? GroupFor(string path)
        {
            string ext = ExtOf(path);
            return File.Groups.FirstOrDefault(g => g.Extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>Tarifin üreteceği uzantı ("{same}" çözümlenmiş halde).</summary>
        public static string ResolveOutputExt(RecipeGroup group, RecipeAction action, string inputExt)
        {
            if (action.Ext != SameExt) return action.Ext;
            if (group.Id == "image" && !SameImageWritable.Contains(inputExt)) return "jpg";
            return inputExt;
        }

        public List<MenuEntry> EntriesFor(RecipeGroup group, string inputExt, ToolLocator tools)
        {
            var list = new List<MenuEntry>();
            foreach (var a in group.Actions)
            {
                string outExt = ResolveOutputExt(group, a, inputExt);
                bool sameFormat = Canonical(outExt) == Canonical(inputExt);
                if (sameFormat && string.IsNullOrEmpty(a.Suffix)) continue; // PNG -> PNG gösterme

                string? missing = tools.MissingToolFor(a.Engine, a.Via);
                list.Add(new MenuEntry(group, a, missing == null, missing == null ? null : missing + " gerekli"));
            }
            return list;
        }
    }
}
