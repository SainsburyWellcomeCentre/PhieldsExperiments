using Bonsai;
using System;
using System.ComponentModel;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Reactive.Linq;
using System.Reactive.Disposables;

[Combinator]
[Description("Maintains a persistent TCP connection to a HelvarNet router. Each input string is sent as a command (with the optional Terminator appended); replies from the router are emitted as strings, split on the '#' message terminator.")]
[WorkflowElementCategory(ElementCategory.Combinator)]
public class HelvarNetClient
{
    [Description("IP address or hostname of the Helvar router.")]
    public string Host { get; set; }

    [Description("TCP port of the HelvarNet interface (fixed at 50000 on Helvar routers).")]
    public int Port { get; set; }

    [Description("Extra bytes appended after each command. The protocol terminator '#' is already part of the command string, so leave this empty unless your router needs a trailing CR/LF. The escapes \\r \\n \\t are interpreted.")]
    public string Terminator { get; set; }

    public HelvarNetClient()
    {
        Host = "10.254.1.4";
        Port = 50000;
        Terminator = string.Empty;
    }

    public IObservable<string> Process(IObservable<string> source)
    {
        return Observable.Create<string>(observer =>
        {
            var terminator = Unescape(Terminator);
            var encoding = Encoding.ASCII;

            var client = new TcpClient();
            client.Connect(Host, Port);
            var stream = client.GetStream();
            var writeLock = new object();
            var running = true;

            // Background thread: read incoming bytes with a blocking read and emit
            // each complete '#'-terminated reply as it arrives. Disposing the stream
            // unblocks the read on shutdown.
            var reader = new Thread(() =>
            {
                var buffer = new byte[4096];
                var pending = string.Empty;
                try
                {
                    while (running)
                    {
                        var count = stream.Read(buffer, 0, buffer.Length);
                        if (count == 0) break; // router closed the connection
                        pending += encoding.GetString(buffer, 0, count);
                        int end;
                        while ((end = pending.IndexOf('#')) >= 0)
                        {
                            observer.OnNext(pending.Substring(0, end + 1));
                            pending = pending.Substring(end + 1);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (running) observer.OnError(ex);
                }
            });
            reader.IsBackground = true;
            reader.Start();

            // Forward each incoming command string to the router.
            var subscription = source.Subscribe(
                command =>
                {
                    try
                    {
                        var payload = encoding.GetBytes(command + terminator);
                        lock (writeLock) { stream.Write(payload, 0, payload.Length); }
                    }
                    catch (Exception ex)
                    {
                        if (running) observer.OnError(ex);
                    }
                },
                observer.OnError);

            return Disposable.Create(() =>
            {
                running = false;
                subscription.Dispose();
                try { stream.Dispose(); } catch { }
                try { client.Close(); } catch { }
            });
        });
    }

    static string Unescape(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t");
    }
}
