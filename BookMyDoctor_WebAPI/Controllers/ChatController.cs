using System.Net.Mime;
using System.Security.Claims;                    // 👈 thêm dòng này
using BookMyDoctor_WebAPI.RequestModel.Chat;
using BookMyDoctor_WebAPI.Services.Chat;
using Microsoft.AspNetCore.Mvc;

namespace BookMyDoctor_WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
public class ChatController : ControllerBase
{
    private readonly ChatService _chat;
    private readonly ILogger<ChatController> _logger;

    public ChatController(ChatService chat, ILogger<ChatController> logger)
    {
        _chat = chat;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] ChatRequest req, CancellationToken ct)
    {
        if (req == null || req.Messages == null || req.Messages.Count == 0)
            return BadRequest(new { error = "Thiếu messages." });

        // ✅ Nếu user đã đăng nhập → gán UserId vào ChatRequest
        if (User.Identity?.IsAuthenticated == true)
        {
            var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(idStr, out var id))
            {
                req.UserId = id;
            }
        }

        try
        {
            var reply = await _chat.Process(req, ct);
            return Ok(reply); // reply đã là ChatReply
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Lỗi khi gọi dịch vụ AI bên ngoài.");
            return StatusCode(502, new { message = "Xin lỗi, dịch vụ AI đang lỗi. Bạn thử lại sau nhé." });
        }
    }

    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { ok = true, svc = "chat", time = DateTimeOffset.Now });
}
