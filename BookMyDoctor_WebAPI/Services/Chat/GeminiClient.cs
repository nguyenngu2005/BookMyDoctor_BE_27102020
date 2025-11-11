using BookMyDoctor_WebAPI.RequestModel.Chat;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace BookMyDoctor_WebAPI.Services.Chat;

public interface IGeminiClient
{
    Task<(Intent intent, Dictionary<string, string> entities, string raw)> InferAsync(
        IEnumerable<ChatMessage> messages, CancellationToken ct = default);
}

public class GeminiOptions
{
    public string ApiKey { get; set; } = "";                 // Đọc từ User Secrets/ENV
    public string Model { get; set; } = "gemini-2.5-flash";  // <-- default ĐÃ SỬA
}

public class GeminiClient : IGeminiClient
{
    private readonly HttpClient _http;
    private readonly GeminiOptions _opt;

    // SYSTEM PROMPT (guardrail)
    private const string SystemPrompt = """
    Bạn là trợ lý AI phòng khám, trả lời tiếng Việt. Nhiệm vụ: tìm bác sĩ, xem giờ trống, đặt/hủy lịch, FAQ.
    Không chẩn đoán/điều trị. Trường hợp khẩn cấp: khuyên gọi 115.
    Không tiết lộ PII bệnh nhân khác.

    NHIỆM VỤ PHÂN LOẠI:
    - Trả về CHÍNH XÁC một JSON { "intent": "...", "entities": { ... } } ở cuối câu trả lời.
    - intent ∈ { GreetingHelp, SearchDoctors, GetBusySlots, CreatePublicBooking, CancelBooking, Faq, Unknown }
    - entities có thể gồm: { doctorId, doctorName, department, date(YYYY-MM-DD), hour(HH:mm), bookingId, fullName, phone, email, symptom }

    QUY TẮC:
    - Nếu câu chứa “đặt lịch”, “book”, “hẹn khám” ⇒ ưu tiên intent CreatePublicBooking.
    - Nếu có “hủy lịch”, “cancel”, “xóa lịch” ⇒ intent CancelBooking.
    - “xem giờ trống”, “còn giờ nào” ⇒ GetBusySlots.
    - “bác sĩ khoa …”, “tìm bác sĩ …” ⇒ SearchDoctors.
    - Nếu hỏi giờ mở cửa/quy trình… ⇒ Faq.

    VÍ DỤ:
    User: "Tôi muốn đặt lịch với bác sĩ ID 9 ngày 2025-11-15 lúc 09:00."
    JSON: {"intent":"CreatePublicBooking","entities":{"doctorId":"9","date":"2025-11-15","hour":"09:00"}}

    User: "Cho xem bác sĩ khoa Tai Mũi Họng."
    JSON: {"intent":"SearchDoctors","entities":{"department":"Tai Mũi Họng"}}

    User: "Ngày mai bác sĩ ID 12 còn giờ trống không?"
    JSON: {"intent":"GetBusySlots","entities":{"doctorId":"12","date":"<NGÀY_MAI_YYYY-MM-DD>"}}

    User: "Hủy lịch mã 12345"
    JSON: {"intent":"CancelBooking","entities":{"bookingId":"12345"}}

    Luôn trả 1 đoạn JSON hợp lệ duy nhất ở cuối.
    """;
    public GeminiClient(HttpClient http, IOptions<GeminiOptions> opt)
    {
        _http = http;
        _opt = opt.Value;
        // (tuỳ chọn) timeout nhẹ để tránh treo
        if (_http.Timeout == default) _http.Timeout = TimeSpan.FromSeconds(20);
    }

    public async Task<(Intent intent, Dictionary<string, string> entities, string raw)>
        InferAsync(IEnumerable<ChatMessage> messages, CancellationToken ct = default)
    {
        // ===== Guard cấu hình =====
        if (string.IsNullOrWhiteSpace(_opt.ApiKey))
            throw new HttpRequestException("Gemini API key is empty. Set GoogleAi:ApiKey via User Secrets or ENV.");

        if (string.IsNullOrWhiteSpace(_opt.Model))
            throw new HttpRequestException("Gemini model is empty. Set GoogleAi:Model (e.g., gemini-2.5-flash).");

        // ĐÚNG endpoint v1beta + generateContent
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_opt.Model}:generateContent?key={_opt.ApiKey}";

        // Ghép messages -> parts (chỉ cần text)
        // Nếu bạn muốn giữ lịch sử hội thoại (assistant/user đan xen), có thể thêm nhiều contents,
        // ở đây tối giản: gộp nội dung người dùng hiện tại vào 1 content.
        var userParts = messages?.Select(m => new { text = m.Content })?.ToArray()
                        ?? Array.Empty<object>();

        var payload = new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = SystemPrompt } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = userParts
                }
            },
            generationConfig = new { temperature = 0.2, topP = 0.9 }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload)
        };

        var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            // Ném lỗi kèm body để controller LogError thấy nguyên nhân (model not found, key invalid, api disabled...)
            throw new HttpRequestException($"Gemini API error {(int)res.StatusCode} {res.ReasonPhrase}. Body: {body}");
        }

        // Parse text ra
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // candidates[0].content.parts[0].text
        var text = root.GetProperty("candidates")[0]
                       .GetProperty("content")
                       .GetProperty("parts")[0]
                       .GetProperty("text")
                       .GetString() ?? string.Empty;

        // Cố gắng bóc JSON intent {intent, entities} từ câu trả lời
        try
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                var jsonSlice = text.Substring(start, end - start + 1);
                using var nodeDoc = JsonDocument.Parse(jsonSlice);
                var node = nodeDoc.RootElement;

                var intentStr = node.TryGetProperty("intent", out var pi) ? (pi.GetString() ?? "Unknown") : "Unknown";
                var entDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (node.TryGetProperty("entities", out var ents))
                {
                    foreach (var p in ents.EnumerateObject())
                        entDict[p.Name] = p.Value.ValueKind == JsonValueKind.String ? (p.Value.GetString() ?? "") : p.Value.ToString();
                }

                if (!Enum.TryParse(intentStr, true, out Intent intent))
                    intent = Intent.Unknown;

                return (intent, entDict, text);
            }
        }
        catch
        {
            // không sao, fallback Unknown
        }

        return (Intent.Unknown, new Dictionary<string, string>(), text);
    }
}