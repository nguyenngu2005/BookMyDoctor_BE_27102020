using BookMyDoctor_WebAPI.Models;

namespace BookMyDoctor_WebAPI.Data.Repositories
{
    /// Repository cho bảng patients – xử lý CRUD và tìm kiếm bệnh nhân.
    public interface IPatientRepository
    {
        /// Tìm bệnh nhân theo name (có thể là tên đầy đủ hoặc một phần).
        Task<IReadOnlyList<Patient>> SearchPatientByNameAsync(
            string name,
            CancellationToken ct = default);

        Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);

        /// Lấy thông tin bệnh nhân theo số điện thoại.
        Task<Patient?> GetByPhoneAsync(
            string phone,
            CancellationToken ct = default);

        /// Lấy thông tin bệnh nhân theo UserId (nếu có liên kết tài khoản user).
        Task<Patient?> GetByUserIdAsync(
            int userId,
            CancellationToken ct = default);

        /// Lấy thông tin bệnh nhân theo PatientId.
        Task<Patient?> GetByIdAsync(
            int patientId,
            CancellationToken ct = default);

        /// Lấy toàn bộ danh sách bệnh nhân (phục vụ admin/tra cứu).
        Task<IReadOnlyList<Patient>> GetAllAsync(
            CancellationToken ct = default);

        /// Thêm mới bệnh nhân vào DB.
        Task AddAsync(
            Patient patient,
            CancellationToken ct = default);

        /// Cập nhật thông tin bệnh nhân (tên, email, địa chỉ...).
        Task UpdateAsync(
            Patient patient,
            CancellationToken ct = default);

        /// Xóa bệnh nhân theo ID.
        Task<bool> DeleteAsync(
            int patientId,
            CancellationToken ct = default);
    }
}
