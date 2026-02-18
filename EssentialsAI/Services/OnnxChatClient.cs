//using Microsoft.Extensions.AI;
//using Microsoft.ML.OnnxRuntimeGenAI;
//using System.Runtime.CompilerServices;
//using System.Text;

//namespace EssentialsAI.Services;

//public class OnnxChatClient : IChatClient
//{
//    private readonly Model _model;
//    private readonly Tokenizer _tokenizer;
//    private readonly ChatClientMetadata _metadata;

//    public OnnxChatClient(string modelPath)
//    {
//        // GenAI loads from a FOLDER containing .onnx, config, binary files
//        try
//        {
//            _model = new Model(modelPath);
//            _tokenizer = new Tokenizer(_model);
//            _metadata = new ChatClientMetadata("OnnxRuntimeGenAI", new Uri("https://github.com/microsoft/onnxruntime-genai"), "GenAI Model");

//        }
//        catch (Exception ex)
//        {
//            throw new InvalidOperationException($"Failed to load GenAI model from '{modelPath}'. Ensure the folder contains model.onnx (or model.onnx.data), genai_config.json, and tokenizer.json. Error: {ex.Message}", ex);
//        }
//    }

//    public ChatClientMetadata Metadata => _metadata;

//    public void Dispose()
//    {
//        _tokenizer?.Dispose();
//        _model?.Dispose();
//    }

//    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
//    {
//        var sb = new StringBuilder();
//        await foreach (var update in GetStreamingResponseAsync(chatMessages, options, cancellationToken))
//        {
//            if (!string.IsNullOrEmpty(update.Text))
//            {
//                sb.Append(update.Text);
//            }
//        }

//        return new ChatResponse(new[] { new ChatMessage(ChatRole.Assistant, sb.ToString()) });
//    }

//    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
//    {
//        using var generatorParams = new GeneratorParams(_model);

//        var prompt = BuildPrompt(chatMessages);

//        using var sequences = _tokenizer.Encode(prompt);
//        // generatorParams.SetInputSequences(sequences); // Removed in 0.6.0+

//        // Set options
//        if (options?.Temperature.HasValue == true)
//            generatorParams.SetSearchOption("temperature", options.Temperature.Value);

//        generatorParams.SetSearchOption("max_length", 2048);

//        using var generator = new Generator(_model, generatorParams);
//        generator.AppendTokenSequences(sequences);

//        using var tokenizerStream = _tokenizer.CreateStream();

//        while (!generator.IsDone())
//        {
//            cancellationToken.ThrowIfCancellationRequested();

//            await Task.Run(() =>
//            {
//                generator.GenerateNextToken();
//            }, cancellationToken);

//            // Get the last generated token
//            // 0 is the batch index
//            var seq = generator.GetSequence(0);
//            var newTokenId = seq[seq.Length - 1];

//            // Decode incrementally
//            var decodedChunk = tokenizerStream.Decode(newTokenId);

//            if (!string.IsNullOrEmpty(decodedChunk))
//            {
//                yield return new ChatResponseUpdate
//                {
//                    Role = ChatRole.Assistant,
//                    Contents = new List<AIContent> { new TextContent(decodedChunk) }
//                };
//            }
//        }
//    }

//    private string BuildPrompt(IEnumerable<ChatMessage> messages)
//    {
//        var sb = new StringBuilder();
//        // This is a naive implementation. 
//        // Real implementation should check `_tokenizer` config or use a standard template.
//        // Assuming Phi-3/Llama-3 style for now which often handles "User: \n Assistant: \n" or ChatML

//        foreach (var msg in messages)
//        {
//            if (msg.Role == ChatRole.System)
//            {
//                sb.AppendLine($"<|system|>\n{msg.Text}<|end|>");
//            }
//            else if (msg.Role == ChatRole.User)
//            {
//                sb.AppendLine($"<|user|>\n{msg.Text}<|end|>");
//            }
//            else if (msg.Role == ChatRole.Assistant)
//            {
//                sb.AppendLine($"<|assistant|>\n{msg.Text}<|end|>");
//            }
//            else if(msg.Role == ChatRole.Tool)
//            {
//                sb.AppendLine($"<|tool|>\n{msg.Text}<|end|>");
//			}
//        }
//        sb.Append("<|assistant|>\n"); // Trigger generation
//        return sb.ToString();
//    }

//    public object? GetService(Type serviceType, object? serviceKey = null)
//    {
//        return null; // or if (serviceType == typeof(OnnxChatClient)) return this;
//    }
//}

using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntimeGenAI;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace EssentialsAI.Services;

public interface IOnnxChatClient : IChatClient
{
	public Task<bool> InitializeModel(string modelPath);
}

public class OnnxChatClient : IChatClient
{
	private Model? _model;
	private Tokenizer? _tokenizer;
	private ChatClientMetadata? _metadata;

	public ChatClientMetadata Metadata => _metadata;

	public OnnxChatClient()
	{

	}

	public OnnxChatClient(string modelPath)
	{
		_ = InitializeModel(modelPath);
	}

	

	public bool InitializeModel(string modelPath)
	{
		// GenAI loads from a FOLDER containing .onnx, config, binary files
		try
		{
			_model = new Microsoft.ML.OnnxRuntimeGenAI.Model(modelPath);
			_tokenizer = new Tokenizer(_model);
			_metadata = new ChatClientMetadata("OnnxRuntimeGenAI", new Uri("https://github.com/microsoft/onnxruntime-genai"), "GenAI Model");

			return true;
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Failed to load GenAI model from '{modelPath}'. Ensure the folder contains model.onnx (or model.onnx.data), genai_config.json, and tokenizer.json. Error: {ex.Message}", ex);
		}
	}

	public void Dispose()
	{
		_tokenizer?.Dispose();
		_model?.Dispose();
	}

	public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
	{
		var sb = new StringBuilder();

		// Use ConfigureAwait(false) to avoid locking the UI thread while buffering
		await foreach (var update in GetStreamingResponseAsync(chatMessages, options, cancellationToken).ConfigureAwait(false))
		{
			if (!string.IsNullOrEmpty(update.Text))
			{
				sb.Append(update.Text);
			}
		}

		var fullText = sb.ToString().Trim();

		// --- OPTIMIZED TOOL PARSING ---
		// Basic check to avoid JSON parsing overhead on normal text
		if (fullText.Length > 2 && fullText.StartsWith("{") && fullText.Contains("\"name\""))
		{
			try
			{
				// Parse on background thread if result is large
				using var document = JsonDocument.Parse(fullText);
				var root = document.RootElement;

				if (root.TryGetProperty("name", out var nameElement))
				{
					string functionName = nameElement.GetString() ?? string.Empty;

					var arguments = root.TryGetProperty("arguments", out var argsElement)
						? JsonSerializer.Deserialize<IDictionary<string, object?>>(argsElement.GetRawText())
						: new Dictionary<string, object?>();

					var functionCall = new FunctionCallContent(
						callId: Guid.NewGuid().ToString(),
						name: functionName,
						arguments: arguments
					);

					return new ChatResponse(new[] { new ChatMessage(ChatRole.Assistant, new[] { functionCall }) });
				}
			}
			catch (JsonException) { /* Fallback to text */ }
		}

		return new ChatResponse(new[] { new ChatMessage(ChatRole.Assistant, fullText) });
	}

	public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		// -----------------------------------------------------------------------
		// OPTIMIZATION 1: Offload the "Prefill" (Heavy CPU work) to a background thread.
		// This prevents the UI freeze ("Close and Wait" dialog) when starting a reply.
		// -----------------------------------------------------------------------

		// We prepare the generator resources in a Task.Run block
		var (generator, tokenizerStream) = await Task.Run(() =>
		{
			var generatorParams = new GeneratorParams(_model);

			// Build prompt
			var prompt = BuildPrompt(chatMessages, options);

			// Heavy CPU operation: Tokenization
			var sequences = _tokenizer.Encode(prompt);

			// Set options
			if (options?.Temperature.HasValue == true)
				generatorParams.SetSearchOption("temperature", options.Temperature.Value);
			else
				generatorParams.SetSearchOption("temperature", 0.0d);

			generatorParams.SetSearchOption("max_length", 2048);
			// generatorParams.SetSearchOption("do_sample", false); // Optional: faster, less creative

			var gen = new Generator(_model, generatorParams);

			// Heavy CPU operation: Ingesting history (Prefill)
			gen.AppendTokenSequences(sequences);

			// Cleanup the sequences explicitly here as they aren't needed after Append
			sequences.Dispose();

			return (gen, _tokenizer.CreateStream());
		}, cancellationToken).ConfigureAwait(false);

		// Ensure we dispose of these resources when the loop finishes
		using (generator)
		using (tokenizerStream)
		{
			while (!generator.IsDone())
			{
				cancellationToken.ThrowIfCancellationRequested();

				// -----------------------------------------------------------------------
				// OPTIMIZATION 2: Generate token on background thread
				// -----------------------------------------------------------------------
				await Task.Run(() =>
				{
					generator.GenerateNextToken();
				}, cancellationToken).ConfigureAwait(false);

				// Decoding is fast, can be done here
				var seq = generator.GetSequence(0);
				var newTokenId = seq[seq.Length - 1];
				var decodedChunk = tokenizerStream.Decode(newTokenId);

				if (!string.IsNullOrEmpty(decodedChunk))
				{
					yield return new ChatResponseUpdate
					{
						Role = ChatRole.Assistant,
						Contents = new List<AIContent> { new TextContent(decodedChunk) }
					};
				}
			}
		}
	}

	private string BuildPrompt(IEnumerable<ChatMessage> messages, ChatOptions? options = null)
	{
		var sb = new StringBuilder();

		// 1. Inject Tools (Critical for Function Calling)
		string toolSystemInstruction = "";
		if (options?.Tools is { Count: > 0 } tools)
		{
			var toolDefs = tools.Select(t => new
			{
				name = t.Name,
				description = t.Description,
				parameters = (t as AIFunction)?.JsonSchema
			});

			var toolsJson = JsonSerializer.Serialize(toolDefs, new JsonSerializerOptions { WriteIndented = false }); // Compact JSON is faster to tokenize
			toolSystemInstruction = $"\n[AVAILABLE TOOLS]\n{toolsJson}\n[INSTRUCTION]\nOutput a JSON object with 'name' and 'arguments' to call a tool.";
		}

		foreach (var msg in messages)
		{
			if (msg.Role == ChatRole.System)
			{
				sb.Append($"<|system|>\n{msg.Text}{toolSystemInstruction}<|end|>\n");
			}
			else if (msg.Role == ChatRole.User)
			{
				sb.Append($"<|user|>\n{msg.Text}<|end|>\n");
			}
			else if (msg.Role == ChatRole.Assistant)
			{
				// Handle previous tool calls in history
				var funcCall = msg.Contents.OfType<FunctionCallContent>().FirstOrDefault();
				if (funcCall != null)
				{
					var jsonCall = JsonSerializer.Serialize(new { name = funcCall.Name, arguments = funcCall.Arguments });
					sb.Append($"<|assistant|>\n >!{jsonCall}<|end|>\n");
				}
				else
				{
					sb.Append($"<|assistant|>\n >!{msg.Text}<|end|>\n");
				}
			}
			//else if (msg.Role == ChatRole.Tool)
			//{
			//	var result = msg.Contents.OfType<FunctionResultContent>().FirstOrDefault();
			//	if (result != null)
			//	{
			//		var resultJson = JsonSerializer.Serialize(result.Result);
			//		sb.Append($"<|tool|>\n{resultJson}<|end|>\n");
			//	}
			//}
		}
		sb.Append("<|assistant|>\n");
		return sb.ToString();
	}

	public object? GetService(Type serviceType, object? serviceKey = null)
	{
		return serviceType == typeof(IChatClient) ? this : null;
	}
}