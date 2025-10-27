using BookMyDoctor_WebAPI.RequestModel;

namespace BookMyDoctor_WebAPI.Services
{
    public interface IOwnerService
    {
        Task<(bool Success, string Message)> CreateDoctorAccountAsync(CreateDoctorRequest request);
    }
}
