using BookMyDoctor_WebAPI.RequestModel.Chat;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace BookMyDoctor_WebAPI.Services.Chat
{
    public interface IBookingBackend
    {
        Task<IReadOnlyList<DoctorDto>> SearchDoctorsAsync(string? name, string? department, string? gender, string? phone, DateTimeOffset? workDate, CancellationToken ct);
        Task<IReadOnlyList<BusySlotDto>> GetBusySlotsAsync(int doctorId, DateOnly date, CancellationToken ct);
        Task<BookingResultDto> CreatePublicAsync(PublicBookingRequestDto payload, CancellationToken ct);
        Task<bool> CancelAsync(int bookingId, CancellationToken ct);
    }

    public class BackendOptions
    {
        public string BaseUrl { get; set; } = "http://localhost:7243";
    }

    public class BookingBackend : IBookingBackend
    {
        private readonly HttpClient _http;
        private readonly ILogger<BookingBackend> _logger;

        public BookingBackend(HttpClient http, IOptions<BackendOptions> opt, ILogger<BookingBackend> logger)
        {
            _http = http;
            _logger = logger;

            var baseUrl = (opt.Value.BaseUrl ?? "http://localhost:7243").TrimEnd('/');
            _http.BaseAddress = new Uri(baseUrl);
            if (_http.Timeout == default) _http.Timeout = TimeSpan.FromSeconds(20);
        }

        public async Task<IReadOnlyList<DoctorDto>> SearchDoctorsAsync(string? name, string? department, string? gender, string? phone, DateTimeOffset? workDate, CancellationToken ct)
        {
            var path = "api/Doctors/Search-Doctors";
            var q = new List<string>();

            if (!string.IsNullOrWhiteSpace(name)) q.Add("name=" + Uri.EscapeDataString(name));
            if (!string.IsNullOrWhiteSpace(department)) q.Add("department=" + Uri.EscapeDataString(department));
            if (!string.IsNullOrWhiteSpace(gender)) q.Add("gender=" + Uri.EscapeDataString(gender));
            if (!string.IsNullOrWhiteSpace(phone)) q.Add("phone=" + Uri.EscapeDataString(phone));
            if (workDate.HasValue) q.Add("workDate=" + workDate.Value.ToString("yyyy-MM-dd"));

            var url = q.Count > 0 ? $"{path}?{string.Join("&", q)}" : path;

            var res = await _http.GetAsync(url, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"Backend GET {url} → {(int)res.StatusCode} {res.ReasonPhrase}. Body: {body}");

            var data = JsonSerializer.Deserialize<List<DoctorDto>>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? new List<DoctorDto>();

            return data;
        }

        public async Task<IReadOnlyList<BusySlotDto>> GetBusySlotsAsync(int doctorId, DateOnly date, CancellationToken ct)
        {
            var path = $"api/Booking/info_slot_busy?doctorId={doctorId}&date={date:yyyy-MM-dd}";

            var res = await _http.GetAsync(path, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"Backend GET {path} → {(int)res.StatusCode} {res.ReasonPhrase}. Body: {body}");

            var data = JsonSerializer.Deserialize<List<BusySlotDto>>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? new List<BusySlotDto>();

            return data;
        }

        public async Task<BookingResultDto> CreatePublicAsync(PublicBookingRequestDto payload, CancellationToken ct)
        {
            var path = "api/Booking/public";

            var res = await _http.PostAsJsonAsync(path, payload, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"Backend POST {path} → {(int)res.StatusCode} {res.ReasonPhrase}. Body: {body}");

            return JsonSerializer.Deserialize<BookingResultDto>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? throw new InvalidOperationException("Empty booking result");
        }

        public async Task<bool> CancelAsync(int bookingId, CancellationToken ct)
        {
            var path = $"api/Booking/cancel/{bookingId}";

            var res = await _http.DeleteAsync(path, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"Backend DELETE {path} → {(int)res.StatusCode} {res.ReasonPhrase}. Body: {body}");

            return true;
        }
    }
}
