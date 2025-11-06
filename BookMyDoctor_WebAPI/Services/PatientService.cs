using BookMyDoctor_WebAPI.Data;
using BookMyDoctor_WebAPI.Data.Repositories;
using BookMyDoctor_WebAPI.Models;
using BookMyDoctor_WebAPI.RequestModel;
using Microsoft.EntityFrameworkCore;

namespace BookMyDoctor_WebAPI.Services
{
    public interface IPatientService
    {
        Task<IReadOnlyList<PatientDetailRequest>> GetAllPatientsAsync(
            string? search = null,
            DateTime? appointDate = null,
            string? status = null,
            int? doctorId = null,
            CancellationToken ct = default);

        Task<PatientDetailRequest?> GetPatientDetailAsync(
            int patientId,
            CancellationToken ct = default);

        Task<IReadOnlyList<HistoryRequest>> GetPatientHistoryAsync(
            int userId,
            CancellationToken ct = default);

        Task<ServiceResult<bool>> UpdatePatientAsync(
            int patientId,
            DateOnly appointDate,
            TimeOnly appointHour,
            PatientUpdateRequest dto,
            CancellationToken ct = default);

        Task<bool> DeletePatientAsync(
            int patientId,
            CancellationToken ct = default);
    }

    public class PatientService : IPatientService
    {
        private readonly DBContext _context;
        private readonly IPatientRepository _repo;

        public PatientService(DBContext context, IPatientRepository repo)
        {
            _context = context;
            _repo = repo;
        }

        // ==================== READ ALL with filters ====================
        public async Task<IReadOnlyList<PatientDetailRequest>> GetAllPatientsAsync(
            string? name,
            DateTime? appointDate,
            string? status,
            int? doctorId = null,                // ✅ thêm parameter lọc theo DoctorId
            CancellationToken ct = default)
        {
            try
            {
                // B1: Patients + optional Users
                var patients = await (
                    from p in _context.Patients.AsNoTracking()
                    join u in _context.Users.AsNoTracking() on p.UserId equals u.UserId into pu
                    from user in pu.DefaultIfEmpty()
                    where p.IsActive == true
                    select new
                    {
                        Patient = p,
                        User = user
                    }).ToListAsync(ct);

                // B2: Appointments (with Schedule) & Prescriptions
                var appointments = await _context.Appointments
                    .Include(a => a.Schedule)
                    .AsNoTracking()
                    .ToListAsync(ct);

                var prescriptions = await _context.Prescriptions
                    .Include(pre => pre.Appointment)
                    .AsNoTracking()
                    .ToListAsync(ct);

                // B3: Map
                var result = patients.Select(p =>
                {
                    var patientAppointments = appointments
                        .Where(a => a.PatientId == p.Patient.PatientId && a.Schedule != null)
                        .OrderByDescending(a => a.Schedule!.WorkDate);

                    var latestApp = patientAppointments.FirstOrDefault();

                    var pres = prescriptions
                        .Where(pre => pre.Appointment != null &&
                                      pre.Appointment.PatientId == p.Patient.PatientId)
                        .OrderByDescending(pre => pre.DateCreated)
                        .FirstOrDefault();

                    return new PatientDetailRequest
                    {
                        FullName = p.Patient.Name,
                        Username = p.User?.Username,
                        DateOfBirth = p.Patient.DateOfBirth,
                        Gender = p.Patient.Gender,
                        PhoneNumber = p.Patient.Phone,
                        Email = p.Patient.Email,
                        Address = p.Patient.Address,
                        Status = latestApp?.Status,
                        Symptoms = latestApp?.Symptom,
                        Prescription = pres?.Description,
                        AppointDate = latestApp?.Schedule?.WorkDate,
                        AppointHour = latestApp?.Schedule?.StartTime,
                        // ✅ thêm DoctorId để có thể lọc
                        DoctorId = latestApp?.Schedule?.DoctorId
                    };
                }).ToList();

                // B4: Filters
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var keyword = name.Trim().ToLowerInvariant();
                    result = result.Where(p =>
                        (p.FullName?.ToLowerInvariant().Contains(keyword) ?? false) ||
                        (p.PhoneNumber?.Contains(keyword) ?? false) ||
                        (p.Email?.ToLowerInvariant().Contains(keyword) ?? false)
                    ).ToList();
                }

                if (appointDate.HasValue)
                {
                    var date = DateOnly.FromDateTime(appointDate.Value.Date);
                    result = result.Where(p => p.AppointDate.HasValue && p.AppointDate.Value == date).ToList();
                }

                if (!string.IsNullOrWhiteSpace(status))
                {
                    var s = status.Trim().ToLowerInvariant();
                    result = result.Where(p => (p.Status?.ToLowerInvariant() ?? string.Empty) == s).ToList();
                }

                if (doctorId.HasValue)
                {
                    result = result.Where(p => p.DoctorId.HasValue && p.DoctorId.Value == doctorId.Value).ToList();
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetAllPatientsAsync] Error: {ex.Message}");
                return new List<PatientDetailRequest>();
            }
        }


        // ==================== READ ONE ====================
        public async Task<PatientDetailRequest?> GetPatientDetailAsync(
            int patientId,
            CancellationToken ct = default)
        {
            return await (from p in _context.Patients
                          join u in _context.Users on p.UserId equals u.UserId into pu
                          from user in pu.DefaultIfEmpty()
                          where p.PatientId == patientId
                          select new PatientDetailRequest
                          {
                              FullName = p.Name,
                              Username = user != null ? user.Username : null,
                              DateOfBirth = p.DateOfBirth,
                              Gender = p.Gender,
                              PhoneNumber = p.Phone,
                              Email = p.Email,
                              Address = p.Address,

                              // Trạng thái gần nhất (join đảm bảo có Schedule)
                              Status = _context.Appointments
                                  .Where(a => a.PatientId == p.PatientId)
                                  .Join(_context.Schedules,
                                            a => a.ScheduleId,
                                            s => s.ScheduleId,
                                            (a, s) => new { a.Status, s.WorkDate })
                                  .OrderByDescending(x => x.WorkDate)
                                  .Select(x => x.Status)
                                  .FirstOrDefault(),

                              // Triệu chứng gần nhất
                              Symptoms = _context.Appointments
                                  .Where(a => a.PatientId == p.PatientId
                                           && a.Symptom != null
                                           && a.Schedule != null)
                                  .OrderByDescending(a => a.Schedule!.WorkDate)
                                  .Select(a => a.Symptom)
                                  .FirstOrDefault(),

                              // Đơn thuốc gần nhất
                              Prescription = _context.Prescriptions
                                  .Where(pre => pre.Appointment != null
                                             && pre.Appointment.PatientId == p.PatientId)
                                  .OrderByDescending(pre => pre.DateCreated)
                                  .Select(pre => pre.Description)
                                  .FirstOrDefault(),

                              // Ngày khám gần nhất
                              AppointDate = _context.Appointments
                                  .Where(a => a.PatientId == p.PatientId && a.Schedule != null)
                                  .OrderByDescending(a => a.Schedule!.WorkDate)
                                  .Select(a => a.Schedule!.WorkDate)
                                  .FirstOrDefault()
                          })
                          .AsNoTracking()
                          .FirstOrDefaultAsync(ct);
        }

        // ==================== HISTORY BY USER ====================
        public async Task<IReadOnlyList<HistoryRequest>> GetPatientHistoryAsync(
            int userId,
            CancellationToken ct = default)
        {
            var query = from p in _context.Patients
                        join a in _context.Appointments on p.PatientId equals a.PatientId
                        join s in _context.Schedules on a.ScheduleId equals s.ScheduleId
                        join d in _context.Doctors on s.DoctorId equals d.DoctorId
                        join pre in _context.Prescriptions on a.AppointId equals pre.AppointId into preJoin
                        from pre in preJoin.DefaultIfEmpty()
                        where p.UserId == userId
                        orderby s.WorkDate descending, s.StartTime descending
                        select new HistoryRequest
                        {
                            NamePatient = p.Name,
                            NameDoctor = d.Name,
                            PhoneDoctor = d.Phone,
                            Department = d.Department,
                            AppointHour = s.StartTime,
                            AppointDate = s.WorkDate,
                            Status = a.Status,
                            Symptoms = a.Symptom ?? "Không có",
                            Prescription = pre != null ? (pre.Description ?? "Không có") : "Không có"
                        };

            return await query.AsNoTracking().ToListAsync(ct);
        }

        // ==================== UPDATE ====================
        public async Task<ServiceResult<bool>> UpdatePatientAsync(
            int patientId,
            DateOnly appointDate,
            TimeOnly appointHour,
            PatientUpdateRequest dto,
            CancellationToken ct = default)
        {
            try
            {
                // 1) Patient exists?
                var patient = await _repo.GetByIdAsync(patientId, ct);
                if (patient == null)
                    return ServiceResult<bool>.Fail(AuthError.NotFound, "Không tìm thấy bệnh nhân để cập nhật.");

                // 2) Latest appointment (must have Schedule)
                var latestAppointment = await _context.Appointments
                    .Include(a => a.Schedule)
                    .Where(a => a.PatientId == patientId && a.Schedule != null)
                    .OrderByDescending(a => a.Schedule!.WorkDate)
                    .FirstOrDefaultAsync(ct);

                if (latestAppointment == null)
                    return ServiceResult<bool>.Fail(AuthError.NotFound, "Bệnh nhân chưa có cuộc hẹn nào để cập nhật.");

                bool isUpdated = false;

                // 3) Update Symptom
                if (!string.IsNullOrWhiteSpace(dto.Symptoms))
                {
                    latestAppointment.Symptom = dto.Symptoms.Trim();
                    isUpdated = true;
                }

                // 4) Upsert Prescription
                if (!string.IsNullOrWhiteSpace(dto.Prescription))
                {
                    var existingPrescription = await _context.Prescriptions
                        .FirstOrDefaultAsync(pre => pre.AppointId == latestAppointment.AppointId, ct);

                    if (existingPrescription != null)
                    {
                        existingPrescription.Description = dto.Prescription.Trim();
                        existingPrescription.DateCreated = DateTime.Now;
                    }
                    else
                    {
                        await _context.Prescriptions.AddAsync(new Prescription
                        {
                            AppointId = latestAppointment.AppointId,
                            Description = dto.Prescription.Trim(),
                            DateCreated = DateTime.Now,
                            IsActive = true
                        }, ct);
                    }

                    isUpdated = true;
                }

                if (!isUpdated)
                    return ServiceResult<bool>.Fail(AuthError.BadRequest, "Không có thông tin nào để cập nhật.");

                await _context.SaveChangesAsync(ct);
                return ServiceResult<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdatePatientAsync] Error: {ex.Message}");
                return ServiceResult<bool>.Fail(AuthError.ServerError, "Đã xảy ra lỗi khi cập nhật thông tin bệnh nhân.");
            }
        }

        // ==================== DELETE ====================
        public async Task<bool> DeletePatientAsync(int patientId, CancellationToken ct = default)
        {
            var existing = await _repo.GetByIdAsync(patientId, ct);
            if (existing == null) return false;
            return await _repo.DeleteAsync(patientId, ct);
        }
    }
}
