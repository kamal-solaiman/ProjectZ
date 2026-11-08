using ProjectZ.Data;
using ProjectZ.Models;
namespace ProjectZ.Services;
public sealed class AuthenticationService(UserRepository repository)
{
    private static readonly string DummyHash = PasswordHasher.Hash("not-a-user-password");
    public async Task<UserSession?> SignInAsync(string username, string password, CancellationToken token)
    {
        var row = await repository.FindAsync(username, token);
        var valid = await Task.Run(() => PasswordHasher.Verify(password, row?.PasswordHash ?? DummyHash), token);
        token.ThrowIfCancellationRequested();
        return valid && row is { IsActive: true } ? row.User : null;
    }
}
