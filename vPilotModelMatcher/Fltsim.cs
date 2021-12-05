namespace vPilotModelMatcher
{
    public class Fltsim
    {
        public string title { get; set; }
        public string ui_type { get; set; }
        public string ui_variation { get; set; }
        public List<string> atc_parking_codes { get; set; }

        public override string ToString()
        {
            return $"{title} - {ui_type} - {ui_variation}{(atc_parking_codes?.Any() ?? false ? $" - {string.Join(", ", atc_parking_codes)}" : "")}";
        }
    }
}
