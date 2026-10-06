using System.IO.Ports;
using Ape.Core.Config.Models;

namespace Ape.Module.DeviceManager.Services.DeviceManagement;

/// <summary>Serial port line settings for <see cref="SerialDataHandler"/>. Read from host JSON via <see cref="Parse"/>.</summary>
public sealed class SerialPortOptions
{
    public int BaudRate { get; init; } = 9600;
    public Parity Parity { get; init; } = Parity.None;
    public int DataBits { get; init; } = 8;
    public StopBits StopBits { get; init; } = StopBits.One;
    public int ReadTimeoutMs { get; init; } = 500;
    public int WriteTimeoutMs { get; init; } = 500;

    /// <summary>
    /// Reads <c>baudRate</c>, <c>parity</c>, <c>dataBits</c>, <c>stopBits</c>,
    /// <c>readTimeoutMs</c>, <c>writeTimeoutMs</c> from a config object (typically <c>serial</c>).
    /// Missing keys keep <paramref name="fallback"/> (default 9600 8N1).
    /// </summary>
    public static SerialPortOptions Parse(IConfigNode? node, SerialPortOptions? fallback = null)
    {
        var d = fallback ?? new SerialPortOptions();
        if (node == null)
        {
            return d;
        }

        return new SerialPortOptions
        {
            BaudRate = node.GetInt("baudRate", d.BaudRate),
            Parity = ParseParity(node.GetString("parity", d.Parity.ToString()), d.Parity),
            DataBits = node.GetInt("dataBits", d.DataBits),
            StopBits = ParseStopBits(node.GetString("stopBits", d.StopBits.ToString()), d.StopBits),
            ReadTimeoutMs = node.GetInt("readTimeoutMs", d.ReadTimeoutMs),
            WriteTimeoutMs = node.GetInt("writeTimeoutMs", d.WriteTimeoutMs),
        };
    }

    private static Parity ParseParity(string raw, Parity fallback)
        => Enum.TryParse(raw, ignoreCase: true, out Parity parsed) ? parsed : fallback;

    private static StopBits ParseStopBits(string raw, StopBits fallback)
        => Enum.TryParse(raw, ignoreCase: true, out StopBits parsed) ? parsed : fallback;
}
