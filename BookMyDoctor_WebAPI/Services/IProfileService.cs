using BookMyDoctor_WebAPI.RequestModel;

namespace BookMyDoctor_WebAPI.Services
{
    public interface IProfileService
    {
        Task<object?> GetProfileAsync(int userId);
        Task<string> UpdateProfileAsync(int userId, ProfileRequest request);
    }
}
