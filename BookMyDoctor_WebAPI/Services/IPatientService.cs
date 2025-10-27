using BookMyDoctor_WebAPI.RequestModel;

namespace BookMyDoctor_WebAPI.Services
{
    public interface IPatientService
    {
        // Lấy danh sách tất cả bệnh nhân, có thể lọc theo tên và ngày khám.
        Task<IReadOnlyList<PatientDetailRequest>> GetAllPatientsAsync(
            string? search = null,
            DateTime? appointDate = null,
            string? status = null,
            CancellationToken ct = default);

        // Lấy thông tin chi tiết của một bệnh nhân dựa theo patientId.
        Task<PatientDetailRequest?> GetPatientDetailAsync(
            int patientId,
            CancellationToken ct = default);

        // Lay lich su kham benh cua mot account
        Task<IReadOnlyList<HistoryRequest>> GetPatientHistoryAsync(
            int UserId,
            CancellationToken ct = default);
        
        // Update thong tin benh nhan
        Task<ServiceResult<bool>> UpdatePatientAsync(
            int patientId,
            PatientDetailRequest dto,
            CancellationToken ct = default);

        // Xoa benh nhan
        Task<bool> DeletePatientAsync(
            int patientId,
            CancellationToken ct = default);
    }
}
