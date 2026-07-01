namespace ContextMessenger.Core.Patching;

public interface ITestRunner
{
    TestResult Run(TestRequest request);
}
