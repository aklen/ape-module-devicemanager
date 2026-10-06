namespace Ape.Module.DeviceManager.Services.DeviceManagement;

/// <summary>
/// Data display modes for serial/HID device data logging.
/// </summary>
public enum DataDisplayMode
{
    /// <summary>
    /// Show only hexadecimal representation (e.g., "48-65-6C-6C-6F")
    /// </summary>
    HexOnly,

    /// <summary>
    /// Show only ASCII representation (e.g., "Hello")
    /// </summary>
    AsciiOnly,

    /// <summary>
    /// Show both hex and ASCII representations (default)
    /// </summary>
    Both,

    /// <summary>
    /// Show raw byte array as decimal numbers (e.g., "[72, 101, 108, 108, 111]")
    /// </summary>
    Decimal,

    /// <summary>
    /// Show binary representation (e.g., "01001000 01100101...")
    /// </summary>
    Binary
}

/// <summary>
/// Helper methods for formatting device data in different display modes.
/// </summary>
public static class DataFormatter
{
    public static string Format(byte[] data, DataDisplayMode mode)
    {
        return mode switch
        {
            DataDisplayMode.HexOnly => BitConverter.ToString(data),

            DataDisplayMode.AsciiOnly => System.Text.Encoding.UTF8.GetString(data)
                .Replace('\n', '↵')
                .Replace('\r', '↲')
                .Replace('\t', '→'),

            DataDisplayMode.Both => $"HEX: {BitConverter.ToString(data)} | ASCII: \"{System.Text.Encoding.UTF8.GetString(data).Replace('\n', '↵').Replace('\r', '↲')}\"",

            DataDisplayMode.Decimal => $"[{string.Join(", ", data)}]",

            DataDisplayMode.Binary => string.Join(" ", data.Select(b => Convert.ToString(b, 2).PadLeft(8, '0'))),

            _ => BitConverter.ToString(data)
        };
    }
}
