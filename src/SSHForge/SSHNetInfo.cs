using RemoteForge;
using Renci.SshNet;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;

namespace SSHForge;

public sealed class SSHNetInfo : IRemoteForge
{
    public static string ForgeName => "sshnet";
    public static string ForgeDescription => "SSH.NET wrapped PowerShell session";

    public string ComputerName { get; }
    public int Port { get; }
    public string UserName { get; }
    public string? Subsystem { get; private set; }


    private SSHNetInfo(string computerName, int port, string username)
    {
        ComputerName = computerName;
        Port = port;
        UserName = username;

        Console.WriteLine($"SSHNetInfo has been created withou subsystem");
    }

    private SSHNetInfo(string computerName, int port, string username, string? subsystem = null)
    {
        ComputerName = computerName;
        Port = port;
        UserName = username;
        Subsystem = subsystem;

        Console.WriteLine($"SSHNetInfo has been created with subsystem '{Subsystem}'");
    }

    public static IRemoteForge Create(string info, string subsystem)
    {
        (string hostname, int port, string? user) = SSHTransport.ParseSSHInfo(info);

        if (string.IsNullOrWhiteSpace(user))
        {
            throw new ArgumentException("User must be supplied in sshnet connection string");
        }

        return new SSHNetInfo(hostname, port, user, subsystem);
    }

    public static IRemoteForge Create(string info)
    {
        (string hostname, int port, string? user) = SSHTransport.ParseSSHInfo(info);

        if (string.IsNullOrWhiteSpace(user))
        {
            throw new ArgumentException("User must be supplied in sshnet connection string");
        }

        return new SSHNetInfo(hostname, port, user);
    }

    public RemoteTransport CreateTransport()
    {
        ConnectionInfo connInfo = new ConnectionInfo(
            ComputerName,
            Port,
            UserName,
            new AuthenticationMethod[]{
                new PasswordAuthenticationMethod(UserName, Environment.GetEnvironmentVariable("TEST_PASS")),
            });

        return new SSHNetTransport(connInfo);
    }

    public RemoteTransport CreateTransport(string? subsystem = null)
    {
        ConnectionInfo connInfo = new ConnectionInfo(
            ComputerName,
            Port,
            UserName,
            new AuthenticationMethod[]
            {
                new PasswordAuthenticationMethod(UserName, Environment.GetEnvironmentVariable("TEST_PASS")),
            });

        return new SSHNetTransport(connInfo, subsystem ?? Subsystem);
    }

}

public sealed class SSHNetTransport : RemoteTransport
{
    private readonly SshClient _client;
    private readonly string _subsystem;
    private SshCommand? _cmd;
    private StreamReader? _stdoutReader;
    private StreamReader? _stderrReader;
    private StreamWriter? _stdinWriter;
    private IAsyncResult _cmdTask;
    private ShellStream? _shellStream;


    internal SSHNetTransport(ConnectionInfo connInfo)
    {
        _client = new(connInfo);
        Console.WriteLine($"RemoteTransport has been created without subsystem");
    }

    internal SSHNetTransport(ConnectionInfo connInfo, string subsystem)
    {
        _client = new(connInfo);
        _subsystem = subsystem;
        Console.WriteLine($"RemoteTransport has been created with subsystem {subsystem}");
    }

    protected override async Task Open(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_subsystem))
        {
            await Task.Run(() => _client.Connect(), cancellationToken);

            _shellStream = _client.CreateShellStream(_subsystem, 80, 24, 800, 600, 1024);

            _stdoutReader = new StreamReader(_shellStream);
            _stderrReader = new StreamReader(_shellStream);
            _stdinWriter = new StreamWriter(_shellStream) { AutoFlush = true };
        }
        else
        {
            await _client.ConnectAsync(cancellationToken);
            _cmd = _client.CreateCommand("pwsh -NoProfile -SSHServerMode");
            _cmdTask = _cmd.BeginExecute();
            _stdoutReader = new(_cmd.OutputStream);
            _stderrReader = new(_cmd.ExtendedOutputStream);
            _stdinWriter = new(_cmd.CreateInputStream());
        }
        Console.WriteLine($"Task has been opened {_subsystem}");
    }

    protected override Task Close(CancellationToken cancellationToken)
    {
        if (_cmd != null)
        {
            _cmd.CancelAsync();
        }
        _client.Disconnect();
        return Task.CompletedTask;
    }

    protected override async Task<string?> ReadOutput(CancellationToken cancellationToken)
    {
        Debug.Assert(_stdoutReader != null);
        string? msg = await _stdoutReader.ReadLineAsync(cancellationToken);
        Console.WriteLine($"STDOUT: {msg}");
        Console.WriteLine("STDOUT END");
        if (msg == null)
        {
            string res = _cmd.EndExecute(_cmdTask);
            Console.WriteLine(res);
        }

        return msg;
    }

    protected override async Task<string?> ReadError(CancellationToken cancellationToken)
    {
        Debug.Assert(_stderrReader != null);
        string? msg = await _stderrReader.ReadToEndAsync(cancellationToken);
        Console.WriteLine($"STDERR: {msg}");

        return msg;
    }

    protected override async Task WriteInput(string input, CancellationToken cancellationToken)
    {
        Debug.Assert(_stdinWriter != null);
        Console.WriteLine($"STDIN: {input}");
        await _stdinWriter.WriteLineAsync(input.AsMemory(), cancellationToken);
        await _stdinWriter.FlushAsync();
    }

    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing)
        {
            _cmd?.Dispose();
        }
        base.Dispose(isDisposing);
    }
}
