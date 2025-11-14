namespace BookMyDoctor_WebAPI.RequestModel.Chat
{
    public class LlmStructuredOutput
    {
        public Intent Intent { get; set; } = Intent.Unknown;

        public string NaturalReply { get; set; } = string.Empty;

        public Dictionary<string, object?> Extra { get; set; } = new();

        public object? Raw { get; set; }
    }
}
