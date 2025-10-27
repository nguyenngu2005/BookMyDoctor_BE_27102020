// Magic. Don't touch
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using BookMyDoctor_WebAPI.Models;

namespace BookMyDoctor_WebAPI.Data
{
    public sealed class DBContext : DbContext
    {
        public DBContext(DbContextOptions<DBContext> options) : base(options) { }

        // DbSets
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<User> Users => Set<User>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<Schedule> Schedules => Set<Schedule>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<Prescription> Prescriptions => Set<Prescription>();
        public DbSet<OtpTicket> OtpTickets => Set<OtpTicket>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ========= Converters cho DateOnly/TimeOnly =========
            var dateOnlyConverter = new ValueConverter<DateOnly, DateTime>(
                v => v.ToDateTime(TimeOnly.MinValue),
                v => DateOnly.FromDateTime(v));

            var timeOnlyConverter = new ValueConverter<TimeOnly, TimeSpan>(
                v => v.ToTimeSpan(),
                v => TimeOnly.FromTimeSpan(v));

            // ========= roles =========
            modelBuilder.Entity<Role>(e =>
            {
                e.ToTable("roles");
                e.HasKey(x => x.RoleId);

                e.Property(x => x.RoleId)
                    .HasMaxLength(10)
                    .IsRequired();

                e.Property(x => x.RoleName)
                    .HasMaxLength(100)
                    .IsRequired();

                e.HasIndex(x => x.RoleName)
                    .IsUnique()
                    .HasDatabaseName("IX_roles_RoleName_UQ");

                // Seed 3 role cơ bản
                e.HasData(
                    new Role { RoleId = "R01", RoleName = "Admin" },
                    new Role { RoleId = "R02", RoleName = "Staff" },
                    new Role { RoleId = "R03", RoleName = "User" }
                );
            });

            // ========= users =========
            modelBuilder.Entity<User>(e =>
            {
                e.ToTable("users");
                e.HasKey(x => x.UserId);

                e.Property(x => x.Username)
                    .HasMaxLength(100)
                    .IsRequired();

                e.HasIndex(x => x.Username)
                    .IsUnique()
                    .HasDatabaseName("IX_users_Username_UQ");

                // Hash & Salt đúng SQL type
                e.Property(x => x.PasswordHash)
                    .HasColumnType("varbinary(64)")
                    .IsRequired();

                e.Property(x => x.PasswordSalt)
                    .HasColumnType("varbinary(32)")
                    .IsRequired();

                e.Property(x => x.Phone)
                    .HasMaxLength(15)
                    .IsRequired();

                e.HasIndex(x => x.Phone)
                    .IsUnique()
                    .HasDatabaseName("IX_users_Phone_UQ");

                e.Property(x => x.Email)
                    .HasMaxLength(250);

                // unique nhưng cho phép null
                e.HasIndex(x => x.Email)
                    .IsUnique()
                    .HasFilter("[Email] IS NOT NULL")
                    .HasDatabaseName("IX_users_Email_UQ_NOTNULL");

                // Default RoleId tại DB để an toàn khi insert thiếu
                e.Property(x => x.RoleId)
                    .HasMaxLength(10)
                    .HasDefaultValue("R03")
                    .IsRequired();

                // FK User.RoleId -> Role.RoleId (restrict xóa role)
                e.HasOne(x => x.Role)
                    .WithMany(r => r.Users)
                    .HasForeignKey(x => x.RoleId)
                    .HasConstraintName("FK_users_roles_RoleId")
                    .OnDelete(DeleteBehavior.Restrict);

                // Index phụ trợ cho truy vấn theo RoleId
                e.HasIndex(x => x.RoleId)
                    .HasDatabaseName("IX_users_RoleId");
            });

            // ========= doctors =========
            modelBuilder.Entity<Doctor>(e =>
            {
                e.ToTable("doctors", t =>
                {
                    t.HasCheckConstraint("CHK_Doctor_Gender", "Gender IN ('Male','Female')");
                    t.HasCheckConstraint("CHK_Doctor_Exp", "[Experience_year] >= 0");
                });

                e.HasKey(x => x.DoctorId);

                e.Property(x => x.Name).HasMaxLength(100).IsRequired();
                e.Property(x => x.Gender).HasMaxLength(6).IsRequired();
                e.Property(x => x.DateOfBirth).HasConversion(dateOnlyConverter).HasColumnType("date").IsRequired();
                e.Property(x => x.Identification).HasMaxLength(20);
                e.Property(x => x.Phone).HasMaxLength(15);
                e.Property(x => x.Email).HasMaxLength(250);
                e.Property(x => x.Address);
                e.Property(x => x.Department).HasMaxLength(50).IsRequired();
                e.Property(x => x.Experience_year).HasColumnName("Experience_year");

                e.HasOne(x => x.User)
                    .WithMany(u => u.Doctors)
                    .HasForeignKey(x => x.UserId)
                    .HasConstraintName("FK_doctors_users_UserId")
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ========= schedules =========
            modelBuilder.Entity<Schedule>(e =>
            {
                e.ToTable("schedules", t =>
                {
                    t.HasCheckConstraint("CHK_Schedule_Status", "Status IN ('Scheduled','Completed','Cancelled')");
                });

                e.HasKey(x => x.ScheduleId);

                e.Property(x => x.WorkDate).HasConversion(dateOnlyConverter).HasColumnType("date").IsRequired();
                e.Property(x => x.StartTime).HasConversion(timeOnlyConverter).HasColumnType("time").HasPrecision(0).IsRequired();
                e.Property(x => x.EndTime).HasConversion(timeOnlyConverter).HasColumnType("time").HasPrecision(0).IsRequired();

                e.Property(x => x.Status)
                    .HasMaxLength(10)
                    .HasDefaultValue("Scheduled")
                    .IsRequired();

                e.HasOne(x => x.Doctor)
                    .WithMany(d => d.Schedules)
                    .HasForeignKey(x => x.DoctorId)
                    .HasConstraintName("FK_schedules_doctors_DoctorId")
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ========= patients =========
            modelBuilder.Entity<Patient>(e =>
            {
                e.ToTable("patients", t =>
                {
                    t.HasCheckConstraint("CHK_Patient_Gender", "Gender IN ('Male','Female')");
                });

                e.HasKey(x => x.PatientId);

                e.Property(x => x.Name).HasMaxLength(100).IsRequired();
                e.Property(x => x.Gender).HasMaxLength(6).IsRequired();
                e.Property(x => x.DateOfBirth).HasConversion(dateOnlyConverter).HasColumnType("date").IsRequired();
                e.Property(x => x.Phone).HasMaxLength(15);
                e.Property(x => x.Email).HasMaxLength(250);
                e.Property(x => x.Address);

                e.HasOne(x => x.User)
                    .WithMany(u => u.Patients)
                    .HasForeignKey(x => x.UserId)
                    .HasConstraintName("FK_patients_users_UserId")
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // ========= appointments =========
            modelBuilder.Entity<Appointment>(e =>
            {
                e.ToTable("appointments", t =>
                {
                    t.HasCheckConstraint("CHK_Appointment_Status", "Status IN ('Scheduled','Completed','Cancelled')");
                });

                e.HasKey(x => x.AppointId);

                e.Property(x => x.AppointHour)
                    .HasConversion(timeOnlyConverter)
                    .HasColumnType("time")
                    .HasPrecision(0)
                    .IsRequired();

                e.Property(x => x.Status)
                    .HasMaxLength(10)
                    .HasDefaultValue("Scheduled")
                    .IsRequired();

                e.Property(x => x.Symptom);

                // Một khung giờ chỉ book 1 lần cho 1 schedule
                e.HasIndex(x => new { x.ScheduleId, x.AppointHour })
                    .IsUnique()
                    .HasDatabaseName("IX_appointments_Schedule_Hour_UQ");

                e.HasOne(x => x.Patient)
                    .WithMany(p => p.Appointments)
                    .HasForeignKey(x => x.PatientId)
                    .HasConstraintName("FK_appointments_patients_PatientId")
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Schedule)
                    .WithMany(s => s.Appointments)
                    .HasForeignKey(x => x.ScheduleId)
                    .HasConstraintName("FK_appointments_schedules_ScheduleId")
                    .OnDelete(DeleteBehavior.NoAction);
            });

            // ========= otp_tickets =========
            modelBuilder.Entity<OtpTicket>(e =>
            {
                e.ToTable("OtpTickets");
                e.HasKey(x => x.OtpId);

                e.Property(x => x.UserId).IsRequired();

                e.Property(x => x.Purpose).HasMaxLength(30).IsRequired();
                e.Property(x => x.Channel).HasMaxLength(10).IsRequired();

                // SHA-512 HEX: 128 ký tự
                e.Property(x => x.OtpHash).HasMaxLength(128).IsRequired();

                // DateTimeOffset -> datetimeoffset (đúng chuẩn, tránh lỗi TimeOnly)
                e.Property(x => x.CreatedAtUtc)
                    .HasColumnType("datetimeoffset")
                    .HasDefaultValueSql("SYSDATETIMEOFFSET()")
                    .IsRequired();

                e.Property(x => x.ExpireAtUtc)
                    .HasColumnType("datetimeoffset")
                    .HasDefaultValueSql("DATEADD(MINUTE, 5, SYSDATETIMEOFFSET())")
                    .IsRequired();

                e.Property(x => x.Destination).HasMaxLength(200);
                e.Property(x => x.Attempts).HasDefaultValue(0).IsRequired();
                e.Property(x => x.Used).HasDefaultValue(false).IsRequired();

                e.HasOne(x => x.User)
                    .WithMany(u => u.OtpTickets)
                    .HasForeignKey(x => x.UserId)
                    .HasConstraintName("FK_otps_users_UserId")
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasIndex(x => new { x.UserId, x.Purpose })
                    .HasDatabaseName("IX_OtpTickets_User_Purpose");

                e.HasIndex(x => new { x.ExpireAtUtc, x.Used })
                    .HasDatabaseName("IX_OtpTickets_Active");
            });
        }
    }
}
