using System.Text.Json.Nodes;
using BookMyDoctor_WebAPI.RequestModel.Chat;

namespace BookMyDoctor_WebAPI.Services.Chat
{
    public static class LlmJsonParser
    {
        public static LlmStructuredOutput Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new LlmStructuredOutput
                {
                    Intent = Intent.Unknown,
                    NaturalReply = "",
                    Extra = new Dictionary<string, object?>(),
                    Raw = null
                };
            }

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(json);
            }
            catch
            {
                return new LlmStructuredOutput
                {
                    Intent = Intent.Unknown,
                    NaturalReply = json,
                    Extra = new Dictionary<string, object?>(),
                    Raw = null
                };
            }

            var model = new LlmStructuredOutput
            {
                Raw = root,
                NaturalReply = root?["naturalReply"]?.ToString() ?? "",
                Extra = new Dictionary<string, object?>()
            };

            var intentStr = root?["intent"]?.ToString();
            if (!string.IsNullOrWhiteSpace(intentStr) &&
                Enum.TryParse(intentStr, true, out Intent intent))
            {
                model.Intent = intent;
            }

            if (root is JsonObject obj)
            {
                foreach (var kvp in obj)
                {
                    if (kvp.Key is "intent" or "naturalReply")
                        continue;

                    model.Extra[kvp.Key] = ConvertJson(kvp.Value);
                }
            }

            return model;
        }

        private static object? ConvertJson(JsonNode? n)
        {
            if (n is null)
                return null;

            if (n is JsonValue v)
            {
                if (v.TryGetValue<int>(out var i)) return i;
                if (v.TryGetValue<long>(out var l)) return l;
                if (v.TryGetValue<double>(out var d)) return d;
                if (v.TryGetValue<bool>(out var b)) return b;
                if (v.TryGetValue<string>(out var s)) return s;
            }

            if (n is JsonArray arr)
                return arr.Select(ConvertJson).ToList();

            if (n is JsonObject obj)
                return obj.ToDictionary(e => e.Key, e => ConvertJson(e.Value));

            return n.ToJsonString();
        }
    }
}
