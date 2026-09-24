using System;
using System.Collections.Generic;
using System.IO;
using RythmRPG.LevelComposer.Json;

namespace RythmRPG.LevelComposer.Types
{
    /// <summary>
    /// All note types the composer knows: the built-ins, then any JSON note-type files (which can add new gimmicks or
    /// override built-ins by id), then anything registered from code. The library, inspector, validator, simulator and
    /// importer only ever talk to this registry, never to a fixed enum.
    /// </summary>
    public sealed class NoteTypeRegistry
    {
        private readonly List<NoteTypeDef> types = new List<NoteTypeDef>();
        private readonly List<string> loadMessages = new List<string>();

        public IList<NoteTypeDef> All { get { return types.AsReadOnly(); } }
        /// <summary>Warnings and info from the last JSON load (shown in the app's status/problems list).</summary>
        public IList<string> LoadMessages { get { return loadMessages.AsReadOnly(); } }
        public event Action Changed;

        public static NoteTypeRegistry CreateDefault()
        {
            var r = new NoteTypeRegistry();
            foreach (NoteTypeDef d in BuiltInNoteTypes.Create()) r.types.Add(d);
            return r;
        }

        public NoteTypeDef Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < types.Count; i++)
                if (string.Equals(types[i].Id, id, StringComparison.OrdinalIgnoreCase)) return types[i];
            return null;
        }

        /// <summary>Visible types in display order, grouped by category (categories in first-seen order).</summary>
        public List<string> Categories()
        {
            var result = new List<string>();
            for (int i = 0; i < types.Count; i++)
                if (!types[i].Hidden && !result.Contains(types[i].Category)) result.Add(types[i].Category);
            return result;
        }

        public List<NoteTypeDef> InCategory(string category)
        {
            var result = new List<NoteTypeDef>();
            for (int i = 0; i < types.Count; i++)
                if (!types[i].Hidden && types[i].Category == category) result.Add(types[i]);
            return result;
        }

        /// <summary>Adds a type, or replaces the one with the same id.</summary>
        public void Register(NoteTypeDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.Id)) throw new ArgumentException("Note type needs an id");
            for (int i = 0; i < types.Count; i++)
            {
                if (string.Equals(types[i].Id, def.Id, StringComparison.OrdinalIgnoreCase))
                {
                    types[i] = def;
                    RaiseChanged();
                    return;
                }
            }

            types.Add(def);
            RaiseChanged();
        }

        /// <summary>Loads every *.json file in the given folders (missing folders are skipped). Returns the number of types read.</summary>
        public int LoadFolders(IEnumerable<string> folders)
        {
            int count = 0;
            foreach (string folder in folders)
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
                string[] files = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (string file in files)
                {
                    try
                    {
                        count += LoadJson(File.ReadAllText(file), file);
                    }
                    catch (Exception e)
                    {
                        loadMessages.Add("Note types: could not read " + Path.GetFileName(file) + ": " + e.Message);
                    }
                }
            }

            if (count > 0) RaiseChanged();
            return count;
        }

        /// <summary>
        /// Reads a note-type JSON document: either {"noteTypes":[...]} or a single type object. A type whose id already
        /// exists overrides that type's fields (parameters are merged by key); "extends": "otherId" starts from a copy of
        /// another type. Returns the number of types added or changed.
        /// </summary>
        public int LoadJson(string json, string source)
        {
            object root = MiniJson.Parse(json);
            var list = new List<Dictionary<string, object>>();
            Dictionary<string, object> obj = JsonRead.Obj(root);
            if (obj != null && JsonRead.Arr(obj, "noteTypes") != null)
            {
                foreach (object o in JsonRead.Arr(obj, "noteTypes"))
                {
                    Dictionary<string, object> d = JsonRead.Obj(o);
                    if (d != null) list.Add(d);
                }
            }
            else if (obj != null)
            {
                list.Add(obj);
            }
            else
            {
                List<object> arr = root as List<object>;
                if (arr != null)
                    foreach (object o in arr)
                        if (JsonRead.Obj(o) != null) list.Add(JsonRead.Obj(o));
            }

            int count = 0;
            foreach (Dictionary<string, object> d in list)
            {
                string id = JsonRead.Str(d, "id").Trim();
                if (id.Length == 0)
                {
                    loadMessages.Add("Note types: an entry in " + Path.GetFileName(source) + " has no id and was skipped.");
                    continue;
                }

                NoteTypeDef existing = Find(id);
                NoteTypeDef def;
                if (existing != null) def = existing.Clone();
                else
                {
                    string extends = JsonRead.Str(d, "extends");
                    NoteTypeDef baseDef = Find(extends);
                    if (extends.Length > 0 && baseDef == null)
                        loadMessages.Add("Note types: '" + id + "' extends unknown type '" + extends + "'.");
                    def = baseDef != null ? baseDef.Clone() : new NoteTypeDef();
                    def.Id = id;
                    if (baseDef == null || !d.ContainsKey("name")) def.Name = id;
                }

                Apply(def, d);
                def.Source = source;
                if (!IsKnownArchetype(def.Archetype))
                    loadMessages.Add("Note types: '" + id + "' uses archetype '" + def.Archetype + "'. The preview needs a matching simulator behaviour, otherwise it plays like a tap.");
                Register(def);
                count++;
            }

            return count;
        }

        public static bool IsKnownArchetype(string archetype)
        {
            switch (archetype)
            {
                case Archetypes.Tap:
                case Archetypes.Hold:
                case Archetypes.Stationary:
                case Archetypes.StationaryHold:
                case Archetypes.Mash:
                case Archetypes.PingPong:
                    return true;
                default:
                    return false;
            }
        }

        private static void Apply(NoteTypeDef def, Dictionary<string, object> d)
        {
            if (d.ContainsKey("name")) def.Name = JsonRead.Str(d, "name", def.Name);
            if (d.ContainsKey("category")) def.Category = JsonRead.Str(d, "category", def.Category);
            if (d.ContainsKey("description")) def.Description = JsonRead.Str(d, "description", def.Description);
            if (d.ContainsKey("color")) def.Color = JsonRead.Str(d, "color", def.Color);
            if (d.ContainsKey("shape")) def.Shape = ParseEnum(JsonRead.Str(d, "shape"), def.Shape);
            if (d.ContainsKey("archetype")) def.Archetype = JsonRead.Str(d, "archetype", def.Archetype).ToLowerInvariant();
            if (d.ContainsKey("hasLength")) def.HasLength = JsonRead.Bool(d, "hasLength", def.HasLength);
            if (d.ContainsKey("defaultLengthBeats")) def.DefaultLengthBeats = JsonRead.Num(d, "defaultLengthBeats", def.DefaultLengthBeats);
            if (d.ContainsKey("output")) def.Output = ParseEnum(JsonRead.Str(d, "output"), def.Output);
            if (d.ContainsKey("legacyType")) def.LegacyType = JsonRead.Str(d, "legacyType", def.LegacyType);
            if (d.ContainsKey("sequenceKind")) def.SequenceKind = JsonRead.Str(d, "sequenceKind", def.SequenceKind);
            if (d.ContainsKey("prefab")) def.Prefab = JsonRead.Str(d, "prefab", def.Prefab);
            if (d.ContainsKey("stationary")) def.Stationary = JsonRead.Bool(d, "stationary", def.Stationary);
            if (d.ContainsKey("hidden")) def.Hidden = JsonRead.Bool(d, "hidden", def.Hidden);

            List<object> ps = JsonRead.Arr(d, "params");
            if (ps == null) return;
            foreach (object po in ps)
            {
                Dictionary<string, object> pd = JsonRead.Obj(po);
                if (pd == null) continue;
                string key = JsonRead.Str(pd, "key").Trim();
                if (key.Length == 0) continue;
                ParamDef p = def.FindParam(key);
                if (JsonRead.Bool(pd, "remove"))
                {
                    if (p != null) def.Params.Remove(p);
                    continue;
                }

                if (p == null)
                {
                    p = new ParamDef { Key = key, Label = key };
                    def.Params.Add(p);
                }

                ApplyParam(p, pd);
            }
        }

        private static void ApplyParam(ParamDef p, Dictionary<string, object> pd)
        {
            if (pd.ContainsKey("label")) p.Label = JsonRead.Str(pd, "label", p.Label);
            if (pd.ContainsKey("kind")) p.Kind = ParseEnum(JsonRead.Str(pd, "kind"), p.Kind);
            if (pd.ContainsKey("min")) p.Min = JsonRead.Num(pd, "min", p.Min);
            if (pd.ContainsKey("max")) p.Max = JsonRead.Num(pd, "max", p.Max);
            if (pd.ContainsKey("step")) p.Step = JsonRead.Num(pd, "step", p.Step);
            if (pd.ContainsKey("tooltip")) p.Tooltip = JsonRead.Str(pd, "tooltip", p.Tooltip);
            if (pd.ContainsKey("group")) p.Group = JsonRead.Str(pd, "group", p.Group);
            if (pd.ContainsKey("bind")) p.Bind = JsonRead.Str(pd, "bind", p.Bind);
            if (pd.ContainsKey("defaultSeconds")) p.DefaultSeconds = JsonRead.Num(pd, "defaultSeconds", p.DefaultSeconds);
            List<object> options = JsonRead.Arr(pd, "options");
            if (options != null)
            {
                var o = new List<string>();
                foreach (object x in options) if (x != null) o.Add(Convert.ToString(x, System.Globalization.CultureInfo.InvariantCulture));
                p.Options = o.ToArray();
            }

            if (pd.ContainsKey("default"))
            {
                object v;
                pd.TryGetValue("default", out v);
                p.Default = v;
                p.Default = p.Coerce(v);
            }
        }

        public static T ParseEnum<T>(string text, T fallback) where T : struct
        {
            if (string.IsNullOrEmpty(text)) return fallback;
            T value;
            string cleaned = text.Replace("_", "").Replace("-", "").Replace(" ", "");
            return Enum.TryParse(cleaned, true, out value) ? value : fallback;
        }

        /// <summary>Serializes a type to the JSON note-type format (used to write example files and for tests).</summary>
        public static Dictionary<string, object> ToJson(NoteTypeDef def)
        {
            var d = new Dictionary<string, object>();
            d["id"] = def.Id;
            d["name"] = def.Name;
            d["category"] = def.Category;
            d["description"] = def.Description;
            d["color"] = def.Color;
            d["shape"] = def.Shape.ToString().ToLowerInvariant();
            d["archetype"] = def.Archetype;
            d["hasLength"] = def.HasLength;
            if (def.HasLength) d["defaultLengthBeats"] = def.DefaultLengthBeats;
            d["output"] = def.Output.ToString().ToLowerInvariant();
            if (def.Output == NoteOutput.Note) d["legacyType"] = def.LegacyType;
            else d["sequenceKind"] = def.SequenceKind;
            if (!string.IsNullOrEmpty(def.Prefab)) d["prefab"] = def.Prefab;
            if (def.Stationary) d["stationary"] = true;
            var ps = new List<object>();
            foreach (ParamDef p in def.Params)
            {
                var pd = new Dictionary<string, object>();
                pd["key"] = p.Key;
                pd["label"] = p.Label;
                pd["kind"] = p.Kind.ToString().ToLowerInvariant();
                if (p.Kind == ParamKind.Beats && p.DefaultSeconds > 0d) pd["defaultSeconds"] = p.DefaultSeconds;
                else if (p.Default != null) pd["default"] = p.Default;
                if (!double.IsInfinity(p.Min)) pd["min"] = p.Min;
                if (!double.IsInfinity(p.Max)) pd["max"] = p.Max;
                if (p.Step > 0d) pd["step"] = p.Step;
                if (p.Options.Length > 0) pd["options"] = new List<object>(p.Options);
                if (!string.IsNullOrEmpty(p.Bind)) pd["bind"] = p.Bind;
                if (!string.IsNullOrEmpty(p.Group)) pd["group"] = p.Group;
                if (!string.IsNullOrEmpty(p.Tooltip)) pd["tooltip"] = p.Tooltip;
                ps.Add(pd);
            }

            d["params"] = ps;
            return d;
        }

        private void RaiseChanged()
        {
            Action h = Changed;
            if (h != null) h();
        }
    }
}
