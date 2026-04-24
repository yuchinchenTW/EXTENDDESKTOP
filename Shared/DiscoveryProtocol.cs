using System;
using System.IO;
using System.Text;

namespace ExtentDesktop.Shared
{
    internal sealed class DiscoveredHostInfo
    {
        public string MachineName;
        public string HostAddress;
        public int HostPort;
        public string DisplayLabel;
        public DateTime LastSeenUtc;
    }

    internal static class DiscoveryProtocol
    {
        public const int BroadcastPort = 6202;
        public const int BroadcastIntervalMs = 2000;
        public const int HostTimeoutMs = 6000;

        public static byte[] CreateAnnouncement(string machineName, int port, string displayLabel)
        {
            using (var memory = new MemoryStream())
            using (var writer = new BinaryWriter(memory, Encoding.UTF8))
            {
                writer.Write("EXTENTDESKTOP_DISCOVERY_V1");
                writer.Write(machineName ?? string.Empty);
                writer.Write(port);
                writer.Write(displayLabel ?? string.Empty);
                writer.Flush();
                return memory.ToArray();
            }
        }

        public static bool TryParseAnnouncement(byte[] data, int length, string hostAddress, out DiscoveredHostInfo host)
        {
            host = null;

            try
            {
                using (var memory = new MemoryStream(data, 0, length, false))
                using (var reader = new BinaryReader(memory, Encoding.UTF8))
                {
                    var signature = reader.ReadString();
                    if (!string.Equals(signature, "EXTENTDESKTOP_DISCOVERY_V1", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    host = new DiscoveredHostInfo
                    {
                        MachineName = reader.ReadString(),
                        HostAddress = hostAddress,
                        HostPort = reader.ReadInt32(),
                        DisplayLabel = reader.ReadString(),
                        LastSeenUtc = DateTime.UtcNow
                    };
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
