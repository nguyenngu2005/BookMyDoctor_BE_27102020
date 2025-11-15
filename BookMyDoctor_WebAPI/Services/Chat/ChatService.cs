using System.Collections.Concurrent;
using System.Text;
using BookMyDoctor_WebAPI.RequestModel.Chat;

namespace BookMyDoctor_WebAPI.Services.Chat
{
    public class ChatService
    {
        private readonly GeminiClient _gemini;
        private readonly BookingBackendHandler _handler;

        // Lưu history theo SessionId (server-side)
        private static readonly ConcurrentDictionary<string, ChatSessionState> _sessions = new();

        public ChatService(GeminiClient gemini, BookingBackendHandler handler)
        {
            _gemini = gemini;
            _handler = handler;
        }

        public async Task<ChatReply> Process(ChatRequest req, CancellationToken ct = default)
        {
            // 1) Chuẩn hóa SessionId (phòng khi FE gửi rỗng)
            var sessionId = string.IsNullOrWhiteSpace(req.SessionId)
                ? Guid.NewGuid().ToString("N")
                : req.SessionId;

            // 2) Lấy câu hỏi hiện tại (FE mỗi lần chỉ cần gửi 1 message)
            var lastMsg = req.Messages.LastOrDefault();
            if (lastMsg == null || string.IsNullOrWhiteSpace(lastMsg.Content))
            {
                return new ChatReply
                {
                    Reply = "Anh/chị chưa nhập nội dung câu hỏi."
                };
            }

            var userText = lastMsg.Content.Trim();

            // 3) Lấy hoặc tạo session history trên server
            var session = _sessions.GetOrAdd(sessionId, _ => new ChatSessionState());

            string prompt;
            lock (session)
            {
                // Lưu lại câu user vừa hỏi vào history
                session.Turns.Add(new ChatTurn("user", userText));

                // Build prompt từ toàn bộ lịch sử hội thoại
                prompt = BuildPrompt(session);
            }

            string raw;
            try
            {
                // 4) Gọi Gemini với prompt gồm cả history
                raw = await _gemini.AskAsync(prompt, ct);
            }
            catch (Exception ex)
            {
                // Nếu lỗi kết nối model
                return new ChatReply
                {
                    Reply = $"Xin lỗi, hệ thống AI đang gặp lỗi: {ex.Message}"
                };
            }

            // 5) Cố gắng tách JSON sạch từ output (loại bỏ ***json, ```json, text thừa...)
            var pureJson = ExtractJson(raw);

            LlmStructuredOutput llm;
            try
            {
                llm = LlmJsonParser.Parse(pureJson);
            }
            catch (Exception)
            {
                // Nếu tới đây mà vẫn parse fail thì coi như model trả sai format
                // Không trả raw JSON nữa để tránh user thấy ***json {...}
                return new ChatReply
                {
                    Reply = "Xin lỗi, hệ thống AI tạm thời không hiểu được phản hồi từ mô hình. Bạn có thể hỏi lại theo cách khác giúp em được không ạ?"
                };
            }

            // 6) Đưa structured output cho BookingBackendHandler xử lý (tìm bác sĩ, đặt lịch, hủy, v.v.)
            var replyText = await _handler.HandleAsync(llm, sessionId, req.UserId, ct);

            // 7) Lưu câu trả lời cuối cùng (đã qua handler) vào history
            lock (session)
            {
                session.Turns.Add(new ChatTurn("assistant", replyText));
            }

            // 8) Trả về cho FE
            return new ChatReply
            {
                Reply = replyText
            };
        }

        /// <summary>
        /// Ghép toàn bộ lịch sử hội thoại thành 1 prompt gửi cho Gemini.
        /// </summary>
        private static string BuildPrompt(ChatSessionState session)
        {
            var sb = new StringBuilder();

            // Nếu GeminiClient không add system prompt thì có thể thêm ở đây.
            // sb.AppendLine("Bạn là chatbot hỗ trợ đặt lịch khám cho phòng khám BookMyDoctor...");
            // sb.AppendLine("Luôn trả lời dưới dạng JSON đúng schema.");
            // sb.AppendLine();

            foreach (var turn in session.Turns)
            {
                if (turn.Role == "user")
                    sb.AppendLine($"Người dùng: {turn.Content}");
                else
                    sb.AppendLine($"Trợ lý: {turn.Content}");
            }

            sb.AppendLine("Tiếp theo, hãy trả lời bước kế tiếp dưới dạng JSON theo đúng schema đã được cấu hình.");

            return sb.ToString();
        }

        /// <summary>
        /// Cố gắng trích phần JSON chính từ output của model.
        /// Ví dụ model trả:
        /// ***json
        /// { "intent": "...", ... }
        /// ***endjson
        /// → hàm sẽ cắt còn lại phần {...}.
        /// </summary>
        private static string ExtractJson(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "{}";

            // Loại bỏ markdown fence hoặc prefix riêng
            var cleaned = raw.Trim();

            // Một số prompt hay dùng ***json hoặc ```json
            cleaned = cleaned
                .Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("```", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("***json", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("***endjson", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();

            // Nếu vẫn còn text rác, lấy đoạn từ { đầu tiên tới } cuối cùng
            var firstBrace = cleaned.IndexOf('{');
            var lastBrace = cleaned.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                return cleaned.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            // Không tìm thấy ngoặc nhọn → trả nguyên, để Parse xử lý lỗi
            return cleaned;
        }

        // ======= Các class hỗ trợ lưu history =======

        private sealed class ChatSessionState
        {
            public List<ChatTurn> Turns { get; } = new();
        }

        private sealed class ChatTurn
        {
            public ChatTurn(string role, string content)
            {
                Role = role;
                Content = content;
            }

            public string Role { get; }
            public string Content { get; }
        }
    }
}
