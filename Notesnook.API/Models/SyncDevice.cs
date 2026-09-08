using System.Collections.Generic;

namespace Notesnook.API.Models
{
    public class SyncDevice
    {

        public required string UserId { get; set; }
        public required string DeviceId { get; set; }
        public required long LastAccessTime { get; set; }
        public required bool IsSyncReset { get; set; }
        public string? AppVersion { get; set; }
        public string? DatabaseVersion { get; set; }
    }
}