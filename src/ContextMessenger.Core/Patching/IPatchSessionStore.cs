namespace ContextMessenger.Core.Patching;

public interface IPatchSessionStore
{
    PatchSessionMetadata? Load();

    void Save(PatchSessionMetadata metadata);

    void Clear();
}
