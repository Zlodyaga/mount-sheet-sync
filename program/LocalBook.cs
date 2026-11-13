// Данные программы в файле на этом компьютере, без Google: те же листы и колонки, что в таблице, поэтому логика
// (SheetLogic, CollectionSync) работает как обычно. Создают и меняют листы те же запросы SheetBuilder: из них
// берутся значения ячеек, оформление не нужно.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MountSheetSync
{
    /// <summary>Режим Settings.ModeLocal: читаем файл, решаем всё здесь и пишем обратно.</summary>
    sealed class LocalSheetService : BookSheetService
    {
        readonly string file;
        readonly Settings settings;

        public LocalSheetService(string file, Settings settings)
        {
            this.file = file;
            this.settings = settings;
        }

        public override Dictionary<string, object> Call(Dictionary<string, object> payload)
        {
            using (LocalBook.Exclusive(file))
            {
                return base.Call(payload);
            }
        }

        public override CollectionResult SyncCollection(GameSnapshot game, bool force)
        {
            using (LocalBook.Exclusive(file))
            {
                return base.SyncCollection(game, force);
            }
        }

        /// <summary>Файла ещё нет: создаём, как только аддон узнал персонажей. До тех пор данные пустые и на диск не пишутся.</summary>
        protected override IStructuredBook Load()
        {
            if (File.Exists(file))
            {
                return LocalBook.Load(file);
            }
            GameSnapshot game = Program.ReadGame(settings);
            if (game.Characters.Count == 0)
            {
                return LocalBook.Empty(file);
            }
            LocalBook created;
            try
            {
                created = LocalBook.Create(file, Program.PlanFor(game, null, null));
            }
            catch (ArgumentException ex)
            {
                throw new SyncException(ex.Message);
            }
            Log.Write(Tr.L("Данные программы созданы на этом компьютере: {0}", file));
            return created;
        }

        /// <summary>
        /// Без Google таблицы, которую человек ведёт сам, нет: кого аддон узнал, того и добавляем, всеми группами, до отправки его
        /// убийств. Кого человек убрал сам, остаётся убранным (LocalBook.IsSkipped). Файла нет — его создаст первая загрузка, сразу со всеми.
        /// </summary>
        public List<string> AddNewCharacters(GameSnapshot game)
        {
            List<string> added = new List<string>();
            using (LocalBook.Exclusive(file))
            {
                if (game.Characters.Count == 0 || !File.Exists(file))
                {
                    return added;
                }
                try
                {
                    LocalBook book = (LocalBook)Book();
                    HashSet<string> have = new HashSet<string>(SheetCreator.TableCharacters(book).Select(c => SheetLogic.Norm(SheetLogic.StripNote(c.Name))));
                    List<BuildCharacter> fresh = new List<BuildCharacter>();
                    foreach (GameCharacter c in game.Characters)
                    {
                        string name = (c.Name ?? "").Trim();
                        if (SheetCreator.NameFits(name) && !book.IsSkipped(name) && have.Add(SheetLogic.Norm(name)))
                        {
                            fresh.Add(new BuildCharacter(name, true, true, true));
                            added.Add(name);
                        }
                    }
                    if (fresh.Count > 0)
                    {
                        SheetCreator.AddCharacters(book, fresh, delegate { });
                        Log.Write(Tr.L("Новые персонажи добавлены сами: {0}", string.Join(", ", added)));
                    }
                }
                catch (SyncException)
                {
                    throw;   // убийства подождут следующего прохода, иначе ушли бы мимо листов
                }
                catch (Exception ex)
                {
                    Log.Write(Tr.L("Ошибка при разборе таблицы: {0}", ex));
                    throw new SyncException(Tr.F("внутренняя ошибка при работе с таблицей: {0}", ex.Message));
                }
            }
            return added;
        }

        protected override IStructuredBook LoadForTrial()
        {
            LocalBook book = File.Exists(file) ? LocalBook.Load(file) : LocalBook.Empty(file);
            book.ReadOnly = true;
            return book;
        }
    }

    /// <summary>
    /// IBook поверх JSON-файла. Листы внутри — «русские», как GoogleBook после загрузки (SheetLang). Запись атомарная:
    /// сначала во временный файл, прошлая версия остаётся рядом (.bak) и выручит, если файл окажется повреждён.
    /// </summary>
    sealed class LocalBook : IStructuredBook
    {
        const int Format = 1;
        /// <summary>Сколько дневных копий хранить в папке «backups».</summary>
        public const int DailyBackups = 7;

        /// <summary>
        /// Раз в день версия файла перед записью уходит в папку «backups» рядом (хранятся последние DailyBackups): «.bak» —
        /// только прошлая запись, а испорченное по ошибке могло записаться уже дважды. Не вышло — не беда: запись важнее.
        /// </summary>
        static void KeepDaily(string file)
        {
            try
            {
                string dir = Path.Combine(Path.GetDirectoryName(file), "backups");
                string stem = Path.GetFileNameWithoutExtension(file) + "-", ext = Path.GetExtension(file);
                string copy = Path.Combine(dir, stem + DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ext);
                if (File.Exists(copy))
                {
                    return;
                }
                Directory.CreateDirectory(dir);
                File.Copy(file, copy, false);
                string[] all = Directory.GetFiles(dir, stem + "*" + ext);
                Array.Sort(all, StringComparer.Ordinal);
                for (int i = 0; i < all.Length - DailyBackups; i++)
                {
                    File.Delete(all[i]);
                }
            }
            catch (Exception ex)
            {
                if (ex is OutOfMemoryException)
                {
                    throw;
                }
            }
        }

        readonly string file;
        string title = "";
        string creatorApp = "";
        SheetLang lang = SheetLang.Russian;
        readonly List<SheetEntry> entries = new List<SheetEntry>();
        readonly Dictionary<string, string> properties = new Dictionary<string, string>(StringComparer.Ordinal);
        bool dirty;
        DateTime savedAt;

        /// <summary>Проба (dryRun) и пустые данные: правки остаются в памяти, файл не меняется.</summary>
        public bool ReadOnly;

        LocalBook(string file)
        {
            this.file = file;
        }

        public string Title { get { return title; } }
        public string Url { get { return ""; } }
        public List<SheetEntry> Entries { get { return entries; } }
        public string CreatorApp { get { return creatorApp; } }
        public SheetLang Lang { get { return lang; } }

        /// <summary>Файл менялся (окно «Что фармить», команда из другого процесса) — копия в памяти устарела.</summary>
        public bool Reusable
        {
            get { return !dirty && !ReadOnly && File.Exists(file) && File.GetLastWriteTimeUtc(file) == savedAt; }
        }

        /// <summary>Данных ещё нет: ни листов, ни файла.</summary>
        public static LocalBook Empty(string file)
        {
            LocalBook book = new LocalBook(file);
            book.ReadOnly = true;
            return book;
        }

        /// <summary>Куда уходят нынешние данные, когда их заменяют дневной копией.</summary>
        public static string BeforeRestoreFile(string file)
        {
            return Path.Combine(Path.Combine(Path.GetDirectoryName(file), "backups"), "before-restore" + Path.GetExtension(file));
        }

        /// <summary>Дневные копии из папки «backups», новые первыми: день («2026-11-13») и путь.</summary>
        public static List<KeyValuePair<string, string>> DailyCopies(string file)
        {
            List<KeyValuePair<string, string>> found = new List<KeyValuePair<string, string>>();
            string dir = Path.Combine(Path.GetDirectoryName(file), "backups");
            string stem = Path.GetFileNameWithoutExtension(file) + "-", ext = Path.GetExtension(file);
            if (!Directory.Exists(dir))
            {
                return found;
            }
            string[] all = Directory.GetFiles(dir, stem + "*" + ext);
            Array.Sort(all, StringComparer.Ordinal);
            for (int i = all.Length - 1; i >= 0; i--)
            {
                string day = Path.GetFileNameWithoutExtension(all[i]).Substring(stem.Length);
                DateTime parsed;
                if (DateTime.TryParseExact(day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out parsed))
                {
                    found.Add(new KeyValuePair<string, string>(day, all[i]));
                }
            }
            return found;
        }

        /// <summary>
        /// Заменить данные дневной копией. Копию сначала читаем — испорченную не берём; нынешние данные остаются в
        /// BeforeRestoreFile. Сама копия на месте: вернуть её можно ещё раз.
        /// </summary>
        public static void Restore(string file, string copy)
        {
            if (Read(copy).Entries.Count == 0)
            {
                throw new SyncException(Tr._("в копии нет данных"));
            }
            using (Exclusive(file))
            {
                if (File.Exists(file))
                {
                    File.Copy(file, BeforeRestoreFile(file), true);
                }
                File.Copy(copy, file, true);
            }
        }

        /// <summary>Только один пишет в файл: цикл синхронизации, окна и команды из другого процесса.</summary>
        public static IDisposable Exclusive(string file)
        {
            return new FileGate(file);
        }

        sealed class FileGate : IDisposable
        {
            readonly System.Threading.Mutex mutex;
            readonly bool held;

            public FileGate(string file)
            {
                StringBuilder name = new StringBuilder("Local\\MountSheetSync.Data.");
                foreach (char c in Path.GetFullPath(file).ToLowerInvariant())
                {
                    name.Append(char.IsLetterOrDigit(c) ? c : '_');
                }
                mutex = new System.Threading.Mutex(false, name.ToString());
                try
                {
                    held = mutex.WaitOne(TimeSpan.FromSeconds(60));
                }
                catch (System.Threading.AbandonedMutexException)
                {
                    held = true;   // прошлый владелец упал; запись атомарная, файл цел
                }
                if (!held)
                {
                    mutex.Dispose();
                    throw new SyncException(Tr._("данные заняты другой операцией, попробую позже"));
                }
            }

            public void Dispose()
            {
                mutex.ReleaseMutex();
                mutex.Dispose();
            }
        }

        /// <summary>Новые данные по плану — те же листы и строки, что у новой таблицы в Google.</summary>
        public static LocalBook Create(string file, BuildPlan plan)
        {
            SheetBuilder.Validate(plan);
            if (plan.Catalog == null)
            {
                plan.Catalog = Catalog.Embedded();
            }
            LocalBook book = new LocalBook(file);
            book.title = plan.Title;
            book.lang = plan.Lang ?? SheetLang.Russian;
            book.Run(SheetBuilder.Requests(plan, 0));
            book.Run(SheetBuilder.FinishRequests(plan.Catalog, plan.CollectionStamp, book.lang));
            if (!book.lang.IsRussian)
            {
                // запросы уже на языке таблицы — внутри программы листы «русские»
                foreach (SheetEntry e in book.entries)
                {
                    e.Name = book.lang.SheetNameToRu(e.Name);
                    book.lang.ToRussian(e);
                }
            }
            book.Save();
            return book;
        }

        /// <summary>Копия таблицы из Google: все листы и служебные значения.</summary>
        public static LocalBook Import(string file, GoogleBook source)
        {
            LocalBook book = new LocalBook(file);
            book.title = source.Title;
            book.creatorApp = source.CreatorApp;
            book.lang = source.Lang;
            book.entries.AddRange(source.Entries);
            foreach (KeyValuePair<string, string> p in source.AllProperties)
            {
                book.properties[p.Key] = p.Value;
            }
            book.Save();
            return book;
        }

        public static LocalBook Load(string file)
        {
            try
            {
                return Read(file);
            }
            catch (Exception ex)
            {
                if (ex is OutOfMemoryException)
                {
                    throw;
                }
                string backup = file + ".bak";
                if (File.Exists(file) && File.Exists(backup))
                {
                    Log.Write(Tr.L("Файл данных повреждён ({0}), читаю резервную копию.", ex.Message));
                    try
                    {
                        Read(backup);
                        // копия цела — она становится основным файлом, повреждённый остаётся рядом (иначе запись затёрла бы им копию)
                        File.Copy(file, file + ".damaged", true);
                        File.Copy(backup, file, true);
                        return Read(file);
                    }
                    catch (Exception)
                    {
                    }
                }
                throw new SyncException(Tr.F("не удалось прочитать данные с диска: {0}", ex.Message));
            }
        }

        static LocalBook Read(string path)
        {
            Dictionary<string, object> d = Json.Parse(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
            if (d == null || J.Long(d, "format") != Format)
            {
                throw new FormatException("format");
            }
            LocalBook book = new LocalBook(path);
            book.savedAt = File.GetLastWriteTimeUtc(path);
            book.title = J.Str(d, "title");
            book.creatorApp = J.Str(d, "app");
            book.lang = SheetLang.Detect(J.Str(d, "lang"), new string[0]);
            Dictionary<string, object> props = J.Obj(J.Get(d, "properties"));
            if (props != null)
            {
                foreach (KeyValuePair<string, object> p in props)
                {
                    book.properties[p.Key] = Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            foreach (object o in J.Arr(J.Get(d, "sheets")))
            {
                Dictionary<string, object> s = J.Obj(o);
                SheetEntry e = new SheetEntry();
                e.Name = J.Str(s, "name");
                e.SheetId = J.Long(s, "id");
                foreach (object r in J.Arr(J.Get(s, "rows")))
                {
                    e.Data.Add(J.Arr(r).ToArray());
                }
                foreach (object f in J.Arr(J.Get(s, "formulas")))
                {
                    e.Formulas.Add(Convert.ToInt64(f));
                }
                book.entries.Add(e);
            }
            return book;
        }

        void Save()
        {
            if (ReadOnly)
            {
                return;
            }
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["format"] = Format;
            d["app"] = creatorApp;
            d["lang"] = lang.Code;
            d["title"] = title;
            Dictionary<string, object> props = new Dictionary<string, object>();
            foreach (KeyValuePair<string, string> p in properties)
            {
                props[p.Key] = p.Value;
            }
            d["properties"] = props;
            List<object> sheets = new List<object>();
            foreach (SheetEntry e in entries)
            {
                Dictionary<string, object> s = new Dictionary<string, object>();
                s["name"] = e.Name;
                s["id"] = e.SheetId;
                s["formulas"] = e.Formulas.OrderBy(k => k).Cast<object>().ToList();
                s["rows"] = e.Data.Cast<object>().ToList();
                sheets.Add(s);
            }
            d["sheets"] = sheets;
            string temp = file + ".tmp";
            try
            {
                File.WriteAllText(temp, Json.Write(d, false), new UTF8Encoding(false));
                if (File.Exists(file))
                {
                    KeepDaily(file);
                    File.Replace(temp, file, file + ".bak");
                }
                else
                {
                    File.Move(temp, file);
                }
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException))
                {
                    throw;
                }
                throw new SyncException(Tr.F("не удалось записать данные на диск: {0}", ex.Message));
            }
            dirty = false;
            savedAt = File.GetLastWriteTimeUtc(file);
        }

        public void Set(SheetEntry entry, int r, int c, object value)
        {
            entry.Data[r][c] = value;
            dirty = true;
        }

        public void Flush()
        {
            if (dirty)
            {
                Save();
            }
        }

        public void AppendColumn(SheetEntry entry, List<object> values, bool hidden)
        {
            int col = entry.Data.Count > 0 ? entry.Data[0].Length : 0;
            List<object> plain = new List<object>();
            for (int r = 0; r < values.Count; r++)
            {
                bool formula = values[r] is SheetBuilder.Formula;
                plain.Add(formula ? "" : values[r]);
                if (formula)
                {
                    entry.Formulas.Add(SheetLogic.Key(r, col));
                }
            }
            BookEdit.AppendColumn(entry, plain);
            dirty = true;
            Flush();
        }

        public void DeleteRows(Dictionary<SheetEntry, List<int>> rows)
        {
            foreach (KeyValuePair<SheetEntry, List<int>> kv in rows)
            {
                BookEdit.DeleteRows(kv.Key, kv.Value);
            }
            dirty = true;
            Flush();
        }

        /// <summary>
        /// Обновление вида (SheetUpgrade): запросы только про оформление и Google, значения вызывающий правит сам сразу после,
        /// поэтому запись — на его Flush.
        /// </summary>
        public void Apply(List<object> requests, string propertyKey, string propertyValue)
        {
            if (propertyKey != null)
            {
                properties[propertyKey] = propertyValue;
            }
            dirty = true;
        }

        const string SkippedKey = "skippedCharacters";

        List<string> Skipped()
        {
            return (GetProperty(SkippedKey) ?? "").Split('\n').Where(n => n.Length > 0).ToList();
        }

        /// <summary>Человек убрал этого персонажа сам: обратно он возвращается только галочкой.</summary>
        public bool IsSkipped(string name)
        {
            return Skipped().Any(n => SheetLogic.Norm(n) == SheetLogic.Norm(SheetLogic.StripNote(name)));
        }

        public void NoteCharacters(IEnumerable<string> removed, IEnumerable<string> kept)
        {
            List<string> was = Skipped();
            List<string> now = was.Where(n => !kept.Any(k => SheetLogic.Norm(SheetLogic.StripNote(k)) == SheetLogic.Norm(n))).ToList();
            foreach (string name in removed)
            {
                string plain = SheetLogic.StripNote(name);
                if (!now.Any(n => SheetLogic.Norm(n) == SheetLogic.Norm(plain)))
                {
                    now.Add(plain);
                }
            }
            if (!now.SequenceEqual(was))
            {
                SetProperty(SkippedKey, string.Join("\n", now));
            }
        }

        public string GetProperty(string key)
        {
            string v;
            return properties.TryGetValue(key, out v) ? v : null;
        }

        public void SetProperty(string key, string value)
        {
            properties[key] = value;
            dirty = true;
            Flush();
        }

        /// <summary>Значения из запросов SheetBuilder (листы, ячейки, колонки); оформление пропускается. Файл не пишет.</summary>
        public void Run(List<object> requests)
        {
            // через JSON: числа — double, списки — List<object>, формулы уже записаны как formulaValue
            foreach (object o in (List<object>)Json.Parse(Json.Write(requests, false)))
            {
                Dictionary<string, object> req = J.Obj(o);
                Dictionary<string, object> body;
                if ((body = J.Obj(J.Get(req, "addSheet"))) != null)
                {
                    AddSheet(J.Obj(J.Get(body, "properties")));
                }
                else if ((body = J.Obj(J.Get(req, "updateCells"))) != null)
                {
                    if (J.Str(body, "fields") == "userEnteredValue")   // остальные — только формат
                    {
                        UpdateCells(body);
                    }
                }
                else if ((body = J.Obj(J.Get(req, "deleteSheet"))) != null)
                {
                    long id = J.Long(body, "sheetId");
                    entries.RemoveAll(e => e.SheetId == id);
                }
                else if ((body = J.Obj(J.Get(req, "deleteDimension"))) != null)
                {
                    DeleteDimension(J.Obj(J.Get(body, "range")));
                }
                else if ((body = J.Obj(J.Get(req, "createDeveloperMetadata"))) != null)
                {
                    Metadata(J.Obj(J.Get(body, "developerMetadata")));
                }
            }
            foreach (SheetEntry e in entries)
            {
                Pad(e);
            }
            dirty = true;
        }

        void AddSheet(Dictionary<string, object> p)
        {
            SheetEntry e = new SheetEntry();
            e.Name = J.Str(p, "title");
            e.SheetId = J.Long(p, "sheetId");
            entries.Insert(Math.Min((int)J.Long(p, "index"), entries.Count), e);
        }

        void UpdateCells(Dictionary<string, object> body)
        {
            Dictionary<string, object> start = J.Obj(J.Get(body, "start"));
            long id = J.Long(start, "sheetId");
            SheetEntry e = entries.First(x => x.SheetId == id);
            int r = (int)J.Long(start, "rowIndex");
            int c0 = (int)J.Long(start, "columnIndex");
            foreach (object row in J.Arr(J.Get(body, "rows")))
            {
                int c = c0;
                foreach (object cell in J.Arr(J.Get(J.Obj(row), "values")))
                {
                    Store(e, r, c++, J.Obj(J.Get(J.Obj(cell), "userEnteredValue")));
                }
                r++;
            }
        }

        static void Store(SheetEntry e, int r, int c, Dictionary<string, object> value)
        {
            while (e.Data.Count <= r)
            {
                e.Data.Add(new object[0]);
            }
            object[] row = e.Data[r];
            if (row.Length <= c)
            {
                Array.Resize(ref row, c + 1);
                for (int i = 0; i < row.Length; i++)
                {
                    row[i] = row[i] ?? "";
                }
                e.Data[r] = row;
            }
            long key = SheetLogic.Key(r, c);
            e.Formulas.Remove(key);
            row[c] = "";
            if (value == null)
            {
                return;
            }
            if (value.ContainsKey("formulaValue"))
            {
                e.Formulas.Add(key);   // считает таблица в Google; здесь её значения никто не читает
            }
            else if (value.ContainsKey("stringValue"))
            {
                row[c] = J.Str(value, "stringValue");
            }
            else if (value.ContainsKey("numberValue") || value.ContainsKey("boolValue"))
            {
                row[c] = J.Get(value, value.ContainsKey("boolValue") ? "boolValue" : "numberValue");
            }
        }

        void DeleteDimension(Dictionary<string, object> range)
        {
            long id = J.Long(range, "sheetId");
            SheetEntry e = entries.FirstOrDefault(x => x.SheetId == id);
            if (e == null)
            {
                return;
            }
            int from = (int)J.Long(range, "startIndex");
            int to = (int)J.Long(range, "endIndex");
            if (J.Str(range, "dimension") == "ROWS")
            {
                BookEdit.DeleteRows(e, Enumerable.Range(from, to - from));
                return;
            }
            int count = to - from;
            for (int r = 0; r < e.Data.Count; r++)
            {
                e.Data[r] = e.Data[r].Where((v, c) => c < from || c >= to).ToArray();
            }
            HashSet<long> formulas = new HashSet<long>();
            foreach (long key in e.Formulas)
            {
                int r = (int)(key / 100000);
                int c = (int)(key % 100000);
                if (c < from)
                {
                    formulas.Add(key);
                }
                else if (c >= to)
                {
                    formulas.Add(SheetLogic.Key(r, c - count));
                }
            }
            e.Formulas.Clear();
            e.Formulas.UnionWith(formulas);
        }

        void Metadata(Dictionary<string, object> m)
        {
            string key = J.Str(m, "metadataKey");
            string value = J.Str(m, "metadataValue");
            if (key == SheetBuilder.MetadataKey)
            {
                creatorApp = J.Str(Json.Parse(value) as Dictionary<string, object>, "app");
            }
            else if (key.StartsWith(GoogleBook.PropertyPrefix, StringComparison.Ordinal))
            {
                properties[key.Substring(GoogleBook.PropertyPrefix.Length)] = value;
            }
        }

        static void Pad(SheetEntry e)
        {
            int width = e.Data.Count == 0 ? 0 : e.Data.Max(r => r.Length);
            for (int r = 0; r < e.Data.Count; r++)
            {
                object[] row = e.Data[r];
                if (row.Length < width)
                {
                    int old = row.Length;
                    Array.Resize(ref row, width);
                    for (int c = old; c < width; c++)
                    {
                        row[c] = "";
                    }
                    e.Data[r] = row;
                }
            }
        }
    }
}
