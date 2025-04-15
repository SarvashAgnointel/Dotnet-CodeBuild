using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace TravelAd_Api.Models
{
    public class AdminModel
    {
        public class UpdateCampaign
        {
            public int campaignId { get; set; }
            public string status { get; set; }
            public int serverId { get; set; }

            public int connectionId { get; set; }
        }

        public class UpdateShortCodeMapper
        {
            public int workspaceId { get; set; }
            public string data { get; set; }
        }
        public class AdvertiserChannelRequest
        {
            public int WorkspaceId { get; set; }
            public List<string> Channels { get; set; }
        }

        public class MarkNotificationsReadRequest
        {
            public int WorkspaceId { get; set; }
        }
        public class CreateNotificationRequest
        {
            public int WorkspaceId { get; set; }
            public int? CampaignId { get; set; }
            public string? CampaignName { get; set; }
            public string StatusMark { get; set; }
            public string NotificationType { get; set; }
            public JsonElement NotificationData { get; set; }
            public string? Role { get; set; }
        }

        public class AdminNotification
        {
            public int Id { get; set; }
            public string WorkspaceName { get; set; }
            public int WorkspaceId { get; set; }
            public string ChannelName { get; set; }
            public string Status { get; set; }
            public DateTime CreatedAt { get; set; }
        }

    }
}
