
namespace Tools;

public class AIToolWeather : IAITool
{
    public string? tool { get; set; }// = "Weather";
    public string? description { get; set; }// = "Provides weather information.";
    public string? pseudo_parameters { get; set; }// = "{ \"city\": \"string city name\" }";
    public List<Dictionary<string, object>> parameters { get; set; }
    public string? toolresponseformattype { get; set; }

    public AIToolWeather()
    {
        tool = "GetWeatherForecast";
        description = "Provides weather information. It should return a formatted HTML";
		pseudo_parameters = "{ \"city\": \"string city name\" }";
        toolresponseformattype = "html";
	}

    public async Task<string> ExecuteAsync(Dictionary<string, object> parameters)
    {
        string ret = "";

        ret += "User asked to look for a weather in the current city> " + parameters["city"] + "\n";

		return ret;
    }
}
