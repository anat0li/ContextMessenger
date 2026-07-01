namespace ContextMessenger.Core.Patching;

public interface IBuildRunner
{
    BuildResult Run(BuildRequest request);
}
