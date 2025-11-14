// Program.cs (.NET 8)
using BookMyDoctor_WebAPI.Data;
using BookMyDoctor_WebAPI.Data.Repositories;
using BookMyDoctor_WebAPI.Helpers;
using BookMyDoctor_WebAPI.Repositories;
// ===== CHÚ Ý NAMESPACE REPOSITORY =====
using BookMyDoctor_WebAPI.Services;               // <-- cần để thấy PasswordHasherAdapter
using BookMyDoctor_WebAPI.Services.Chat;
using BookMyDoctor_WebAPI.Services.Register;
// using BookMyDoctor_WebAPI.Data.Repositories;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using static BookMyDoctor_WebAPI.Data.Repositories.AuthRepository;
using ProfileRepository = BookMyDoctor_WebAPI.Data.Repositories.ProfileRepository;

var builder = WebApplication.CreateBuilder(args);

// ================= Logging =================
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// ================= DbContext =================
builder.Services.AddDbContext<DBContext>(opt =>
{
    opt.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql => sql.MigrationsAssembly(typeof(DBContext).Assembly.FullName));
});

// ================= Repositories =================
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAuthRepository, AuthRepository>();
builder.Services.AddScoped<IOtpRepository, OtpRepository>();            // + OTP repo
builder.Services.AddScoped<IProfileRepository, ProfileRepository>();
builder.Services.AddScoped<IPatientRepository, PatientRepository>();
builder.Services.AddScoped<IDoctorRepository, DoctorRepository>();
builder.Services.AddScoped<IScheduleRepository, ScheduleRepository>();
builder.Services.AddScoped<IOwnerRepository, OwnerRepository>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
//============== AI Chatbox =========================
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection("GoogleAi"));
builder.Services.Configure<BackendOptions>(builder.Configuration.GetSection("Backend"));

// Typed HttpClient cho Gemini & Backend thật
builder.Services.AddHttpClient<GeminiClient>();
builder.Services.AddHttpClient<BookingBackend>();

// Handler AI + ChatService
builder.Services.AddSingleton<BookingBackendHandler>();
builder.Services.AddScoped<ChatService>();

// ============= !!! QUAN TRỌNG: Hasher DI =============
// ❌ BỎ dòng cũ vì PasswordHasher là static helper, không inject được:
// builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
// ✅ Đúng: dùng Adapter bọc helper để DI
builder.Services.AddScoped<IPasswordHasher, PasswordHasherAdapter>();

// ================= Services =================
builder.Services.AddScoped<IRegisterService, RegisterService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IPatientService, PatientService>();
builder.Services.AddScoped<IDoctorService, DoctorService>();
builder.Services.AddScoped<IScheduleService, ScheduleService>();            // + interface
builder.Services.AddScoped<IOwnerService, OwnerService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<ScheduleGeneratorService>();

builder.Services.AddDataProtection();
builder.Services.AddSingleton<ITimeLimitedToken, TimeLimitedToken>();
builder.Services.AddHttpContextAccessor();

// ================= Auth: Cookie + JWT =================
//var jwtKey = builder.Configuration["Jwt:Key"] ?? "dev_super_secret_32bytes_minimum_key";
//var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "BookMyDoctor";
//var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "BookMyDoctor.Client";
//var keyBytes = Encoding.UTF8.GetBytes(jwtKey);

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opts =>
    {
        opts.Cookie.Name = "bmd_auth";
        opts.Cookie.HttpOnly = true;
        opts.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        opts.Cookie.SameSite = SameSiteMode.None;  // FE khác domain thì dùng None + HTTPS
        opts.SlidingExpiration = true;
        opts.ExpireTimeSpan = TimeSpan.FromMinutes(60);
        opts.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; },
            OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; }
        };
    });

    //.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, opt =>
    //{
    //    opt.TokenValidationParameters = new TokenValidationParameters
    //    {
    //        ValidateIssuer = true,
    //        ValidIssuers = new[] { "http://localhost", "http://192.168.1.10" },
    //        ValidAudiences = new[] { "localhost", "192.168.1.10" },
    //        //ValidIssuer = jwtIssuer,
    //        ValidateAudience = true,
    //        //ValidAudience = jwtAudience,
    //        ValidateIssuerSigningKey = true,
    //        IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
    //        ValidateLifetime = true,
    //        RoleClaimType = ClaimTypes.Role,
    //        NameClaimType = ClaimTypes.NameIdentifier
    //    };
        // 🔹 Thêm xử lý sự kiện để bắt token
        //opt.Events = new JwtBearerEvents
        //{
        //    OnMessageReceived = context =>
        //    {
        //        // Lấy token từ header Authorization
        //        var token = context.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();

        //        // Hoặc nếu token nằm trong query (ví dụ cho SignalR/WebSocket)
        //        if (string.IsNullOrEmpty(token))
        //        {
        //            token = context.Request.Query["access_token"];
        //        }

        //        // Log token hoặc xử lý tùy chỉnh
        //        if (!string.IsNullOrEmpty(token))
        //        {
        //            Console.WriteLine($"🔑 JWT Token: {token}");
        //            context.Token = token;
        //        }

        //        return Task.CompletedTask;
        //    },
        //    OnAuthenticationFailed = context =>
        //    {
        //        Console.WriteLine($"❌ Token invalid: {context.Exception.Message}");
        //        return Task.CompletedTask;
        //    },
        //    OnTokenValidated = context =>
        //    {
        //        Console.WriteLine("✅ Token hợp lệ!");
        //        return Task.CompletedTask;
        //    }
        //};
    //});

builder.Services.AddAuthorization();

// ================= Controllers/JSON =================
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = null;
    });
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.SuppressModelStateInvalidFilter = true;
});

// ================= Swagger =================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "BookMyDoctor API", Version = "v1" });

    //var scheme = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    //{
    //    Name = "Authorization",
    //    Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
    //    Scheme = "bearer",
    //    BearerFormat = "JWT",
    //    In = Microsoft.OpenApi.Models.ParameterLocation.Header,
    //    Description = "Nhập: Bearer {token}"
    //};
    //c.AddSecurityDefinition("Bearer", scheme);
    //c.AddSecurityRequirement(new() { [scheme] = new List<string>() });
});

// =========== Hangfire config =================
builder.Services.AddHangfire(config =>
    config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
          .UseSimpleAssemblyNameTypeSerializer()
          .UseRecommendedSerializerSettings()
          .UseSqlServerStorage(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHangfireServer();

// ================= CORS =================
//const string AllowAll = "AllowAll";
//builder.Services.AddCors(o => o.AddPolicy(AllowAll, p => p
//    .AllowAnyOrigin()
//    .AllowAnyHeader()
//    .AllowAnyMethod()
//));
const string AllowFrontend = "AllowFrontend";
builder.Services.AddCors(o => o.AddPolicy(AllowFrontend, p => p
    .WithOrigins("https://doctorcare.id.vn", "http://26.240.106.147:3000", "http://localhost:3000")   // 👈 IP hoặc domain FE
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()   // nếu bạn dùng Cookie hoặc Auth Header
));


// ================= App =================
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();                // 👈 nên bật cho đồng bộ HTTPS

// Hangfire
app.UseHangfireDashboard("/hangfire");
// ĐÚNG THỨ TỰ:
app.UseRouting();
app.UseCors(AllowFrontend);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();