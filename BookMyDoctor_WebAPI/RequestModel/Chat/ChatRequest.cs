namespace BookMyDoctor_WebAPI.RequestModel.Chat;

public class ChatRequest
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public List<ChatMessage> Messages { get; set; } = new();
}