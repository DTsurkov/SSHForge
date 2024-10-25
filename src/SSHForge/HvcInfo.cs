using RemoteForge;
using System.Management.Automation;
using System.Reflection.PortableExecutable;
using System.Security;

namespace SSHForge;

[Cmdlet(VerbsCommon.New, "HvcForgeInfo", DefaultParameterSetName = "Credential")]
[OutputType(typeof(HvcInfo))]
public sealed class NewHvcForgeInfo : PSCmdlet
{
    private PSCredential? _credential;

    [Parameter(Mandatory = true, Position = 0)]
    public string VMName { get; set; } = string.Empty;

    [Parameter]
    public int Port { get; set; } = 22;

    [Parameter(ParameterSetName = "Credential")]
    [Credential]
    public PSCredential? Credential
    {
        get => _credential;
        set => _credential = value;
    }

    [Parameter(ParameterSetName = "UserName")]
    public string? UserName
    {
        get => null;
        set => _credential = new(value, new SecureString());
    }

    [Parameter]
    public SwitchParameter SkipHostKeyCheck { get; set; }

    [Parameter]
    public string? Subsystem { get; set; }

    protected override void EndProcessing()
    {
        WriteObject(new HvcInfo(
            VMName,
            Port,
            _credential,
            SkipHostKeyCheck,
            Subsystem));
    }
}

public sealed class HvcInfo : IRemoteForge
{
    public static string ForgeName => "hvc";
    public static string ForgeDescription => "Hyper-V ssh wrapped PowerShell session";

    public string ComputerName { get; }
    public int Port { get; }
    public PSCredential? Credential { get; }
    public bool SkipHostKeyCheck { get; }
    public string? Subsystem { get; }

    internal HvcInfo(
        string hostname,
        int port = 22,
        PSCredential? credential = null,
        bool skipHostKeyCheck = false,
        string? subsystem = null)
    {
        ComputerName = hostname;
        Port = port;
        Credential = credential;
        SkipHostKeyCheck = skipHostKeyCheck;
        Subsystem = subsystem;
    }

    public static IRemoteForge Create(string info)
    {
        (string hostname, int port, string? user, string? subsystem) = SSHTransport.ParseSSHInfo(info);
        PSCredential? credential = null;
        if (user != null)
        {
            credential = new(user, new());
        }

        return new HvcInfo(
            hostname,
            port: port,
            credential: credential,
            subsystem: subsystem);
    }

    public RemoteTransport CreateTransport()
        => SSHTransport.Create(
            ComputerName,
            Port,
            executable: "hvc.exe",
            credential: Credential,
            disableHostKeyCheck: SkipHostKeyCheck,
            subsystem: Subsystem);
}
