using Bonsai;
using System;
using System.ComponentModel;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Reactive.Linq;
using System.Reactive.Disposables;

[Combinator]
[Description("Maintains a persistent TCP connection to a HelvarNet router. Each input string is sent as a command (with the optional Terminator appended); replies from the router are emitted as strings, split on the '#' message terminator.")]
[WorkflowElementCategory(ElementCategory.Combinator)]
public class HelvarNetClient
{
    [Description("IP address or hostname of the Helvar router.")]
    public string Host { get; set; } = "10.254.1.4";

    [Description("TCP port of the HelvarNet interface (fixed at 50000 on Helvar routers).")]
    public int Port { get; set; } = 50000;

    [Description("Extra bytes appended after each command. The protocol terminator '#' is already part of the command string, so leave this empty unless your router needs a trailing CR/LF. The escapes \\r \\n \\t are interpreted.")]
    public string Terminator { get; set; } = string.Empty;

    public IObservable<string> Process(IObservable<string> source)
    {
        return Observable.Create<string>(observer =>
        {
            var terminator = Unescape(Terminator);
            var encoding = Encoding.ASCII;

            var client = new TcpClient();
            client.Connect(Host, Port);
            var stream = client.GetStream();
            var cancellation = new CancellationTokenSource();
            var writeLock = new object();

            // Background reader: accumulate incoming bytes and emit each complete
            // '#'-terminated reply as it arrives.
            Task.Run(async () =>
            {
                var buffer = new byte[4096];
                var pending = string.Empty;
                try
                {
                    while (!cancellation.IsCancellationRequested)
                    {
                        var count = await stream.ReadAsync(buffer, 0, buffer.Length, cancellation.Token);
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
                    if (!cancellation.IsCancellationRequested) observer.OnError(ex);
                }
            });

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
                        if (!cancellation.IsCancellationRequested) observer.OnError(ex);
                    }
                },
                observer.OnError);

            return Disposable.Create(() =>
            {
                cancellation.Cancel();
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
