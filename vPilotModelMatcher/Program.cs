using System.Text.RegularExpressions;
using System.Xml;

namespace vPilotModelMatcher
{
    public class ModelMatchRule
    {
        public string CallsignPrefix { get; set; }
        public string ModelName { get; set; }
        public string TypeCode { get; set; }
    }

    public class Fltsim
    {
        public string title { get; set; }
        public string ui_type { get; set; }
        public string ui_variation { get; set; }

        public override string ToString()
        {
            return $"{title} - {ui_type} - {ui_variation}";
        }
    }

    public class Aircraft
    {
        public string manufacturer_code { get; set; }
        public string model_no { get; set; }
        public string model_name { get; set; }
        public string tdesig { get; set; }
    }

    public class Airline
    {
        public string operator_code { get; set; }
        public string operator_name { get; set; }
        public string country { get; set; }
    }

    public class Program
    {
        public static void Main()
        {
            var aircraft = LoadAircraft();
            var airlines = LoadAirlines();
            var aircraftCfgs = LoadAircraftCfgs();
            var fltsims = ParseAircraftCfgs(aircraftCfgs).ToList();

            var rules = new List<ModelMatchRule>();

            foreach (var fltsim in fltsims)
            {
                var iata = Regex.Match(fltsim.ui_variation, "[A-Z]{3}");

                if (iata.Captures.Count > 1)
                {
                    iata = Regex.Match(fltsim.title, "[A-Z]{3}");

                    if (iata.Captures.Count > 1)
                        iata = null;
                }

                var matchingAirlines = airlines.Where(a => iata == null ? fltsim.ui_variation.Contains(a.operator_code) : iata.Value.ToUpper() == a.operator_code.ToUpper());

                if (!matchingAirlines.Any())
                    Console.WriteLine($"No airline found for {fltsim}");

                if (matchingAirlines.Count() > 1)
                    Console.WriteLine($"More than 1 airline found for {fltsim}");

                var matchingAircraft = aircraft
                    .Where(a => fltsim.ui_type.Contains(a.tdesig))
                    .OrderByDescending(a => a.model_no.Length);

                if (!matchingAircraft.Any())
                    matchingAircraft = aircraft
                        .Where(a => !string.IsNullOrWhiteSpace(a.model_no))
                        .Where(a => fltsim.ui_type.Contains(a.model_no))
                        .OrderByDescending(a => a.model_no.Length);

                if (!matchingAircraft.Any())
                {
                    Console.WriteLine($"No aircraft found for {fltsim}");
                    continue;
                }

                rules.Add(new ModelMatchRule
                {
                    CallsignPrefix = matchingAirlines.Count() == 1 ? matchingAirlines.First().operator_code : null,
                    ModelName = fltsim.title,
                    TypeCode = matchingAircraft.First().tdesig
                });
            }

            rules =
                rules
                    .GroupBy(r => new { r.CallsignPrefix, r.TypeCode })
                    .Select(r => new ModelMatchRule
                    {
                        CallsignPrefix = r.Key.CallsignPrefix,
                        TypeCode = r.Key.TypeCode,
                        ModelName = string.Join("//", r.Select(r => r.ModelName))
                    })
                    .OrderBy(r => r.CallsignPrefix)
                    .ThenBy(r => r.TypeCode)
                    .ToList();

            var outputXml = $"<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                $"<ModelMatchRuleSet>" +
                $"{string.Join('\n', rules.Select(r => $"<ModelMatchRule {(string.IsNullOrWhiteSpace(r.CallsignPrefix) ? "" : $"CallsignPrefix=\"{r.CallsignPrefix}\" ")}TypeCode=\"{r.TypeCode}\" ModelName=\"{r.ModelName}\" />"))}" +
                $"</ModelMatchRuleSet>";

            XmlDocument xmlDocument = new();
            xmlDocument.LoadXml(outputXml);
            xmlDocument.Save("Model Matching Rules.vmr");

            Console.WriteLine(aircraftCfgs.Count);
            Console.ReadLine();
        }

        private static List<Aircraft> LoadAircraft()
        {
            var aircraft = typeof(Program).Assembly.GetManifestResourceStream("vPilotModelMatcher.Aircraft.txt");

            using var streamReader = new StreamReader(aircraft);

            return streamReader
                .ReadToEnd()
                .Split(new[] { "\r\n" }, StringSplitOptions.None)
                .Where(line => !string.IsNullOrWhiteSpace(line))
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
                })
                .ToList();
        }

        private static List<Airline> LoadAirlines()
        {
            var airlines = typeof(Program).Assembly.GetManifestResourceStream("vPilotModelMatcher.Airlines.txt");

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
                })
                .ToList();
        }

        private static Dictionary<string, List<string>> LoadAircraftCfgs()
        {
            var aircraftCfgs = Directory.EnumerateFiles($"{Directory.GetCurrentDirectory()}\\TestData", "*.cfg", SearchOption.AllDirectories);
            //var aircraftCfgs = Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "aircraft.cfg", SearchOption.AllDirectories);

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

                    switch (split[0])
                    {
                        case "title":
                            fltsim.title = split[1];
                            break;
                        case "ui_type":
                            fltsim.ui_type = split[1];
                            break;
                        case "ui_variation":
                            fltsim.ui_variation = split[1];
                            break;
                    }
                }

                yield return fltsim;
            }
        }
    }
}