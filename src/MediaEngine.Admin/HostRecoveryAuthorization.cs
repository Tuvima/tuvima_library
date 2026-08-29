using System.Runtime.InteropServices;
using System.Security.Principal;

namespace MediaEngine.Admin;

public interface IHostRecoveryAuthorizer
{
    void EnsureAuthorized();
}

public interface IHostPrivilegeProbe
{
    bool HasAdministrativeHostAccess();
}

public sealed class SystemHostRecoveryAuthorizer(
    IHostPrivilegeProbe privilegeProbe) : IHostRecoveryAuthorizer
{
    public SystemHostRecoveryAuthorizer()
        : this(new SystemHostPrivilegeProbe())
    {
    }

    public void EnsureAuthorized()
    {
        if (!privilegeProbe.HasAdministrativeHostAccess())
        {
            throw new UnauthorizedAccessException(
                "Host recovery requires an elevated Windows administrator terminal, root on Linux/macOS, or root inside the Engine container.");
        }
    }
}

public sealed class SystemHostPrivilegeProbe : IHostPrivilegeProbe
{
    public bool HasAdministrativeHostAccess()
    {
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        try
        {
            return GetEffectiveUserId() == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();
}
