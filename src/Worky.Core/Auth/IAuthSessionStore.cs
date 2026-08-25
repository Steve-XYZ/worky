namespace Worky.Core.Auth;

public interface IAuthSessionStore
{
    AuthSession? Load();
    void Save(AuthSession session);
}
