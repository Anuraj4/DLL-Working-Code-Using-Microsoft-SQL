namespace Xalta.Edi.AddressParser.Models
{
    public class ParsedAddress
    {
        public string? AddressLine1 { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Zip { get; set; }

        public override string ToString()
        {
            return $"{AddressLine1}, {City}, {State} {Zip}";
        }
    }

    public class StateData
    {
        public string Name { get; set; } = string.Empty;
        public string StateCode { get; set; } = string.Empty;
    }
}
