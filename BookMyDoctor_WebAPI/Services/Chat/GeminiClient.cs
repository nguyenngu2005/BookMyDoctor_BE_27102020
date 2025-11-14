using System.Net.Http.Json;
using System.Text.Json;
using BookMyDoctor_WebAPI.RequestModel.Chat;
using Microsoft.Extensions.Options;

namespace BookMyDoctor_WebAPI.Services.Chat;

public class GeminiOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gemini-2.5-flash";
}
public class GeminiClient
{
    private readonly HttpClient _http;
    private readonly GeminiOptions _opt;

    private const string SystemPrompt = """
    Bạn là trợ lý AI của phòng khám BookMyDoctor.
    Trả lời bằng tiếng Việt, hội thoại tự nhiên.
    Nếu người dùng hỏi gì → hiểu ý → tạo JSON theo format:

    {
      "intent": "...",
      "naturalReply": "...",
      ... các field khác tự do ...
    }

    KHÔNG BAO GIỜ trả text thuần.
    Luôn trả JSON hợp lệ, không kèm text bên ngoài JSON.
    """;

    public GeminiClient(HttpClient http, IOptions<GeminiOptions> opt)
    {
        _http = http;
        _opt = opt.Value;
        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    public async Task<string> AskAsync(string userMessage, CancellationToken ct = default)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_opt.Model}:generateContent?key={_opt.ApiKey}";

        var payload = new
        {
            systemInstruction = new
            {
                parts = new[]
                {
                    new { text = SystemPrompt }
                }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[]
                    {
                        new { text = userMessage }
                    }
                }
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload)
        };

        var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini error {(int)res.StatusCode}: {res.ReasonPhrase}. Body: {body}");

        // Extract JSON từ response
        using var doc = JsonDocument.Parse(body);

        var jsonText =
            doc.RootElement
               .GetProperty("candidates")[0]
               .GetProperty("content")
               .GetProperty("parts")[0]
               .GetProperty("text")
               .GetString() ?? "{}";

        return jsonText.Trim();
    }
}
