namespace Tools;

public interface IAIToolModelBase
{
	/// <summary>
	/// The unique name of the tool (e.g., "GetWorkOrderCount").
	/// </summary>
	string? tool { get; set; }

	/// <summary>
	/// A description of what the tool does, used by the LLM to understand when to call it.
	/// </summary>
	string? description { get; set; }

	/// <summary>
	/// A JSON schema or description of the parameters the tool accepts.
	/// </summary>
	string? pseudo_parameters { get; set; }

	/// <summary>
	/// Gets or sets the collection of parameter sets used for configuring operations or requests.
	/// </summary>
	public List<Dictionary<string, object>> parameters { get; set; }

	string? toolresponseformattype { get; set; }
}

public interface IAITool : IAIToolModelBase
{
	Task<string> ExecuteAsync(Dictionary<string, object> parameters);
}

public class AIToolModel : IAIToolModelBase
{
	public string? tool { get; set; }
	public string? description { get; set; }
	public string? pseudo_parameters { get; set; }
	public List<Dictionary<string, object>> parameters { get; set; }
	public string? toolresponseformattype { get; set; }
}