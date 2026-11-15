// Каталог маунтов (catalog.json) и план сборки таблицы: что читает SheetBuilder.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MountSheetSync
{
    /// <summary>Лист каталога: заголовки и строки (строки — текст, числа — double).</summary>
    sealed class CatalogSheet
    {
        public string Key = "";
        public string Name = "";
        public List<string> Columns = new List<string>();
        public List<object[]> Rows = new List<object[]>();

        public int Col(string name)
        {
            return Columns.IndexOf(name);
        }
    }

    /// <summary>Встроенный список маунтов (снят с таблицы автора).</summary>
    sealed class Catalog
    {
        public string Version = "";
        public CatalogSheet Raids;
        public CatalogSheet Dungeons;
        public CatalogSheet World;
        /// <summary>Редкие монстры; в каталогах до 2.9.2 их нет.</summary>
        public CatalogSheet Rare;
        /// <summary>mountID → прежние названия маунта в каталоге (до названий «как в игре»).</summary>
        public Dictionary<long, List<string>> Aliases = new Dictionary<long, List<string>>();

        public static Catalog Parse(string json)
        {
            Dictionary<string, object> root = (Dictionary<string, object>)Json.Parse(json);
            Catalog c = new Catalog();
            c.Version = J.Str(root, "version");
            foreach (object o in J.Arr(J.Get(root, "sheets")))
            {
                Dictionary<string, object> s = J.Obj(o);
                CatalogSheet sheet = new CatalogSheet();
                sheet.Key = J.Str(s, "key");
                sheet.Name = J.Str(s, "sheet");
                sheet.Columns = J.Arr(J.Get(s, "columns")).Select(x => SheetLogic.JsString(x)).ToList();
                foreach (object r in J.Arr(J.Get(s, "rows")))
                {
                    List<object> cells = J.Arr(r);
                    object[] row = new object[sheet.Columns.Count];
                    for (int i = 0; i < row.Length; i++)
                    {
                        row[i] = i < cells.Count && cells[i] != null ? cells[i] : "";
                    }
                    sheet.Rows.Add(row);
                }
                if (sheet.Key == "raids")
                {
                    c.Raids = sheet;
                }
                else if (sheet.Key == "dungeons")
                {
                    c.Dungeons = sheet;
                }
                else if (sheet.Key == "world")
                {
                    c.World = sheet;
                }
                else if (sheet.Key == "rare")
                {
                    c.Rare = sheet;
                }
            }
            if (c.Raids == null || c.Dungeons == null || c.World == null)
            {
                throw new FormatException("в каталоге нет листа рейдов, подземелий или мировых боссов");
            }
            Dictionary<string, object> aliases = J.Obj(J.Get(root, "aliases"));
            if (aliases != null)
            {
                foreach (KeyValuePair<string, object> kv in aliases)
                {
                    long id;
                    if (long.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id > 0)
                    {
                        c.Aliases[id] = J.Arr(kv.Value).Select(x => SheetLogic.JsString(x)).Where(x => x.Length > 0).ToList();
                    }
                }
            }
            return c;
        }

        static Catalog embedded;

        public static Catalog Embedded()
        {
            if (embedded == null)
            {
                Assembly asm = typeof(Catalog).Assembly;
                using (Stream s = asm.GetManifestResourceStream("MountSheetSync.catalog.json"))
                {
                    if (s == null)
                    {
                        throw new InvalidOperationException("в программе нет каталога маунтов (catalog.json)");
                    }
                    using (StreamReader reader = new StreamReader(s, Encoding.UTF8))
                    {
                        embedded = Parse(reader.ReadToEnd());
                    }
                }
            }
            return embedded;
        }
    }

    /// <summary>Персонаж новой таблицы: для каких листов его заводить.</summary>
    sealed class BuildCharacter
    {
        public string Name = "";
        public bool Raids = true;
        public bool Dungeons = true;
        /// <summary>Колонка-галочка на «Мировых боссах».</summary>
        public bool World = true;

        public BuildCharacter(string name, bool raids, bool dungeons)
            : this(name, raids, dungeons, raids)
        {
        }

        public BuildCharacter(string name, bool raids, bool dungeons, bool world)
        {
            Name = name;
            Raids = raids;
            Dungeons = dungeons;
            World = world;
        }
    }

    sealed class BuildPlan
    {
        public string Title = "Маунты WoW";
        public List<BuildCharacter> Characters = new List<BuildCharacter>();
        /// <summary>Имена собранных маунтов из игры (журнал маунтов) — для строк без ID.</summary>
        public List<string> CollectedMounts = new List<string>();
        /// <summary>mountID собранных маунтов — по ним «Собрано?» = «Да» (точнее имён).</summary>
        public HashSet<long> CollectedIds = new HashSet<long>();
        public DateTime Today = DateTime.Today;
        /// <summary>Время снимка коллекции, по которому отмечено «Собрано?» (0 — нет снимка).</summary>
        public long CollectionStamp;
        public Catalog Catalog;
        /// <summary>Язык таблицы: названия листов, заголовки, списки; тексты каталога — по-английски, если не русский.</summary>
        public SheetLang Lang = SheetLang.Russian;
        /// <summary>Имена из игры (маунты, боссы, подземелья) — если игра на языке таблицы.</summary>
        public GameNames Game;
    }
}
