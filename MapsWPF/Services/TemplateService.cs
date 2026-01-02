using MapsWPF.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MapsWPF.Services
{
    public class TemplateService
    {
        public Dictionary<string, List<string>> Position_Point { get; set; } = new Dictionary<string, List<string>>();
        public Dictionary<string, List<string>> DroneByPosition { get; set; } = new Dictionary<string, List<string>>();

        public List<string> LocalCiti { get; set; } = new List<string>();
        public List<string> StartWorkShablon { get; set; } = new List<string>();
        public List<string> EndWorkShablon { get; set; } = new List<string>();
        public List<string> ReportWorkShablon { get; set; } = new List<string>();
        // Targets list (moved from Template TargetTypeShablon into targets.json)
        public List<string> Targets { get; set; } = new List<string>();
        public List<string> TargetTypeShablon { get; set; } = new List<string>(); // backward compatibility

        // templates map (template name -> lines)
        public Dictionary<string, List<string>> Templates { get; set; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public string CustomUnit { get; set; } = "зрдн";
        public string LaunchArea { get; set; } = "Купянськ";

        public List<string> CustomReportWorkShablon { get; set; }

        // Histories for UI comboboxes
        public List<string> UnitsHistory { get; set; } = new List<string>();
        public List<string> LaunchAreasHistory { get; set; } = new List<string>();

        // Last selections to persist UI state across runs
        public string LastHeight { get; set; }
        public string LastSelectedPosition { get; set; }
        public string LastSelectedPilot { get; set; }
        public string LastSelectedDrone { get; set; }
        public string LastSelectedTarget { get; set; }
        public bool RotateAfterGenerate { get; set; } = false;

        public TemplateService()
        {
            if (CustomReportWorkShablon == null) CustomReportWorkShablon = new List<string>();
            UnitsHistory = new List<string>();
            LaunchAreasHistory = new List<string>();
        }

        public void InitializeData()
        {
            Position_Point = new Dictionary<string, List<string>>
            {
                ["ФОРПОСТ"] = new List<string>{ "GREENDAY", "GREENDAY, Kasper" },
                ["ДЕТРОЙТ"] = new List<string>{ "GREENDAY", "Kasper", "Volt" }
            };

            DroneByPosition = new Dictionary<string, List<string>>
            {
                ["ФОРПОСТ"] = new List<string>{ "BARABASH MAX FLY", "BARABASH 10", "BLINK 8", "F7", "СПОРТИВНИЙ ПОВІТРЯНИЙ РОБОТ", "PILUM 10" },
                ["ДЕТРОЙТ"] = new List<string>{ "Дикі шершні '10'", "Rusoriz '10'" }
            };

            LocalCiti = new List<string>
            {
                "Купянськ", "Подоли", "Соболівка", "Курилівка", "Осиново", "Петропавлівка", "Голубівка",
                "Садове", "Благодатівка", "Московка", "Кіндрашівка", "Калинове", "Синьківка", "Радьківка",
            };

            StartWorkShablon = new List<string>
            {
                "Підрозділ: 14 омбр {UnitName}",
                "Екіпаж: “{Position}”",
                "Пілот: “{Pilot}”",
                "Тип засобу: FPV “{DroneBy}”",
                "Район зльоту: {LaunchArea}",
                "Частоти: {Frequencies}",
                "Висота: {height}",
                "Час роботи: “{Time}”",
                "Мета польоту: {Purpose}",
                "Напрямок польоту: {Direction}",
                "Ціль №: {ShootingTarget}"
            };

            EndWorkShablon = new List<string>
            {
                "Підрозділ: 14 омбр {UnitName}",
                "Роботу закінчили: “{Time}”",
                "{TargetStatus}"
            };

            ReportWorkShablon = new List<string>
            {
                "{Time} БпЛА-П №1 “{Position}” {UnitName} 14 омбр,",
                "{nearestLocality} кв. ({MGRS_Short})",
                "виявлено БпЛА “{TargetType}” (А - {azimyth}, Д - {range}, В - {height}).",
                "Застосовано FPV дрон-перехоплювач мультироторного типу “{DroneBy}”, денний.",
                "Ціль {TargetStatus}.",
                "{Expenses} {AdditionalInfo} виявлення і супроводження DELTA-ВЕЖА, Цілевказівка КП зрдн."
            };

            TargetTypeShablon = new List<string>
            {
                "Молнія 2", "Зала", "Куб", "Орлан", "Ланцет", "Суперкам"
            };

            // initialize Targets list from old TargetTypeShablon by default
            Targets = new List<string>(TargetTypeShablon);

            // initialize templates map (template name -> lines)
            Templates = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Report"] = new List<string>(ReportWorkShablon ?? new List<string>()),
                ["StartWork"] = new List<string>(StartWorkShablon ?? new List<string>()),
                ["EndWork"] = new List<string>(EndWorkShablon ?? new List<string>())
            };

            if (CustomReportWorkShablon == null)
            {
                CustomReportWorkShablon = new List<string>();
            }
        }

        // Folder next to executable where settings JSON files are stored
        public string SettingsFolderPath => Path.Combine(AppContext.BaseDirectory ?? ".", "settings");

        public void LoadAllData()
        {
            // Migrate legacy files (droneby.json → positions.json wrapper) if present
            var folder = SettingsFolderPath;
            if (Directory.Exists(folder))
            {
                MigrateOldPositionFiles(folder);
            }

            // Try to load from settings folder next to exe first
            if (!TryLoadFromSettingsFolder())
            {
                // Fallback to built-in defaults
                InitializeData();
                // Persist defaults to settings folder for easier user inspection and customization
                TrySaveDefaultsToSettingsFolder();
            }
        }

        private bool TryLoadFromSettingsFolder()
        {
            try
            {
                var folder = SettingsFolderPath;
                if (!Directory.Exists(folder)) return false;

                bool loaded = false;
                var templatesPath = Path.Combine(folder, "templates.json");
                if (File.Exists(templatesPath))
                {
                    var json = File.ReadAllText(templatesPath);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        // Try parsing as JObject to preserve property order as defined in JSON file
                        try
                        {
                            var jobj = JObject.Parse(json);
                            if (jobj != null && jobj.Properties().Any())
                            {
                                var tmpMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                                foreach (var prop in jobj.Properties())
                                {
                                    var name = prop.Name;
                                    var val = prop.Value;
                                    List<string> lines = new List<string>();
                                    if (val is JArray arr)
                                    {
                                        foreach (var it in arr) lines.Add(it.ToString());
                                    }
                                    else if (val != null)
                                    {
                                        var s = val.ToString();
                                        lines = s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s2 => s2.Trim()).Where(s2 => !string.IsNullOrEmpty(s2)).ToList();
                                    }
                                    tmpMap[name] = lines;
                                }
                                Templates = new Dictionary<string, List<string>>(tmpMap, StringComparer.OrdinalIgnoreCase);
                                // keep legacy properties in sync for backward compat if needed
                                if (Templates.ContainsKey("StartWork")) StartWorkShablon = Templates["StartWork"];
                                if (Templates.ContainsKey("EndWork")) EndWorkShablon = Templates["EndWork"];
                                if (Templates.ContainsKey("Report")) ReportWorkShablon = Templates["Report"];
                                loaded = true;
                            }
                        }
                        catch { }

                        // Fallback to older dynamic parsing if needed
                        if (!loaded)
                        {
                            dynamic d = JsonConvert.DeserializeObject(json);
                            if (d != null)
                            {
                                if (d.StartWorkShablon != null)
                                {
                                    if (d.StartWorkShablon is IEnumerable<object> arr) { var tmp = new List<string>(); foreach (var o in arr) tmp.Add(o.ToString()); StartWorkShablon = tmp; }
                                    else { string tmpStr = ((object)d.StartWorkShablon).ToString(); StartWorkShablon = tmpStr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList(); }
                                }
                                if (d.EndWorkShablon != null)
                                {
                                    if (d.EndWorkShablon is IEnumerable<object> arr) { var tmp = new List<string>(); foreach (var o in arr) tmp.Add(o.ToString()); EndWorkShablon = tmp; }
                                    else { string tmpStr = ((object)d.EndWorkShablon).ToString(); EndWorkShablon = tmpStr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList(); }
                                }
                                if (d.ReportWorkShablon != null)
                                {
                                    if (d.ReportWorkShablon is IEnumerable<object> arr) { var tmp = new List<string>(); foreach (var o in arr) tmp.Add(o.ToString()); ReportWorkShablon = tmp; }
                                    else { string tmpStr = ((object)d.ReportWorkShablon).ToString(); ReportWorkShablon = tmpStr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList(); }
                                }
                                if (d.TargetTypeShablon != null)
                                {
                                    if (d.TargetTypeShablon is IEnumerable<object> arr) { var tmp = new List<string>(); foreach (var o in arr) tmp.Add(o.ToString()); TargetTypeShablon = tmp; }
                                    else { string tmpStr = ((object)d.TargetTypeShablon).ToString(); TargetTypeShablon = tmpStr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList(); }
                                }
                                loaded = true;
                            }
                        }
                    }
                }

                // load targets.json if available (preferred over TargetTypeShablon)
                try
                {
                    var targetsPath = Path.Combine(folder, "targets.json");
                    if (File.Exists(targetsPath))
                    {
                        var tgtJson = File.ReadAllText(targetsPath);
                        if (!string.IsNullOrWhiteSpace(tgtJson))
                        {
                            var jobj = JsonConvert.DeserializeObject(tgtJson);
                            if (jobj != null)
                            {
                                // Expecting object { Targets: [ ... ] } or plain array
                                try
                                {
                                    var jo = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JToken>(tgtJson);
                                    if (jo.Type == Newtonsoft.Json.Linq.JTokenType.Array)
                                    {
                                        Targets = jo.Values<string>().Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                                    }
                                    else if (jo.Type == Newtonsoft.Json.Linq.JTokenType.Object && jo["Targets"] != null)
                                    {
                                        var tkn = jo["Targets"];
                                        if (tkn.Type == Newtonsoft.Json.Linq.JTokenType.Array) Targets = tkn.Values<string>().Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                                    }
                                    loaded = true;
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch { }

                var positionsPath = Path.Combine(folder, "positions.json");
                if (File.Exists(positionsPath))
                {
                    var json = File.ReadAllText(positionsPath);
                    try { System.IO.Directory.CreateDirectory(SettingsFolderPath); System.IO.File.AppendAllText(System.IO.Path.Combine(SettingsFolderPath, "diagnostics.log"), DateTime.Now.ToString("o") + " Loading positions.json (len=" + (json?.Length ?? 0) + ")" + Environment.NewLine); } catch { }
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        // Support several forms:
                        // 1) dictionary<string, List<string>> where keys are positions
                        // 2) wrapper object { Position_Point: {...}, DroneByPosition: {...}, Pilots: [...] }
                        // 3) mixed top-level object: keys are positions (arrays) and additionally a DroneByPosition object
                        // First try to parse as a simple dictionary<string, List<string>>; if that throws, try JObject-based parsing below
                        bool parsed = false;
                        try
                        {
                            var jobjDict = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(json);
                            if (jobjDict != null && jobjDict.Count > 0)
                            {
                                Position_Point = jobjDict;
                                loaded = true;
                                parsed = true;
                                try { System.IO.File.AppendAllText(System.IO.Path.Combine(SettingsFolderPath, "diagnostics.log"), DateTime.Now.ToString("o") + " positions parsed as dictionary, count=" + Position_Point.Count + Environment.NewLine); } catch { }
                            }
                        }
                        catch (Exception ex)
                        {
                            try { System.IO.File.AppendAllText(System.IO.Path.Combine(SettingsFolderPath, "diagnostics.log"), DateTime.Now.ToString("o") + " positions dictionary parse failed: " + ex.Message + Environment.NewLine); } catch { }
                        }

                        if (!parsed)
                        {
                            try
                            {
                                // Parse as JObject to handle mixed/wrapper formats robustly
                                var j = Newtonsoft.Json.Linq.JObject.Parse(json);
                                bool any = false;
                                // If wrapper with Position_Point exists, prefer that
                                if (j["Position_Point"] != null)
                                {
                                    try
                                    {
                                        var pp = j["Position_Point"].ToObject<Dictionary<string, List<string>>?>();
                                        if (pp != null && pp.Count > 0) { Position_Point = pp; any = true; try { System.IO.File.AppendAllText(System.IO.Path.Combine(SettingsFolderPath, "diagnostics.log"), DateTime.Now.ToString("o") + " positions parsed from wrapper Position_Point, count=" + Position_Point.Count + Environment.NewLine); } catch { } }
                                    }
                                    catch { try { System.IO.File.AppendAllText(System.IO.Path.Combine(SettingsFolderPath, "diagnostics.log"), DateTime.Now.ToString("o") + " positions wrapper parsing failed" + Environment.NewLine); } catch { } }
                                }
                                // If top-level contains position keys (arrays), use them
                                foreach (var prop in j.Properties())
                                {
                                    try { System.IO.File.AppendAllText(System.IO.Path.Combine(SettingsFolderPath, "diagnostics.log"), DateTime.Now.ToString("o") + " positions property: " + prop.Name + " type=" + prop.Value.Type + Environment.NewLine); } catch { }
                                    if (prop.Name.Equals("DroneByPosition", StringComparison.OrdinalIgnoreCase)) continue;
                                    if (prop.Value.Type == Newtonsoft.Json.Linq.JTokenType.Array)
                                    {
                                        try
                                        {
                                            var arr = prop.Value.ToObject<List<string>>();
                                            try { System.IO.File.AppendAllText(System.IO.Path.Combine(SettingsFolderPath, "diagnostics.log"), DateTime.Now.ToString("o") + " positions property array count for " + prop.Name + " = " + (arr?.Count ?? 0) + Environment.NewLine); } catch { }
                                            if (arr != null && arr.Count > 0)
                                            {
                                                Position_Point ??= new Dictionary<string, List<string>>();
                                                Position_Point[prop.Name] = arr;
                                                any = true;
                                            }
                                        }
                                        catch (Exception ex) { try { System.IO.File.AppendAllText(System.IO.Path.Combine(SettingsFolderPath, "diagnostics.log"), DateTime.Now.ToString("o") + " positions property parsing failed for " + prop.Name + ": " + ex.Message + Environment.NewLine); } catch { } }
                                    }
                                    else
                                    {
                                        try { System.IO.File.AppendAllText(System.IO.Path.Combine(SettingsFolderPath, "diagnostics.log"), DateTime.Now.ToString("o") + " positions property skipped (not array): " + prop.Name + Environment.NewLine); } catch { }
                                    }
                                }
                                // DroneByPosition may be present as a nested object or a top-level property
                                if (j["DroneByPosition"] != null)
                                {
                                    try
                                    {
                                        var dp = j["DroneByPosition"].ToObject<Dictionary<string, List<string>>?>();
                                        if (dp != null && dp.Count > 0) { DroneByPosition = dp; any = true; try { System.IO.File.AppendAllText(System.IO.Path.Combine(SettingsFolderPath, "diagnostics.log"), DateTime.Now.ToString("o") + " DroneByPosition parsed, count=" + DroneByPosition.Count + Environment.NewLine); } catch { } }
                                    }
                                    catch { try { System.IO.File.AppendAllText(System.IO.Path.Combine(SettingsFolderPath, "diagnostics.log"), DateTime.Now.ToString("o") + " DroneByPosition parsing failed" + Environment.NewLine); } catch { } }
                                }
                                if (any) loaded = true;
                                try { System.IO.File.AppendAllText(System.IO.Path.Combine(SettingsFolderPath, "diagnostics.log"), DateTime.Now.ToString("o") + " positions any=" + any + " Position_Point.Count=" + (Position_Point?.Count ?? 0) + Environment.NewLine); } catch { }
                            }
                            catch (Exception ex)
                            {
                                try { System.IO.File.AppendAllText(System.IO.Path.Combine(SettingsFolderPath, "diagnostics.log"), DateTime.Now.ToString("o") + " positions top-level parse exception: " + ex + Environment.NewLine); } catch { }
                            }
                        }
                    }
                }

                // Backwards-compatible: separate droneby.json still supported
                var dronesPath = Path.Combine(folder, "droneby.json");
                if (File.Exists(dronesPath))
                {
                    var json = File.ReadAllText(dronesPath);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var jobj = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(json);
                        if (jobj != null)
                        {
                            DroneByPosition = jobj;
                            loaded = true;
                        }
                    }
                }

                var unitsPath = Path.Combine(folder, "units.json");
                if (File.Exists(unitsPath))
                {
                    var json = File.ReadAllText(unitsPath);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        try
                        {
                            dynamic u = JsonConvert.DeserializeObject(json);
                            if (u != null)
                            {
                                if (u.UnitsHistory != null)
                                    if (u.UnitsHistory is IEnumerable<object> arru) { var tmpu = new List<string>(); foreach (var o in arru) tmpu.Add(o.ToString()); UnitsHistory = tmpu; }
                                if (u.CustomUnit != null) CustomUnit = u.CustomUnit.ToString();
                                loaded = true;
                            }
                        }
                        catch
                        {
                            // file may be plain array of units
                            try
                            {
                                var arr = JsonConvert.DeserializeObject<List<string>>(json);
                                if (arr != null && arr.Count > 0)
                                {
                                    UnitsHistory = arr;
                                    CustomUnit = arr.FirstOrDefault();
                                    loaded = true;
                                }
                            }
                            catch { }
                        }
                    }
                }



                var launchAreasPath = Path.Combine(folder, "launchareas.json");
                if (File.Exists(launchAreasPath))
                {
                    var json = File.ReadAllText(launchAreasPath);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        try
                        {
                            dynamic la = JsonConvert.DeserializeObject(json);
                            if (la != null && la.LaunchAreasHistory != null)
                            {
                                if (la.LaunchAreasHistory is IEnumerable<object> arrla) { var tmpla = new List<string>(); foreach (var o in arrla) tmpla.Add(o.ToString()); LaunchAreasHistory = tmpla; }
                                if (la.LaunchArea != null) LaunchArea = la.LaunchArea.ToString();
                                loaded = true;
                            }
                        }
                        catch
                        {
                            // support plain array
                            try
                            {
                                var arr = JsonConvert.DeserializeObject<List<string>>(json);
                                if (arr != null && arr.Count > 0)
                                {
                                    LaunchAreasHistory = arr;
                                    LaunchArea = arr.FirstOrDefault();
                                    loaded = true;
                                }
                            }
                            catch { }
                        }
                    }
                }

                // load lastchoices.json if present
                try
                {
                    var lastPath = Path.Combine(folder, "lastchoices.json");
                    if (File.Exists(lastPath))
                    {
                        var jsonLast = File.ReadAllText(lastPath);
                        if (!string.IsNullOrWhiteSpace(jsonLast))
                        {
                            try
                            {
                                dynamic last = JsonConvert.DeserializeObject(jsonLast);
                                if (last != null)
                                {
                                    if (last.LastHeight != null) LastHeight = last.LastHeight.ToString();
                                    if (last.LastSelectedPosition != null) LastSelectedPosition = last.LastSelectedPosition.ToString();
                                    if (last.LastSelectedPilot != null) LastSelectedPilot = last.LastSelectedPilot.ToString();
                                    if (last.LastSelectedDrone != null) LastSelectedDrone = last.LastSelectedDrone.ToString();
                                    if (last.LastSelectedTarget != null) LastSelectedTarget = last.LastSelectedTarget.ToString();
                                    if (last.RotateAfterGenerate != null) RotateAfterGenerate = Convert.ToBoolean(last.RotateAfterGenerate);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }

                return loaded;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading settings from folder: {ex.Message}");
                return false;
            }
        }

        private void TrySaveDefaultsToSettingsFolder()
        {
            try
            {
                var folder = SettingsFolderPath;
                Directory.CreateDirectory(folder);

                var templatesPath = Path.Combine(folder, "templates.json");
                if (!File.Exists(templatesPath))
                {
                    var obj = new
                    {
                        StartWorkShablon = StartWorkShablon,
                        EndWorkShablon = EndWorkShablon,
                        ReportWorkShablon = ReportWorkShablon
                    };
                    File.WriteAllText(templatesPath, JsonConvert.SerializeObject(obj, Formatting.Indented));
                }

                var positionsPath = Path.Combine(folder, "positions.json");
                if (!File.Exists(positionsPath))
                {
                    var wrapper = new { Position_Point = Position_Point, DroneByPosition = DroneByPosition };
                    File.WriteAllText(positionsPath, JsonConvert.SerializeObject(wrapper, Formatting.Indented));
                }

                var unitsPath = Path.Combine(folder, "units.json");
                if (!File.Exists(unitsPath))
                {
                    var obj = new { CustomUnit = CustomUnit, UnitsHistory = UnitsHistory };
                    File.WriteAllText(unitsPath, JsonConvert.SerializeObject(obj, Formatting.Indented));
                }

                var laPath = Path.Combine(folder, "launchareas.json");
                if (!File.Exists(laPath))
                {
                    var obj = new { LaunchArea = LaunchArea, LaunchAreasHistory = LaunchAreasHistory };
                    File.WriteAllText(laPath, JsonConvert.SerializeObject(obj, Formatting.Indented));
                }

                // create targets.json from TargetTypeShablon for backwards compatibility
                var targetsPath = Path.Combine(folder, "targets.json");
                if (!File.Exists(targetsPath))
                {
                    var t = Targets != null && Targets.Count > 0 ? Targets : TargetTypeShablon;
                    if (t == null) t = new List<string>();
                    // always create targets.json (may be empty) so user can edit it later
                    File.WriteAllText(targetsPath, JsonConvert.SerializeObject(new { Targets = t }, Formatting.Indented));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving defaults to settings folder: {ex.Message}");
            }
        }

        public void SaveTargetsToSettingsFolder()
        {
            try
            {
                var folder = SettingsFolderPath;
                Directory.CreateDirectory(folder);
                var targetsPath = Path.Combine(folder, "targets.json");
                var obj = new { Targets = Targets ?? new List<string>() };
                File.WriteAllText(targetsPath, JsonConvert.SerializeObject(obj, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving targets to settings folder: {ex.Message}");
            }
            SaveLastChoices();
        }

        // Public helpers for templates and lists used by UI and controllers
        public List<string> ReportWorkShablonActual => (CustomReportWorkShablon != null && CustomReportWorkShablon.Count > 0) ? CustomReportWorkShablon : ReportWorkShablon;



        public void SaveTemplateByName(string name, List<string> template)
        {
            if (string.IsNullOrWhiteSpace(name) || template == null) return;
            if (Templates == null) Templates = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            Templates[name.Trim()] = template;

            // keep legacy fields in sync for other parts of code that still use them
            if (string.Equals(name, "Report", StringComparison.OrdinalIgnoreCase)) ReportWorkShablon = template;
            if (string.Equals(name, "StartWork", StringComparison.OrdinalIgnoreCase)) StartWorkShablon = template;
            if (string.Equals(name, "EndWork", StringComparison.OrdinalIgnoreCase)) EndWorkShablon = template;

            SaveTemplatesToSettingsFolder();
        }

        public List<string> GetTemplateByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return new List<string>();
            if (Templates != null && Templates.TryGetValue(name.Trim(), out var t) && t != null) return t;
            // fallback to legacy names
            switch ((name ?? string.Empty).ToLowerInvariant())
            {
                case "startwork": case "start": return StartWorkShablon ?? new List<string>();
                case "endwork": case "end": return EndWorkShablon ?? new List<string>();
                default: return ReportWorkShablon ?? new List<string>();
            }
        }

        public void SaveTemplatesToSettingsFolder()
        {
            try
            {
                var folder = SettingsFolderPath;
                Directory.CreateDirectory(folder);
                var templatesPath = Path.Combine(folder, "templates.json");
                var map = Templates ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                File.WriteAllText(templatesPath, JsonConvert.SerializeObject(map, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving templates to settings folder: {ex.Message}");
            }
            SaveLastChoices();
        }

        public void SaveLastChoices()
        {
            try
            {
                var folder = SettingsFolderPath;
                Directory.CreateDirectory(folder);
                var lastPath = Path.Combine(folder, "lastchoices.json");
                var obj = new
                {
                    LastHeight = LastHeight,
                    LastSelectedPosition = LastSelectedPosition,
                    LastSelectedPilot = LastSelectedPilot,
                    LastSelectedDrone = LastSelectedDrone,
                    LastSelectedTarget = LastSelectedTarget,
                    RotateAfterGenerate = RotateAfterGenerate
                };
                File.WriteAllText(lastPath, JsonConvert.SerializeObject(obj, Formatting.Indented));
            }
            catch { }
        }

        public void SaveLaunchArea(string la)
        {
            if (string.IsNullOrWhiteSpace(la)) return;
            if (LaunchAreasHistory == null) LaunchAreasHistory = new List<string>();
            if (!LaunchAreasHistory.Contains(la)) LaunchAreasHistory.Add(la);
            LaunchArea = la;
            try { var folder = SettingsFolderPath; Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "launchareas.json"), JsonConvert.SerializeObject(new { LaunchArea = LaunchArea, LaunchAreasHistory = LaunchAreasHistory }, Formatting.Indented)); } catch { }
            SaveLastChoices();
        }

        public void SaveCustomUnit(string unit)
        {
            if (string.IsNullOrWhiteSpace(unit)) return;
            if (UnitsHistory == null) UnitsHistory = new List<string>();
            if (!UnitsHistory.Contains(unit)) UnitsHistory.Add(unit);
            CustomUnit = unit;
            try { var folder = SettingsFolderPath; Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "units.json"), JsonConvert.SerializeObject(new { CustomUnit = CustomUnit, UnitsHistory = UnitsHistory }, Formatting.Indented)); } catch { }
            SaveLastChoices();
        }

        private void MigrateOldPositionFiles(string folder)
        {
            try
            {
                var dronesPath = Path.Combine(folder, "droneby.json");
                var positionsPath = Path.Combine(folder, "positions.json");
                if (!File.Exists(dronesPath)) return;

                string json = File.ReadAllText(dronesPath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    try { if (File.Exists(dronesPath + ".bak")) File.Delete(dronesPath + ".bak"); File.Move(dronesPath, dronesPath + ".bak"); } catch { }
                    return;
                }

                Dictionary<string, List<string>> drones = null;
                try { drones = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(json); } catch { }
                if (drones == null || drones.Count == 0)
                {
                    try { if (File.Exists(dronesPath + ".bak")) File.Delete(dronesPath + ".bak"); File.Move(dronesPath, dronesPath + ".bak"); } catch { }
                    return;
                }

                if (File.Exists(positionsPath))
                {
                    try
                    {
                        var posJson = File.ReadAllText(positionsPath);
                        if (!string.IsNullOrWhiteSpace(posJson))
                        {
                            var wrapper = JsonConvert.DeserializeObject<dynamic>(posJson);
                            if (wrapper != null)
                            {
                                if (wrapper.DroneByPosition == null)
                                {
                                    wrapper.DroneByPosition = JToken.FromObject(drones);
                                    File.WriteAllText(positionsPath, JsonConvert.SerializeObject(wrapper, Formatting.Indented));
                                }
                                else
                                {
                                    var existing = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(wrapper.DroneByPosition.ToString());
                                    foreach (var kv in drones)
                                    {
                                        if (!existing.ContainsKey(kv.Key)) existing[kv.Key] = kv.Value;
                                        else
                                        {
                                            var list = existing[kv.Key];
                                            foreach (var item in kv.Value)
                                            {
                                                if (!list.Contains(item)) list.Add(item);
                                            }
                                        }
                                    }
                                    wrapper.DroneByPosition = JToken.FromObject(existing);
                                    File.WriteAllText(positionsPath, JsonConvert.SerializeObject(wrapper, Formatting.Indented));
                                }
                            }
                        }
                    }
                    catch { }
                }
                else
                {
                    var wrapperObj = new { Position_Point = Position_Point ?? new Dictionary<string, List<string>>(), DroneByPosition = drones };
                    File.WriteAllText(positionsPath, JsonConvert.SerializeObject(wrapperObj, Formatting.Indented));
                }

                try { if (File.Exists(dronesPath + ".bak")) File.Delete(dronesPath + ".bak"); File.Move(dronesPath, dronesPath + ".bak"); } catch { }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error migrating old position files: {ex.Message}");
            }
        }
    }
}

