using FunEvents.Application.Users.Dtos;
using FunEvents.Domain.Users;

namespace FunEvents.Application.Users;

public static class UserMappingExtensions
{
    public static UserDto ToDto(this User user) => new()
    {
        Id = user.Id,
        FullName = user.FullName,
        Email = user.Email,
        IsActive = user.IsActive
    };
}
