namespace Tools;

public class ToolRequest
{
	public string? tool { get; set; }
	public List<Dictionary<string, object>>? parameters { get; set; }
	public string? toolresponseformattype { get; set; }
}

