namespace ARIA.Core.Interfaces;

public interface ICredentialStore
{
    void Save(string key, string value);
    string? Load(string key);
    void Delete(string key);
}
