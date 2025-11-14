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

            string json;
            try
            {
                // 4) Gọi Gemini với prompt gồm cả history
                json = await _gemini.AskAsync(prompt, ct);
            }
            catch (Exception ex)
            {
                // Nếu lỗi kết nối model
                return new ChatReply
                {
                    Reply = $"Xin lỗi, hệ thống AI đang gặp lỗi: {ex.Message}"
                };
            }

            LlmStructuredOutput llm;
            try
            {
                llm = LlmJsonParser.Parse(json);
            }
            catch
            {
                // Nếu parse JSON thất bại thì trả thẳng nội dung model trả về
                return new ChatReply
                {
                    Reply = json
                };
            }

            // 5) Đưa structured output cho BookingBackendHandler xử lý (tìm bác sĩ, đặt lịch, hủy, v.v.)
            var replyText = await _handler.HandleAsync(llm, sessionId, req.UserId, ct);

            // 6) Lưu câu trả lời cuối cùng (đã qua handler) vào history
            lock (session)
            {
                session.Turns.Add(new ChatTurn("assistant", replyText));
            }

            // 7) Trả về cho FE
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

            // Tuỳ bạn, có thể thêm system prompt ở trên cùng nếu GeminiClient không làm nữa.
            // sb.AppendLine("Bạn là chatbot hỗ trợ đặt lịch khám cho phòng khám BookMyDoctor...");
            // sb.AppendLine("Hãy luôn trả lời dưới dạng JSON theo schema đã được cấu hình.");
            // sb.AppendLine();

            foreach (var turn in session.Turns)
            {
                if (turn.Role == "user")
                    sb.AppendLine($"Người dùng: {turn.Content}");
                else
                    sb.AppendLine($"Trợ lý: {turn.Content}");
            }

            // Gợi ý cuối cùng cho model
            sb.AppendLine("Tiếp theo, hãy trả lời bước kế tiếp dưới dạng JSON theo đúng schema đã được cấu hình.");

            return sb.ToString();
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
