namespace ContextMessenger.Data;

public interface IReadOnlySqlGuard
{
    void Validate(string sql);
}
