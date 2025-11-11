namespace BookMyDoctor_WebAPI.RequestModel.Chat;

public class ChatMessage
{
    // "user" | "assistant"
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
}