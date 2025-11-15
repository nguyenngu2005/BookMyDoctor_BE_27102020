using BookMyDoctor_WebAPI.RequestModel.Chat;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BookMyDoctor_WebAPI.Services.Chat
{
    // Dùng đúng model hiện tại của BookingController/BookingService
    public interface IBookingBackend
    {
        Task<List<DoctorBasicInfo>> GetAvailableDoctorsAsync(DateOnly date, CancellationToken ct);

        Task<IReadOnlyList<DoctorDto>> SearchDoctorsAsync(
            string? name,
            string? department,
            string? gender,
            string? phone,
            DateTimeOffset? workDate,
            CancellationToken ct);

        Task<IReadOnlyList<BusySlot>> GetBusySlotsAsync(
            int doctorId,
            DateOnly date,
            CancellationToken ct);

        Task<BookingResult> CreatePublicAsync(
            PublicBookingRequest payload,
            CancellationToken ct);

        Task<bool> CancelAsync(int bookingId, CancellationToken ct);

        // 🔹 Chỉ KHAI BÁO, không viết thân hàm trong interface
        Task<int> CountDoctorsAsync(CancellationToken ct);
    }

    public class BackendOptions
    {
        // bạn có thể override trong appsettings thành https://localhost:7243
        public string BaseUrl { get; set; } = "https://localhost:7243";
    }

    public class BookingBackend : IBookingBackend
    {
        private readonly HttpClient _http;
        private readonly ILogger<BookingBackend> _logger;

        public BookingBackend(
            HttpClient http,
            IOptions<BackendOptions> opt,
            ILogger<BookingBackend> logger)
        {
            _http = http;
            _logger = logger;

            var baseUrl = (opt.Value.BaseUrl ?? "https://localhost:7243").TrimEnd('/');
            _http.BaseAddress = new Uri(baseUrl);

            if (_http.Timeout == default)
                _http.Timeout = TimeSpan.FromSeconds(20);
        }

        // ===================== Doctors/Search-Doctors =====================
        public async Task<IReadOnlyList<DoctorDto>> SearchDoctorsAsync(
    string? name,
    string? department,
    string? gender,
    string? phone,
    DateTimeOffset? workDate,
    CancellationToken ct)
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

            // 🔹 Nếu backend trả 404 = không có bác sĩ phù hợp → coi như list rỗng, KHÔNG ném exception
            if (res.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("SearchDoctorsAsync NotFound for {Url} → 0 doctor.", url);
                return Array.Empty<DoctorDto>();
            }

            // 🔹 Các lỗi khác (500, 401, 403, ...) mới ném exception
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Backend GET {url} → {(int)res.StatusCode} {res.ReasonPhrase}. Body: {body}");

            var data = JsonSerializer.Deserialize<List<DoctorDto>>(
                           body,
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? new List<DoctorDto>();

            return data;
        }


        // ===================== Booking/info_slot_busy =====================
        public async Task<List<DoctorBasicInfo>> GetAvailableDoctorsAsync(DateOnly date, CancellationToken ct)
        {
            var url = $"api/Doctors/Search-Doctors?workDate={date:yyyy-MM-dd}";
            try
            {
                var result = await _http.GetFromJsonAsync<List<DoctorBasicInfo>>(url, ct);
                return result ?? new List<DoctorBasicInfo>();
            }
            catch
            {
                return new List<DoctorBasicInfo>();
            }
        }

        public async Task<IReadOnlyList<BusySlot>> GetBusySlotsAsync(
            int doctorId,
            DateOnly date,
            CancellationToken ct)
        {
            var path = $"api/Booking/info_slot_busy?doctorId={doctorId}&date={date:yyyy-MM-dd}";

            var res = await _http.GetAsync(path, ct);
            var body = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Backend GET {path} → {(int)res.StatusCode} {res.ReasonPhrase}. Body: {body}");

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var data = JsonSerializer.Deserialize<List<BusySlot>>(body, options)
                       ?? new List<BusySlot>();

            return data;
        }

        // ===================== Booking/public =====================
        public async Task<BookingResult> CreatePublicAsync(
            PublicBookingRequest payload,
            CancellationToken ct)
        {
            const string path = "api/Booking/public";

            _logger.LogInformation("POST {Path} with payload {@Payload}", path, payload);

            var res = await _http.PostAsJsonAsync(path, payload, ct);
            var body = await res.Content.ReadAsStringAsync(ct);

            _logger.LogInformation("Response {StatusCode} body: {Body}", res.StatusCode, body);

            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Backend POST {path} → {(int)res.StatusCode} {res.ReasonPhrase}. Body: {body}");

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            return JsonSerializer.Deserialize<BookingResult>(body, options)
                   ?? throw new InvalidOperationException("Empty booking result");
        }

        // ===================== Booking/cancel/{id} =====================
        public async Task<bool> CancelAsync(int bookingId, CancellationToken ct)
        {
            var path = $"api/Booking/cancel/{bookingId}";

            var res = await _http.DeleteAsync(path, ct);
            var body = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Backend DELETE {path} → {(int)res.StatusCode} {res.ReasonPhrase}. Body: {body}");

            return true;
        }

        // ===================== Doctors/All-Doctors → Count =====================
        public async Task<int> CountDoctorsAsync(CancellationToken ct)
        {
            const string path = "api/Doctors/All-Doctors";

            var res = await _http.GetAsync(path, ct);
            var body = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Backend GET {path} → {(int)res.StatusCode} {res.ReasonPhrase}. Body: {body}");

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            // All-Doctors trả về List<DoctorDto>
            var data = JsonSerializer.Deserialize<List<DoctorDto>>(body, options)
                       ?? new List<DoctorDto>();

            return data.Count;
        }
    }
}
