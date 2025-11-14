using BookMyDoctor_WebAPI.RequestModel.Chat;

namespace BookMyDoctor_WebAPI.Services.Chat
{
    public class ChatService
    {
        private readonly GeminiClient _gemini;
        private readonly BookingBackendHandler _handler;

        public ChatService(GeminiClient gemini, BookingBackendHandler handler)
        {
            _gemini = gemini;
            _handler = handler;
        }

        public async Task<ChatReply> Process(ChatRequest req, CancellationToken ct = default)
        {
            var lastMsg = req.Messages.Last();

            string json = await _gemini.AskAsync(lastMsg.Content, ct);

            LlmStructuredOutput llm = LlmJsonParser.Parse(json);

            // ✅ Truyền luôn UserId xuống handler
            string reply = await _handler.HandleAsync(llm, req.SessionId, req.UserId);

            return new ChatReply { Reply = reply };
        }
    }
}
