using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using ContextMessenger.App.Wpf.Settings;
using Microsoft.Extensions.Logging;
using WindowsInput;
using WindowsInput.Native;
using static System.Windows.Automation.AutomationElement;

namespace ContextMessenger.App.Wpf.Services;

public class WinAutomation
{
    private const int PasteSettleDelayMs = 900;
    private const int SendButtonDelayMs  = 300;
    private const int SendButtonAttempts = 5;

    internal static Process? FindMainProcess(string processName) =>
        Process.GetProcessesByName(processName)
            .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    internal const int SW_SHOWNOACTIVATE = 4;
    internal const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    public static async Task<bool> PasteIntoChatInput(TargetProfile target, bool submit, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        var processName = target.ProcessName;
        var automation = target.Automation;
        var process = FindMainProcess(processName);
        if (process is null)
        {
            logger?.LogInformation("No process found for target '{TargetName}' with process name '{ProcessName}'.", target.Name, processName);
            return false;
        }

        SetForegroundWindow(process.MainWindowHandle);

        var root = AutomationElement.FromHandle(process.MainWindowHandle);
        var input = await FindElement(
            root,
            ControlType.Edit,
            e => e.IsEnabled && !e.IsOffscreen && e.IsKeyboardFocusable && e.Name == automation.InputEditName);
        if (input is null)
        {
            logger?.LogWarning(
                "Input edit '{InputEditName}' not found for target '{TargetName}' ({ProcessName}).",
                automation.InputEditName,
                target.Name,
                processName);
            return false;
        }

        input.SetFocus();

        var inputSim = new InputSimulator();
        inputSim.Keyboard.Sleep(150)
            .ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_V);

        if (submit)
        {
            await Task.Delay(PasteSettleDelayMs);
            await SubmitOnce(root, input, inputSim, automation, target, processName, logger);
        }

        return true;
    }

    private static async Task SubmitOnce(
        AutomationElement root,
        AutomationElement input,
        InputSimulator inputSim,
        TargetAutomationSettings automation,
        TargetProfile target,
        string processName,
        ILogger? logger)
    {
        AutomationElement? button = null;
        int attempts = 1;
        while (null == (button = await FindElement(root, ControlType.Button,
                                       b => b.IsEnabled && !b.IsOffscreen && b.Name == automation.SendButtonName))
               && attempts < SendButtonAttempts)
        {
            await Task.Delay(SendButtonDelayMs);
            attempts++;
        }
        if (button is not null)
        {
            if (button.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
            {
                ((InvokePattern)pattern).Invoke();
                logger?.LogInformation("Invoked '{ButtonName}' button for target '{TargetName}' ({ProcessName}) after {Attempts} attempts.", button.Current.Name, target.Name, processName, attempts);
                return;
            }

            var rect = button.Current.BoundingRectangle;
            var x = (int)(rect.Left + rect.Width / 2);
            var y = (int)(rect.Top + rect.Height / 2);
            var (nx, ny) = NormalizeScreenCoordinates(x, y);

            inputSim.Mouse
                .MoveMouseTo(nx, ny)
                .LeftButtonClick();

            logger?.LogInformation("Clicked '{ButtonName}' button at ({X}, {Y}) for target '{TargetName}' ({ProcessName}).", button.Current.Name, x, y, target.Name, processName);
            return;
        }

        input.SetFocus();
        inputSim.Keyboard.Sleep(100)
            .KeyPress(VirtualKeyCode.RETURN);
        logger?.LogInformation("Pressed RETURN key for '{InputName}' for target '{TargetName}' ({ProcessName}).", input.Current.Name, target.Name, processName);
    }

    private static async Task<AutomationElement?> FindElement(
        AutomationElement root,
        ControlType type,
        Predicate<AutomationElementInformation> predicate,
        int attempts = 3)
    {
        bool Matches(AutomationElement el)
        {
            try
            {
                return predicate(el.Current);
            }
            catch
            {
                return false;
            }
        }

        for (var i = 0; i < attempts; i++)
        {
            var el = root.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(ControlTypeProperty, type))
                .Cast<AutomationElement>()
                .Where(Matches)
                .SingleOrDefault();
            if (el is not null)
                return el;

            await Task.Delay(500);
        }

        return null;
    }

    private static (double X, double Y) NormalizeScreenCoordinates(int x, int y)
    {
        var screenWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var screenHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        var screenLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var screenTop = GetSystemMetrics(SM_YVIRTUALSCREEN);

        var normalizedX = ((x - screenLeft) * 65535.0) / screenWidth;
        var normalizedY = ((y - screenTop) * 65535.0) / screenHeight;

        return (normalizedX, normalizedY);
    }

}
