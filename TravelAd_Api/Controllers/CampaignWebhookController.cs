using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/webhook")]
public class CampaignWebhookController : ControllerBase
{
    private static readonly List<CampaignStatusUpdate> LiveCampaigns = new();

    [HttpPost("campaign-status")]
    public IActionResult UpdateCampaignStatus([FromBody] CampaignStatusUpdate model)
    {
        if (model == null || model.WorkspaceId == 0 || model.Status != "Live")
            return BadRequest("Invalid data");

        lock (LiveCampaigns)
        {
            if (!LiveCampaigns.Any(c => c.CampaignId == model.CampaignId))
            {
                LiveCampaigns.Add(model);
            }
        }

        return Ok(new { message = "Campaign status updated", campaign = model });
    }

    [HttpGet("live-campaigns/{workspaceId}")]
    public IActionResult GetLiveCampaigns(int workspaceId)
    {
        var campaigns = LiveCampaigns.Where(c => c.WorkspaceId == workspaceId).ToList();
        return Ok(campaigns);
    }
}

public class CampaignStatusUpdate
{
    public int CampaignId { get; set; }
    public string Status { get; set; }
    public int WorkspaceId { get; set; }
}
