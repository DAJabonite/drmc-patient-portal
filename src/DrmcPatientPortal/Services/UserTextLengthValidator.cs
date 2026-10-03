using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Identity;

namespace DrmcPatientPortal.Services;

public sealed class UserTextLengthValidator : IUserValidator<ApplicationUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        var errors = new List<IdentityError>();
        if (user.Email?.Length > 256 || manager.NormalizeEmail(user.Email)?.Length > 256)
        {
            errors.Add(new IdentityError
            {
                Code = "EmailTooLong",
                Description = "Email address must be 256 characters or fewer."
            });
        }
        if (user.UserName?.Length > 256 || manager.NormalizeName(user.UserName)?.Length > 256)
        {
            errors.Add(new IdentityError
            {
                Code = "UserNameTooLong",
                Description = "User name must be 256 characters or fewer."
            });
        }

        return Task.FromResult(errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed(errors.ToArray()));
    }
}
