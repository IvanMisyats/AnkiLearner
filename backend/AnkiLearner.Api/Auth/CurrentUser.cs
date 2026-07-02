using System.Security.Claims;
using AnkiLearner.Core.Abstractions;

namespace AnkiLearner.Api.Auth;

/// <summary>Reads the user id from the validated JWT ("sub" is mapped to NameIdentifier).</summary>
public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid UserId
    {
        get
        {
            var value = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return value is not null && Guid.TryParse(value, out var id)
                ? id
                : throw new InvalidOperationException("No authenticated user in the current request.");
        }
    }
}
