using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Tools;

namespace EssentialsAI.ViewModels;

public partial class AiViewModel : ObservableObject
{
    #region Fields
    private string? _modelPath;
    private string _statusMessage = "Ready to load model.";
    private bool _isModelLoaded;
    private ObservableCollection<UiChatMessage> _messages = new();
    private string _userInput = "Please check the current weather forecast for me in Saporo Japan";
    private IChatClient? _chatClient;
    private bool _isThinking = false;
    private string _systemPrompt = @"# Mark Down Formatted Instructions
## Role
You will be a helpful friendly assistant. 

## Tools
### You have access to the following tools to reply back for method execution.  
  
tool: `GetWorkOrderDetails`  
description: Gets detailed information about a specific work order by its ID.  
pseudo_parameters: `[{ ""id"": ""string (Work Order ID)"" }]`  
  
tool: `GetWorkOrderCount`  
description: Returns the number of work orders based on a filter (Today, Late, Done). use this tool when the user asks 'how many work orders do I have today?' or similar questions.  
pseudo_parameters: `[{ ""filter"": ""string (Today | Late | Done)"" }]`  
  
tool: `GetWeatherForecast`  
description: Use this method anything related to weather forecast. This method requires city name  
pseudo_parameters: `[{ ""city"": ""string (city name)"" }]`  
  
## Rule
### It's important to follow this protocol and the response format:  
1. Your will respond in a very strict format and will contain the following format on each and every reply you make.  
  
This is the format:  
```>! user_friendly_message ># json_data >END```

Breakdown if command:  
`>!` marks as the start of the command  
`>#` marks as the start of the tool  
`>END` marks as the end of the command
  
below is a valid example response:  
```>! Checking the current weather in Baguio ># { ""tool"": ""tool_name"", ""parameters"": [{ ""city"": ""Baguio"" }], ""toolresponseformattype"": ""html"" } >END```
  
2. Respond cleanly and respectfully. No extra details. Carefully follow the response format.
";

//It's important to follow this protocol and the response format:
//1. When the user asks a question requiring a tool, execute the necessary tool specifically for that task.
//If no valid tool, just tell that you do not have a tool for that.
//The `tool` parameter should be `null`
//for `parameters`, should be `null`
//for `toolresponseformattype` will be `text`.
//2. Your will respond in a very strict format and will contain the following format on each and every reply you make.

//first line of your response will always be like this which tells us the start of the command
//`>!`
//second line tells us the command to start the friendly message bellow. This is where you can ask for questions, suggestions, steps, or any followups or anything related to what you want to say or ask.
//`>@`
//third line will contain the tool and its parameter in JSON format.
//`>#`
//fourth line tells us the end of the command
//`>END`

//below is the sample of the command:

//>!>@ the friendly response will be inserted here
//># { ""tool"": ""tool_name"", ""parameters"": [{ ... }], ""toolresponseformattype"": ""string (html | text)"" }
//>END

//3. Response is plain text if formatType is text, and should be rendered as HTML if formatType is html.
//2. Respond cleanly and respectfully. No extra details.Carefully follow the response format.


	private bool _isDownloading;
    private string _downloadStatus = "";
    private double _downloadProgress;
    private CancellationTokenSource _cts;
    private List<IAITool> _aitools = new List<IAITool>()
    {
        new AIToolWeather()
    };
    #endregion

    #region Properties
    public string? ModelPath
    {
        get => _modelPath;
        set => SetProperty(ref _modelPath, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsModelLoaded
    {
        get => _isModelLoaded;
        set => SetProperty(ref _isModelLoaded, value);
    }

    public ObservableCollection<UiChatMessage> Messages
    {
        get => _messages;
        set => SetProperty(ref _messages, value);
    }

    public string UserInput
    {
        get => _userInput;
        set => SetProperty(ref _userInput, value);
    }

    public bool IsProcessing
    {
        get => _isThinking;
        set => SetProperty(ref _isThinking, value);
    }

    public string SystemPrompt
    {
        get => _systemPrompt;
        set => SetProperty(ref _systemPrompt, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        set
        {
            if (SetProperty(ref _isDownloading, value))
            {
                OnPropertyChanged(nameof(IsNotDownloading));
            }
        }
    }

    public bool IsNotDownloading => !IsDownloading;

    public string DownloadStatus
    {
        get => _downloadStatus;
        set => SetProperty(ref _downloadStatus, value);
    }

    public double DownloadProgress
    {
        get => _downloadProgress;
        set => SetProperty(ref _downloadProgress, value);
    }
    #endregion

    #region Constructors
    public AiViewModel()
    {
        // Auto-load model if it exists
        Task.Run(AutoLoadModelAsync);
    }
    #endregion

    #region Auto-load / Model detection
    private async Task AutoLoadModelAsync()
    {
        var targetDir = System.IO.Path.Combine(FileSystem.AppDataDirectory, "GenAI_Model");
        var targetConfig = System.IO.Path.Combine(targetDir, "genai_config.json");
        var targetModel = System.IO.Path.Combine(targetDir, "phi3-mini-4k-instruct-cpu-int4-rtn-block-32-acc-level-4.onnx");

        if (File.Exists(targetConfig) && File.Exists(targetModel))
        {
            try
            {
                MainThread.BeginInvokeOnMainThread(() => StatusMessage = "Found existing model. Loading...");

                ModelPath = targetDir;
                await Task.Run(() =>
                {
                    _chatClient = new Services.OnnxChatClient(ModelPath);
                });

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    IsModelLoaded = true;
                    StatusMessage = $"Model Loaded from: {ModelPath}";
                    Messages.Add(new UiChatMessage(ChatRole.System, "Existing Model Auto-Loaded. Ready!"));
                });
            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(() => StatusMessage = $"Auto-load failed: {ex.Message}");
            }
        }
    }
    #endregion

    #region Model Loading Commands
    [RelayCommand]
    private async Task LoadModelAsync()
    {
        try
        {
            FileResult? result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select ONNX Model (inside model folder)",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".onnx" } },
                    { DevicePlatform.Android, new[] { "application/octet-stream" } },
                    { DevicePlatform.iOS, new[] { "public.data" } },
                    { DevicePlatform.MacCatalyst, new[] { "public.data" } }
                })
            });

            if (result != null)
            {
                StatusMessage = $"Selected file: {result.FileName}. Copying to App Data...";

                var targetDir = System.IO.Path.Combine(FileSystem.AppDataDirectory, "GenAI_Model");
                Directory.CreateDirectory(targetDir);

                var targetModelPath = System.IO.Path.Combine(targetDir, "model.onnx");
                using (var sourceStream = await result.OpenReadAsync())
                using (var destStream = File.Create(targetModelPath))
                {
                    await sourceStream.CopyToAsync(destStream);
                }

                var sourceDir = System.IO.Path.GetDirectoryName(result.FullPath);
                if (!string.IsNullOrEmpty(sourceDir) && Directory.Exists(sourceDir))
                {
                    var filesToCopy = new[] { "genai_config.json", "tokenizer.json", "tokenizer_config.json" };
                    foreach (var fileName in filesToCopy)
                    {
                        var sourceFile = System.IO.Path.Combine(sourceDir, fileName);
                        if (File.Exists(sourceFile))
                        {
                            var targetFile = System.IO.Path.Combine(targetDir, fileName);
                            File.Copy(sourceFile, targetFile, true);
                        }
                    }

                    var dataFiles = Directory.GetFiles(sourceDir, "*.onnx.data");
                    foreach (var dataFile in dataFiles)
                    {
                        var fileName = System.IO.Path.GetFileName(dataFile);
                        File.Copy(dataFile, System.IO.Path.Combine(targetDir, fileName), true);
                    }
                }
                else
                {
                    StatusMessage += "\nWarning: Could not access source directory to copy config files. Model might fail if not self-contained.";
                }

                var targetConfig = System.IO.Path.Combine(targetDir, "genai_config.json");
                if (!File.Exists(targetConfig))
                {
                    StatusMessage = "Error: 'genai_config.json' not found. Please ensure it was in the source folder alongside the .onnx file.";
                    return;
                }

                ModelPath = targetDir;
                StatusMessage = $"Loading GenAI model from: {ModelPath}...";

                await Task.Run(() =>
                {
                    (_chatClient as IDisposable)?.Dispose();
                    _chatClient = new Services.OnnxChatClient(ModelPath);
                });

                IsModelLoaded = true;
                StatusMessage = $"GenAI Model loaded successfully.\nLocation: {ModelPath}";
                Messages.Clear();
                Messages.Add(new UiChatMessage(ChatRole.System, "GenAI Model Loaded & Copied. Ready to chat!"));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading model: {ex.Message}";
            IsModelLoaded = false;
            (_chatClient as IDisposable)?.Dispose();
            _chatClient = null;
        }
    }
    #endregion

    #region Downloading
    [RelayCommand]
    private async Task DownloadModelAsync()
    {
        if (IsDownloading) return;

        try
        {
            IsDownloading = true;
            DownloadProgress = 0;
            StatusMessage = "Starting download...";

            var targetDir = System.IO.Path.Combine(FileSystem.AppDataDirectory, "GenAI_Model");
            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, true);
            Directory.CreateDirectory(targetDir);

            using var client = new HttpClient();
            var baseUrl = "https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-onnx/resolve/main/cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4";

            var files = new Dictionary<string, string>
            {
                { "genai_config.json", "genai_config.json" },
                { "tokenizer.json", "tokenizer.json" },
                { "tokenizer_config.json", "tokenizer_config.json" },
                { "special_tokens_map.json", "special_tokens_map.json" },
                { "added_tokens.json", "added_tokens.json" },
                { "phi3-mini-4k-instruct-cpu-int4-rtn-block-32-acc-level-4.onnx", "phi3-mini-4k-instruct-cpu-int4-rtn-block-32-acc-level-4.onnx" },
                { "phi3-mini-4k-instruct-cpu-int4-rtn-block-32-acc-level-4.onnx.data", "phi3-mini-4k-instruct-cpu-int4-rtn-block-32-acc-level-4.onnx.data" }
            };

            var totalFiles = files.Count;
            var currentFileIndex = 0;

            foreach (var file in files)
            {
                var remoteName = file.Key;
                var localName = file.Value;
                var url = $"{baseUrl}/{remoteName}?download=true";
                var localPath = System.IO.Path.Combine(targetDir, localName);

                DownloadStatus = $"Downloading {localName}... ({currentFileIndex + 1}/{totalFiles})";

                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    if (remoteName.EndsWith(".onnx"))
                    {
                        url = $"{baseUrl}/model.onnx";
                        response.Dispose();
                        var response2 = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                        if (response2.IsSuccessStatusCode)
                        {
                            using var stream = await response2.Content.ReadAsStreamAsync();
                            using var fileStream = File.Create(localPath);
                            await CopyStreamWithProgressAsync(stream, fileStream, response2.Content.Headers.ContentLength);
                            currentFileIndex++;
                            continue;
                        }
                    }
                    else if (remoteName.EndsWith(".onnx.data"))
                    {
                        url = $"{baseUrl}/model.onnx.data";
                        response.Dispose();
                        var response3 = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                        if (response3.IsSuccessStatusCode)
                        {
                            using var stream = await response3.Content.ReadAsStreamAsync();
                            using var fileStream = File.Create(localPath);
                            await CopyStreamWithProgressAsync(stream, fileStream, response3.Content.Headers.ContentLength);
                            currentFileIndex++;
                            continue;
                        }
                    }

                    throw new Exception($"Failed to download {remoteName}: {response.StatusCode}");
                }

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStreamOut = File.Create(localPath);

                await CopyStreamWithProgressAsync(contentStream, fileStreamOut, response.Content.Headers.ContentLength);
                currentFileIndex++;
            }

            DownloadStatus = "Download Complete. Loading Model...";

            ModelPath = targetDir;
            await Task.Run(() =>
            {
                (_chatClient as IDisposable)?.Dispose();
                _chatClient = new Services.OnnxChatClient(ModelPath);
            });

            IsModelLoaded = true;
            StatusMessage = "Model Downloaded and Loaded.";
            Messages.Clear();
            Messages.Add(new UiChatMessage(ChatRole.System, "Model Ready!"));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Download Failed: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
            DownloadProgress = 0;
            DownloadStatus = "";
        }
    }
    #endregion

    #region Chat
    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(UserInput) || _chatClient == null)
            return;

        IsProcessing = true;

        _cts = new CancellationTokenSource();

        // recontext (refresh memory)
        var fullContext = new List<UiChatMessage>();
        {
            // add system message
            fullContext.Add(new UiChatMessage(ChatRole.System, SystemPrompt));
            // add previous messages
            foreach (var msg in Messages)
                fullContext.Add(new UiChatMessage(msg.Role, msg.StreamingText));
            // add user input
            var userMsg = new UiChatMessage(ChatRole.User, UserInput);
            Messages.Add(userMsg); // add to UI
            fullContext.Add(userMsg); // add to AI context
        }

        string currentInput = UserInput;
        UserInput = string.Empty;

        string toolJSON = string.Empty;

        try
        {
            // prepare assistant response
            var assistantMsg = new UiChatMessage(ChatRole.Assistant, "");
            Messages.Add(assistantMsg);

            await Task.Delay(1);

            StringBuilder sb = new StringBuilder();
            string text = string.Empty;
            string st = string.Empty; // "s"antized "t"ext
            string role = string.Empty;
            bool startCommand = false;
            bool startFriendlyMessage = false;
            bool startTool = false;
            bool startEnding = false;
            bool validResponseStart = false;

            try
            {
                await foreach (var token in _chatClient.GetStreamingResponseAsync(fullContext, cancellationToken: _cts.Token))
                {
                    role = token.Role?.Value?.ToString() ?? "";

                    text = token.Text;
                    st = text.Trim();
                    sb.Append(text);

                    System.Diagnostics.Debug.WriteLine("token=" + token);

                    // start of command
                    if (st == ">")
                    {
                        startCommand = true;
                        validResponseStart = true;

						continue;
                    }

                    //// if command started
                    //if (startCommand)
                    //    if (st == "!")
                    //    {
                    //        validResponseStart = true;
                    //        continue;
                    //    }

                    // check commands
                    if (startCommand && validResponseStart)
                    {
                        if (st == "!")
                        {
                            startFriendlyMessage = true;
                            startTool = false;
                            startEnding = false;
                            continue;
                        }
                        else if (st == "#")
                        {
                            startTool = true;
                            startFriendlyMessage = false;
                            startEnding = false;
                            continue;
                        }
                        else if (st == "END")
                        {
                            startEnding = true;
                            startFriendlyMessage = false;
                            startTool = false;
                            continue;
                        }
                    }

                    if (startCommand && validResponseStart && startFriendlyMessage)
                        assistantMsg.StreamingText += text;
                    if (startCommand && validResponseStart && startTool)
                        toolJSON += text;
                    if (startCommand && validResponseStart && startEnding)
                        StopResponse();
                }
            }
            catch (System.OperationCanceledException)
            {

            }

			// store raw, not the post processed msg
			var rawmsg = new UiChatMessage(ChatRole.Assistant, sb.ToString());
			fullContext.Add(rawmsg);

			if (string.IsNullOrEmpty(toolJSON))
                return;

            // execute tool
            {
				string json = SanitizeJson(toolJSON);

				if (IsValidJson(json))
                {
                    json = $"[{json}]";

					//if(toolJSON)
					//string json = SanitizeJson(toolJSON);

					//var tools = System.Text.Json.JsonSerializer.Deserialize<List<AIToolModel>>(json);
					var tools = System.Text.Json.JsonSerializer.Deserialize<List<AIToolModel>>(json);
					if (tools is null) return;
                    foreach (var tool in tools)
                    {
                        if (tool is not null && string.IsNullOrWhiteSpace(tool.tool))
                            return;
                        if (tool is not null && tool.parameters is null)
                            return;

                        // execute tool
                        foreach (var thetool in _aitools)
                        {
                            if (tool.tool == thetool.tool)
                            {
                                var result = await thetool.ExecuteAsync(tool.parameters[0]);
                                var msg = new UiChatMessage(ChatRole.Assistant, result);
                                Messages.Add(msg);

                                break;
                            }
                        }
                    }
                }
            }
		}
        catch (Exception ex)
        {
            Messages.Add(new UiChatMessage(ChatRole.System, $"Error: {ex.Message}\r\ntoolJson: {toolJSON}"));
        }
        finally
        {
            IsProcessing = false;
        }
    }

	public static string SanitizeJson(string input)
	{
		if (string.IsNullOrEmpty(input))
			return input;

		var sb = new StringBuilder(input.Length);

		for (int i = 0; i < input.Length; i++)
		{
			char c = input[i];

			// Skip BOM
			if (c == '\uFEFF')
				continue;

			// Skip null characters
			if (c == '\0')
				continue;

			// Replace smart quotes
			if (c == '“' || c == '”')
			{
				sb.Append('"');
				continue;
			}

			if (c == '‘' || c == '’')
			{
				sb.Append('\'');
				continue;
			}

			// Remove invalid control characters
			// Allow: \t (9), \n (10), \r (13)
			if (c < 32 && c != '\t' && c != '\n' && c != '\r')
				continue;

			sb.Append(c);
		}

		return sb.ToString();
	}

	/// <summary>
	/// Checks if a string is valid JSON.
	/// Returns true if valid, false otherwise.
	/// </summary>
	public static bool IsValidJson(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
			return false;

		try
		{
			using var doc = JsonDocument.Parse(json);
			return true;
		}
		catch (JsonException jex)
		{
			return false;
		}
	}
	#endregion

	#region Helpers
	private async Task CopyStreamWithProgressAsync(Stream source, Stream destination, long? totalBytes)
    {
        var buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await destination.WriteAsync(buffer, 0, bytesRead);
            totalRead += bytesRead;

            if (totalBytes.HasValue)
            {
                DownloadProgress = (double)totalRead / totalBytes.Value;
            }
        }
    }

    private void StopResponse()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }
    }
    #endregion
}

#region Nested: UiChatMessage
public partial class UiChatMessage : ChatMessage, INotifyPropertyChanged
{
    public string ChatRole => Role.ToString();

    string _streamingText = string.Empty;
    public string StreamingText
    {
        get => _streamingText;
        set => SetProperty(ref _streamingText, value);
    }

    public UiChatMessage(ChatRole role, string text) : base(role, text)
    {
        Role = role;
        StreamingText = text;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(ref T backingField, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(backingField, value))
            return false;

        backingField = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
#endregion