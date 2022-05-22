using Microsoft.Extensions.Configuration;
using System.Security;
using System.Text.RegularExpressions;
using System.Xml;

namespace vPilotModelMatcher
{
    public class Program
    {
        private static IConfigurationRoot Config
        {
            get
            {
                if (_config == null)
                    _config = new ConfigurationBuilder()
                        .AddJsonFile("appSettings.json", optional: true, reloadOnChange: true)
                        .Build();

                return _config;
            }
        }

        private static IConfigurationRoot? _config;

        public static void Main()
        {
            Console.Write("Enter rule set name: ");
#if DEBUG
            Console.WriteLine("");
            var ruleSetName = "Model Matching Rules";
#else
            var ruleSetName = "";

            if (!string.IsNullOrEmpty(Config["ruleSetName"]))
                ruleSetName = Config["ruleSetName"];
            else
                ruleSetName = Console.ReadLine();
#endif
            Console.Write($"Rule set name: \"{ruleSetName}\"");
            Console.WriteLine("");

            var aircraft = LoadAircraft();
            var airlines = LoadAirlines();
            var aircraftCfgs = LoadAircraftCfgs();
            var fltsims = ParseAircraftCfgs(aircraftCfgs);
            var rules = GenerateRules(fltsims, airlines, aircraft).ToList();

            var airlineRules = rules
                .GroupBy(r => new { r.CallsignPrefix, r.TypeCode })
                .Select(r => new ModelMatchRule
                {
                    CallsignPrefix = r.Key.CallsignPrefix,
                    TypeCode = r.Key.TypeCode,
                    ModelName = string.Join("//", r.Select(r => r.ModelName).OrderBy(r => r))
                })
                .Where(r => !string.IsNullOrWhiteSpace(r.CallsignPrefix))
                .OrderBy(r => r.CallsignPrefix)
                .ThenBy(r => r.TypeCode);

            var folderPath = ""; ;

            if (!string.IsNullOrEmpty(Config["savePath"]))
                folderPath = Path.GetFullPath(Config["savePath"]);

            var outputXml = $"<?xml version=\"1.0\" encoding=\"utf-8\"?>{Environment.NewLine}" +
                $"<ModelMatchRuleSet>{Environment.NewLine}" +
                $"{string.Join('\n', airlineRules.Select(r => $"<ModelMatchRule {(string.IsNullOrWhiteSpace(r.CallsignPrefix) ? "" : $"CallsignPrefix=\"{SecurityElement.Escape(r.CallsignPrefix)}\" ")}TypeCode=\"{SecurityElement.Escape(r.TypeCode)}\" ModelName=\"{SecurityElement.Escape(r.ModelName)}\" />"))}" +
                $"</ModelMatchRuleSet>";

            XmlDocument xmlDocument = new();
            xmlDocument.LoadXml(outputXml);
            xmlDocument.Save($"{(string.IsNullOrEmpty(folderPath) ? ruleSetName : $"{folderPath}\\{ruleSetName}")} airlines.vmr");

            var aircraftRules = rules
                .GroupBy(r => new { r.TypeCode })
                .Select(r => new ModelMatchRule
                {
                    TypeCode = r.Key.TypeCode,
                    ModelName = string.Join("//", r.Select(r => r.ModelName).OrderBy(r => r))
                })
                .OrderBy(r => r.TypeCode)
                .ToList();

            outputXml = $"<?xml version=\"1.0\" encoding=\"utf-8\"?>{Environment.NewLine}" +
                $"<ModelMatchRuleSet>{Environment.NewLine}" +
                $"{string.Join('\n', aircraftRules.Select(r => $"<ModelMatchRule TypeCode=\"{SecurityElement.Escape(r.TypeCode)}\" ModelName=\"{SecurityElement.Escape(r.ModelName)}\" />"))}" +
                $"</ModelMatchRuleSet>";

            xmlDocument = new();
            xmlDocument.LoadXml(outputXml);
            xmlDocument.Save($"{(string.IsNullOrEmpty(folderPath) ? ruleSetName : $"{folderPath}\\{ruleSetName}")} aircraft.vmr");

            Console.WriteLine(aircraftCfgs.Count);
            Console.ReadLine();
        }

        private static IEnumerable<Aircraft> LoadAircraft()
        {
            var aircraft = typeof(Program).Assembly.GetManifestResourceStream("vPilotModelMatcher.Aircraft.txt");

            if (aircraft == null)
                throw new NullReferenceException($"{nameof(aircraft)} cannot be null");

            using var streamReader = new StreamReader(aircraft);

            return streamReader
                .ReadToEnd()
                .Split(new[] { "\r\n" }, StringSplitOptions.None)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Where(line => line.Split(',')[9].Length > 2)
                .Select(line =>
                {
                    var split = line.Split(',');

                    return new Aircraft
                    {
                        manufacturer_code = split[0],
                        model_no = split[1],
                        model_name = split[2],
                        tdesig = line.Split(',')[9]
                    };
                });
        }

        private static IEnumerable<Airline> LoadAirlines()
        {
            var airlines = typeof(Program).Assembly.GetManifestResourceStream("vPilotModelMatcher.Airlines.txt");

            if (airlines == null)
                throw new NullReferenceException($"{nameof(airlines)} cannot be null");

            using var streamReader = new StreamReader(airlines);

            return streamReader
                .ReadToEnd()
                .Split(new[] { "\r\n" }, StringSplitOptions.None)
                .Skip(1)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line =>
                {
                    var split = line.Split(',');

                    return new Airline
                    {
                        operator_code = split[0],
                        operator_name = split[1],
                        country = split[3]
                    };
                });
        }

        private static Dictionary<string, List<string>> LoadAircraftCfgs()
        {
            Console.Write("Enter path to folder: ");
#if DEBUG
            Console.WriteLine("");
            var folderPath = $"{Directory.GetCurrentDirectory()}\\TestData\\TestData.Ruben";
#else
            var folderPath = "";

            if (!string.IsNullOrEmpty(Config["scanPath"]))
                folderPath = Path.GetFullPath(Config["scanPath"]);
            else
                folderPath = Console.ReadLine();
#endif
            Console.Write($"Path used \"{folderPath}\"");
            Console.WriteLine("");

            var aircraftCfgs = Directory.EnumerateFiles(folderPath, "*.cfg", SearchOption.AllDirectories);

            var models = new Dictionary<string, List<string>>();

            foreach (var cfg in aircraftCfgs)
            {
                var data = File.ReadAllLines(cfg)
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    //Remove comment lines
                    .Where(line => !line.StartsWith("/"))
                    .Where(line => !line.StartsWith("#"))
                    //Select line with index
                    .Select((line, index) => new KeyValuePair<int, string>(index, line))
                    .ToList();

                while (data.Any(d => Regex.IsMatch(d.Value, @"^\[fltsim\.\d")))
                {
                    var nextFltsimIndex =
                        data
                            .Where(d => Regex.IsMatch(d.Value, @"^\[fltsim\.\d"))
                            .First()
                            .Key;

                    if (nextFltsimIndex > 0)
                    {
                        data = data
                            .Skip(nextFltsimIndex)
                            .Select((line, index) => new KeyValuePair<int, string>(index, line.Value))
                            .ToList();

                        nextFltsimIndex = 0;
                    }

                    var nextSectionIndex =
                        data.Where(d => d.Key != nextFltsimIndex).Any(d => d.Value.StartsWith("[")) ?
                            data.Where(d => d.Key != nextFltsimIndex).First(d => d.Value.StartsWith("[")).Key :
                            data.Count - 1;

                    var fltsim = data.Skip(nextFltsimIndex + 1).Take(nextSectionIndex - 1);

                    var title = fltsim.SingleOrDefault(f => f.Value.StartsWith("title="));

                    if (title.Value != null)
                        models.Add(title.Value.Split("=")[1], fltsim.Select(f => f.Value).ToList());

                    for (var i = nextSectionIndex - 1; i >= nextFltsimIndex; i--)
                        data.RemoveAt(i);

                    data = data
                        .Select((line, index) => new KeyValuePair<int, string>(index, line.Value))
                        .ToList();
                }
            }

            return models;
        }

        private static IEnumerable<Fltsim> ParseAircraftCfgs(Dictionary<string, List<string>> aircraftCfgs)
        {
            foreach (var aircraftCfg in aircraftCfgs)
            {
                var fltsim = new Fltsim();

                foreach (var line in aircraftCfg.Value)
                {
                    var split = line.Split('=');

                    switch (split[0].Trim())
                    {
                        case "title":
                            fltsim.title = split[1].Trim();
                            break;
                        case "ui_type":
                            fltsim.ui_type = split[1].Trim();
                            break;
                        case "ui_variation":
                            fltsim.ui_variation = split[1].Trim();
                            break;
                        case "atc_parking_codes":
                            fltsim.atc_parking_codes = split[1].Split(',').Select(s => s.Trim()).ToList();
                            break;
                    }
                }

                yield return fltsim;
            }
        }

        private static IEnumerable<ModelMatchRule> GenerateRules(IEnumerable<Fltsim> fltsims, IEnumerable<Airline> airlines, IEnumerable<Aircraft> aircraft)
        {
            foreach (var fltsim in fltsims)
            {
                var matchingAirlines = FindAirline(fltsim, airlines);

                if (!matchingAirlines.Any())
                    Console.WriteLine($"No airline found for {fltsim}");

                if (matchingAirlines.Count() > 1)
                    Console.WriteLine($"More than 1 airline found for {fltsim}");

                var matchingAircrafts = FindAircraft(fltsim, aircraft);

                if (!matchingAircrafts.Any())
                {
                    Console.WriteLine($"No aircraft found for {fltsim}");
                    continue;
                }

                yield return new ModelMatchRule
                {
                    CallsignPrefix = matchingAirlines.Count() == 1 ? matchingAirlines.First().operator_code : null,
                    ModelName = fltsim.title,
                    TypeCode = matchingAircrafts.First().tdesig
                };
            }
        }

        private static IEnumerable<Airline> FindAirline(Fltsim fltsim, IEnumerable<Airline> airlines)
        {
            IEnumerable<Airline> AlternativeSearch()
            {
                //What to do here?
                //matchingAirlines = airlines.Where(a => iata == null ? fltsim.ui_variation.Contains(a.operator_code) : iata.Value.ToUpper() == a.operator_code.ToUpper());
                return new List<Airline>();
            }

            var matchingAirlines = new List<Airline>().AsEnumerable();

            if (fltsim.atc_parking_codes?.Any() ?? false)
                matchingAirlines = airlines.Where(a => fltsim.atc_parking_codes.Any(apc => apc.ToUpper() == a.operator_code?.ToUpper()));

            if (!matchingAirlines.Any() || matchingAirlines.Count() > 1)
            {
                if (!string.IsNullOrWhiteSpace(fltsim.ui_variation))
                {
                    var iata = Regex.Match(fltsim.ui_variation, "(?<![A-Z]|-)[A-Z]{3}(?![A-Z])");

                    if (iata.Captures.Count == 1)
                        matchingAirlines = airlines.Where(a => iata.Value.ToUpper() == a.operator_code?.ToUpper());
                }
                else if (!string.IsNullOrWhiteSpace(fltsim.title))
                {
                    var iata = Regex.Match(fltsim.title, "(?<![A-Z]|-)[A-Z]{3}(?![A-Z])");

                    if (iata.Captures.Count == 1)
                        matchingAirlines = airlines.Where(a => iata.Value.ToUpper() == a.operator_code?.ToUpper());
                }
            }

            if (!matchingAirlines.Any())
                matchingAirlines = AlternativeSearch();

            return matchingAirlines;
        }

        private static IEnumerable<Aircraft> FindAircraft(Fltsim fltsim, IEnumerable<Aircraft> aircraft)
        {
            IEnumerable<Aircraft> AlternativeSearch()
            {
                var matchingAircraft = new List<Aircraft>().AsEnumerable();

                var ui_typeSplit = fltsim.ui_type
                    .Split(' ');

                var matchingManufacturer = aircraft.Where(a => ui_typeSplit.Select(ui => ui.ToUpper()).Contains(a.manufacturer_code.ToUpper()));

                if (matchingManufacturer.Any())
                {
                    var ui_typeSplit_numbers = ui_typeSplit
                        .Select(ui => Regex.Replace(ui, "[^0-9.]", ""))
                        .Where(ui => !string.IsNullOrEmpty(ui));

                    matchingAircraft = matchingManufacturer
                        .Where(a => !string.IsNullOrWhiteSpace(a.model_no))
                        .Where(a => ui_typeSplit_numbers.Any(ui => a.model_no.Contains(ui)))
                        .OrderByDescending(a => a.model_no.Length);

                    var matchingTdesig = matchingAircraft.Where(a => ui_typeSplit_numbers.Any(ui => a.tdesig.Contains(ui)));

                    if (matchingTdesig.GroupBy(a => a.tdesig).Count() == 1)
                        return matchingTdesig;
                }
                else
                {
                    matchingAircraft = aircraft
                        .Where(a => fltsim.ui_type.Replace("-", "").Contains(a.tdesig))
                        .OrderByDescending(a => a.tdesig.Length);
                }

                if (!matchingAircraft.Any())
                    matchingAircraft = aircraft
                        .Where(a => !string.IsNullOrWhiteSpace(a.model_no))
                        .Where(a => fltsim.ui_type.Contains(a.model_no))
                        .OrderByDescending(a => a.model_no.Length);

                return matchingAircraft;
            }

            var ui_typeSplit = fltsim.ui_type
                   .Split(' ')
                   .Select(s => s.Trim())
                   .ToList();

            ui_typeSplit.AddRange(ui_typeSplit.Select(ui => ui.Replace("-", "")).ToList());

            ui_typeSplit.AddRange(ui_typeSplit
                .Select(ui => Regex.Replace(ui, "[^0-9.-]", ""))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList());

            ui_typeSplit = ui_typeSplit.Distinct().ToList();

            var matchingManufacturer = aircraft.Where(a => ui_typeSplit.Select(ui => ui.ToUpper()).Contains(a.manufacturer_code.ToUpper()));

            if (matchingManufacturer.Any())
            {
                var matchingAircraft = matchingManufacturer
                    .Where(a => !string.IsNullOrWhiteSpace(a.model_no))
                    .Where(a => ui_typeSplit.Any(ui => ui.Contains(a.model_no)))
                    .OrderByDescending(a => a.model_no.Length)
                    .AsEnumerable();

                matchingAircraft = matchingAircraft.Concat(matchingManufacturer
                    .Where(a => ui_typeSplit.Any(ui => ui.Contains(a.tdesig)))
                    .OrderByDescending(a => a.model_no.Length));

                var tdesigContains = matchingAircraft
                    .Where(a => ui_typeSplit.Any(ui => a.tdesig.Contains(ui)))
                    .GroupBy(a => a.tdesig);

                if (tdesigContains.Count() == 1)
                    return matchingAircraft.Where(a => ui_typeSplit.Any(ui => a.tdesig.Contains(ui)));

                if (matchingAircraft.GroupBy(a => a.tdesig).Count() == 1)
                    return matchingAircraft;

                if (!matchingAircraft.Any())
                {
                    var ui_typeSplit_numbers = ui_typeSplit
                        .Select(ui => Regex.Replace(ui, "[^0-9.-]", ""))
                        .Where(ui => !string.IsNullOrEmpty(ui));

                    matchingAircraft = matchingManufacturer
                        .Where(a => !string.IsNullOrWhiteSpace(a.model_no))
                        .Where(a => ui_typeSplit_numbers.Any(ui => a.model_no.Contains(ui)))
                        .OrderByDescending(a => a.model_no.Length);

                    var matchingTdesig = matchingAircraft.Where(a => ui_typeSplit_numbers.Any(ui => a.tdesig.Contains(ui)));

                    if (matchingTdesig.GroupBy(a => a.tdesig).Count() == 1)
                        return matchingTdesig;
                }

                if (!matchingAircraft.Any())
                    matchingAircraft = AlternativeSearch();

                return matchingAircraft;
            }
            else
                return AlternativeSearch();
        }
    }
}