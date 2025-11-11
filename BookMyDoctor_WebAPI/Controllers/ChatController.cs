using System.Net.Mime;
using BookMyDoctor_WebAPI.RequestModel.Chat;
using BookMyDoctor_WebAPI.Services.Chat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging; // <-- Thêm thư viện này

namespace BookMyDoctor_WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
public class ChatController : ControllerBase
{
    private readonly IChatService _chat;
    private readonly ILogger<ChatController> _logger; // <-- Thêm ILogger

    // Tiêm ILogger vào constructor
    public ChatController(IChatService chat, ILogger<ChatController> logger)
    {
        _chat = chat;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] ChatRequest req, CancellationToken ct)
    {
        if (req is null || req.Messages?.Count == 0)
            return BadRequest(new { error = "Thiếu messages." });
        try
        {
            var replyText = await _chat.HandleAsync(req, ct);
            return Ok(new ChatReply { Reply = replyText });
        }
        catch (HttpRequestException ex) // <-- Biến 'ex' giờ đã được dùng
        {
            // Fix 1: Thực sự log lỗi, biến 'ex' đã được sử dụng
            _logger.LogError(ex, "Lỗi khi gọi dịch vụ AI bên ngoài.");

            // Fix 2: Trả về lỗi theo định dạng { message: "..." }
            return StatusCode(502, new { message = "Xin lỗi, dịch vụ AI đang lỗi. Bạn thử lại sau nhé." });
        }
    }

    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { ok = true, svc = "chat", time = DateTimeOffset.Now });
}