using System.Runtime.InteropServices;

namespace HomeBusinessAssistant.Windows.SystemIntegration;

/// <summary>Creates the application-owned Task Scheduler folder through the local COM API.</summary>
internal static class TaskSchedulerFolderManager
{
    private const int FileNotFoundHResult = unchecked((int)0x80070002);

    /// <summary>Ensures <c>\HomeBusinessAssistant</c> exists without elevation or a stored password.</summary>
    public static void EnsureApplicationFolder()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows Task Scheduler is required.");
        }

        object? service = null;
        object? root = null;
        object? folder = null;
        try
        {
            Type serviceType = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)
                ?? throw new InvalidOperationException("startup.task.folder-unavailable:service-missing");
            service = Activator.CreateInstance(serviceType)
                ?? throw new InvalidOperationException("startup.task.folder-unavailable:service-missing");
            dynamic scheduler = service;
            scheduler.Connect();
            root = scheduler.GetFolder("\\");
            dynamic rootFolder = root;
            try
            {
                folder = rootFolder.GetFolder("\\HomeBusinessAssistant");
            }
            catch (COMException exception) when (exception.HResult == FileNotFoundHResult)
            {
                folder = rootFolder.CreateFolder("\\HomeBusinessAssistant", null);
            }
            catch (DirectoryNotFoundException)
            {
                folder = rootFolder.CreateFolder("\\HomeBusinessAssistant", null);
            }
        }
        catch (COMException exception)
        {
            string category = exception.HResult == unchecked((int)0x80070005)
                ? "permission-denied"
                : "command-failed";
            throw new InvalidOperationException($"startup.task.folder-unavailable:{category}", exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            throw new InvalidOperationException("startup.task.folder-unavailable:path-not-found", exception);
        }
        finally
        {
            Release(folder);
            Release(root);
            Release(service);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }
}
