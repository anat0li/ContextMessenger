namespace ContextMessenger.Core.Meta;

public interface IContextSession
{
    CurrentContextInfo GetCurrentContext();

    IReadOnlyList<RootProfileInfo> ListRoots();

    IReadOnlyList<TargetProfileInfo> ListTargets();

    CurrentContextInfo SetRoot(string name);

    void ApplyPendingRootSwitch();
}
