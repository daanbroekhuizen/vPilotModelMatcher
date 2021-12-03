namespace vPilotModelMatcher
{
    public class ModelMatchRule
    {
        public string CallsignPrefix { get; set; }
        public string ModelName { get; set; }
        public string TypeCode { get; set; }
    }

    public class Program
    {
        public static void Main()
        {
            var aircraft = LoadICAOAircraft();
            var airlines = LoadICAOAirlines();

            var models = ParseAircraftCfgs();
            var rules = new List<ModelMatchRule>();

            foreach (var model in models)
            {
                var matchingAircraft = aircraft
                    .Where(a => model.Key.Contains(a))
                    .OrderByDescending(s => s.Length)
                    .FirstOrDefault();

                var matchingAirline = airlines
                    .Where(a => model.Key.Contains(a))
                    .OrderByDescending(s => s.Length)
                    .FirstOrDefault();

                if (matchingAircraft != null && matchingAirline != null)
                {
                    rules.Add(new ModelMatchRule
                    {
                        CallsignPrefix = matchingAirline,
                        ModelName = model.Key,
                        TypeCode = matchingAircraft
                    });
                }
                else
                {
                    Console.WriteLine($"Meh: {model.Key}");
                }
            }

            Console.WriteLine(models.Count);
            Console.ReadLine();
        }

        private static Dictionary<string, List<string>> ParseAircraftCfgs()
        {
            var aircraftCfgs = Directory.EnumerateFiles(@"C:\IVAO_MTL", "aircraft.cfg", SearchOption.AllDirectories);
            //var aircraftCfgs = Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "aircraft.cfg", SearchOption.AllDirectories);

            var models = new Dictionary<string, List<string>>();

            foreach (var cfg in aircraftCfgs)
            {
                var data = File.ReadAllLines(cfg)
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToList();

                while (data.Any(d => d.StartsWith("[fltsim")))
                {
                    var nextFltsim = data.First(d => d.StartsWith("[fltsim"));
                    var nextFltsimIndex = data.IndexOf(nextFltsim);

                    var nextSection = data.Skip(nextFltsimIndex == 0 ? 1 : nextFltsimIndex).First(d => d.StartsWith("["));
                    var nextSectionIndex = data.IndexOf(nextSection);

                    var fltsim = data.Skip(nextFltsimIndex + 1).Take(nextSectionIndex - 1);

                    var title = fltsim.SingleOrDefault(f => f.StartsWith("title="));

                    if (title != null)
                    {
                        var key = title.Split("=")[1];

                        models.Add(key, fltsim.ToList());
                    }

                    for (var i = nextSectionIndex - 1; i >= nextFltsimIndex; i--)
                    {
                        data.RemoveAt(i);
                    }
                }
            }

            return models;
        }

        private static List<string> LoadICAOAircraft()
        {
            return File.ReadAllLines($"{Directory.GetCurrentDirectory()}\\ICAO_Aircraft.txt")
                .Select(line => line.Split('\t')[0])
                .ToList();
        }

        private static List<string> LoadICAOAirlines()
        {
            return File.ReadAllLines($"{Directory.GetCurrentDirectory()}\\ICAO_Airlines.txt")
                .Select(line => line.Split('\t')[0])
                .ToList();
        }
    }
}