using RemoteForge;
using Renci.SshNet;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Management.Automation.Subsystem;

namespace SSHForge;

public sealed class SSHNetInfo : IRemoteForge
{
    public static string ForgeName => "sshnet";
    public static string ForgeDescription => "SSH.NET wrapped PowerShell session";

    public string ComputerName { get; }
    public int Port { get; }
    public string UserName { get; }
    public string? Subsystem { get; private set; }


    private SSHNetInfo(string computerName, int port, string username, string subsystem)
    {
        ComputerName = computerName;
        Port = port;
        UserName = username;
        Subsystem = subsystem;
    }

    public static IRemoteForge Create(string info)
    {
        (string hostname, int port, string? user, string? subsystem) = SSHTransport.ParseSSHInfo(info);

        if (string.IsNullOrWhiteSpace(user))
        {
            throw new ArgumentException("User must be supplied in sshnet connection string");
        }

        throw new ArgumentException($"{subsystem}");
        return new SSHNetInfo(hostname, port, user, subsystem);
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

        //Console.WriteLine($"CreateTransport with subsystem '{Subsystem}'");
        return new SSHNetTransport(connInfo, Subsystem);
        //return new SSHNetTransport(connInfo, "sudopwsh");
    }
}

public sealed class SSHNetTransport : RemoteTransport
{
    private readonly SshClient _client;
    private readonly string? _subsystem;
    private SshCommand? _cmd;
    private StreamReader? _stdoutReader;
    private StreamReader? _stderrReader;
    private StreamWriter? _stdinWriter;
    private IAsyncResult _cmdTask;
    private ShellStream? _shellStream;


    internal SSHNetTransport(ConnectionInfo connInfo, string? subsystem = null)
    {
        _client = new(connInfo);
        _subsystem = subsystem;
        //Console.WriteLine($"RemoteTransport has been created without subsystem");
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
        //Console.WriteLine($"Task has been opened {_subsystem}");
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
