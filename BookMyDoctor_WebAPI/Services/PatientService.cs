using BookMyDoctor_WebAPI.Data;
using BookMyDoctor_WebAPI.Data.Repositories;
using BookMyDoctor_WebAPI.Models;
using BookMyDoctor_WebAPI.RequestModel;
using Microsoft.EntityFrameworkCore;

namespace BookMyDoctor_WebAPI.Services
{
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
            CancellationToken ct)
        {
            try
            {
                // 🔹 B1: Lấy danh sách bệnh nhân + user
                var patients = await (
                    from p in _context.Patients
                    join u in _context.Users on p.UserId equals u.UserId into pu
                    from user in pu.DefaultIfEmpty()
                    where p.IsActive == true
                    select new
                    {
                        Patient = p,
                        User = user
                    }).ToListAsync(ct);

                // 🔹 B2: Lấy thông tin Appointment, Prescription, Schedule
                var appointments = await _context.Appointments
                    .Include(a => a.Schedule)
                    .AsNoTracking()
                    .ToListAsync(ct);

                var prescriptions = await _context.Prescriptions
                    .Include(pre => pre.Appointment)
                    .AsNoTracking()
                    .ToListAsync(ct);

                // 🔹 B3: Map dữ liệu
                var result = patients.Select(p =>
                {
                    var patientAppointments = appointments
                        .Where(a => a.PatientId == p.Patient.PatientId)
                        .OrderByDescending(a => a.Schedule?.WorkDate);

                    var latestApp = patientAppointments.FirstOrDefault();

                    var pres = prescriptions
                        .Where(pre => pre.Appointment != null
                                   && pre.Appointment.PatientId == p.Patient.PatientId)
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
                        AppointDate = latestApp?.Schedule?.WorkDate
                    };
                }).ToList();

                // 🔹 B4: Lọc dữ liệu theo yêu cầu tìm kiếm
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var keyword = name.Trim().ToLower();
                    result = result.Where(p =>
                        (p.FullName?.ToLower().Contains(keyword) ?? false) ||
                        (p.PhoneNumber?.Contains(keyword) ?? false) ||
                        (p.Email?.ToLower().Contains(keyword) ?? false)
                    ).ToList();
                }

                if (appointDate.HasValue)
                {
                    var date = DateOnly.FromDateTime(appointDate.Value.Date);
                    result = result.Where(p => p.AppointDate.HasValue && p.AppointDate.Value == date).ToList();
                }

                if (!string.IsNullOrWhiteSpace(status))
                {
                    var s = status.Trim().ToLower();
                    result = result.Where(p => p.Status?.ToLower() == s).ToList();
                }

                return result;
            }
            catch (Exception ex)
            {
                // 🔹 Ghi log lỗi (nếu có ILogger, có thể thay bằng _logger.LogError)
                Console.WriteLine($"[GetAllPatientsAsync] Lỗi: {ex.Message}");

                // 🔹 Có thể chọn cách trả về danh sách rỗng hoặc ném lỗi tùy nhu cầu
                return new List<PatientDetailRequest>();
            }
        }

        // ==================== READ ====================
        public async Task<PatientDetailRequest?> GetPatientDetailAsync(int patientId, CancellationToken ct = default)
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
                              //TotalAppointments = _context.Appointments.Count(a => a.PatientId == p.PatientId),
                              //TotalPrescriptions = _context.Prescriptions
                              //    .Count(pr => pr.Appointment.PatientId == p.PatientId),
                              // Lấy trạng thái gần nhất của bệnh nhân
                              Status = _context.Appointments
                                .Where(a => a.PatientId == p.PatientId)
                                .Join(_context.Schedules,
                                      a => a.ScheduleId,
                                      s => s.ScheduleId,
                                      (a, s) => new { a.Status, s.WorkDate })
                                .OrderByDescending(x => x.WorkDate)
                                .Select(x => x.Status)
                                .FirstOrDefault(),

                              // Lấy danh sách triệu chứng (Symptoms)
                              Symptoms = _context.Appointments
                                .Where(a => a.PatientId == p.PatientId && a.Symptom != null && a.Schedule != null)
                                .OrderByDescending(a => a.Schedule!.WorkDate)
                                .Select(a => a.Symptom)
                                .FirstOrDefault(),

                              // Lay don thuoc 
                              // ĐƠN THUỐC GẦN NHẤT
                              Prescription = _context.Prescriptions
                                .Where(pre => pre.Appointment != null                  
                                           && pre.Appointment.PatientId == p.PatientId)
                                .OrderByDescending(pre => pre.DateCreated)
                                .Select(pre => pre.Description)
                                .FirstOrDefault(),

                              // NGÀY KHÁM GẦN NHẤT
                              AppointDate = _context.Appointments
                                .Where(a => a.PatientId == p.PatientId && a.Schedule != null)
                                .OrderByDescending(a => a.Schedule!.WorkDate)          
                                .Select(a => a.Schedule!.WorkDate)                    
                                .FirstOrDefault()

                          })
                          .AsNoTracking().FirstOrDefaultAsync(ct);
        }

        // ==================== GET PATIENTS THUOC MOT USER ==================
        public async Task<IReadOnlyList<HistoryRequest>> GetPatientHistoryAsync(
            int userId,
            CancellationToken ct = default)
        {
            // 1️⃣ Lấy thông tin lịch sử khám của tất cả bệnh nhân thuộc UserId này
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
                            Prescription = pre != null ? pre.Description ?? "Không có" : "Không có"
                        };

            return await query.AsNoTracking().ToListAsync(ct);
        }

        // ==================== UPDATE ====================
        public async Task<ServiceResult<bool>> UpdatePatientAsync(
    int patientId,
    PatientDetailRequest dto,
    CancellationToken ct = default)
        {
            try
            {
                // 🔹 1. Kiểm tra bệnh nhân có tồn tại không
                var patient = await _repo.GetByIdAsync(patientId, ct);
                if (patient == null)
                    return ServiceResult<bool>.Fail(AuthError.NotFound, "Không tìm thấy bệnh nhân để cập nhật.");

                // 🔹 2. Tìm cuộc hẹn (appointment) gần nhất của bệnh nhân
                var latestAppointment = await _context.Appointments
                    .Include(a => a.Schedule)
                    .Where(a => a.PatientId == patientId && a.Schedule != null) 
                    .OrderByDescending(a => a.Schedule!.WorkDate)               
                    .FirstOrDefaultAsync(ct);


                if (latestAppointment == null)
                    return ServiceResult<bool>.Fail(AuthError.NotFound, "Bệnh nhân chưa có cuộc hẹn nào để cập nhật.");

                bool isUpdated = false;

                // 🔹 3. Cập nhật triệu chứng (Symptom)
                if (!string.IsNullOrWhiteSpace(dto.Symptoms))
                {
                    latestAppointment.Symptom = dto.Symptoms.Trim();
                    isUpdated = true;
                }

                // 🔹 4. Cập nhật hoặc tạo mới Prescription (Đơn thuốc)
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
                        var newPrescription = new Prescription
                        {
                            AppointId = latestAppointment.AppointId,
                            Description = dto.Prescription.Trim(),
                            DateCreated = DateTime.Now,
                            IsActive = true
                        };
                        await _context.Prescriptions.AddAsync(newPrescription, ct);
                    }

                    isUpdated = true;
                }

                // 🔹 5. Nếu không có gì để cập nhật → trả về thông báo
                if (!isUpdated)
                    return ServiceResult<bool>.Fail(AuthError.BadRequest, "Không có thông tin nào để cập nhật.");

                // 🔹 6. Lưu thay đổi
                await _context.SaveChangesAsync(ct);

                return ServiceResult<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                // 🔹 Ghi log (có thể thay bằng logger thực tế)
                Console.WriteLine($"[UpdatePatientAsync] Lỗi: {ex.Message}");

                // 🔹 Trả về lỗi cho service
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
