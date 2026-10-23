using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Godot;
using Netrunner.AI;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// The local LLM opponent in the client, against a stub model server in the process: choosing it in the menu shows its
/// server in place of difficulty and style, Start game waits for a server, the connection test reaches it, a Gateway
/// game started from the menu asks the model (with the API key) while the header counts the model's thinking time, and
/// a save marks the LLM opponent.
/// </summary>
public partial class Main
{
	private async Task RunLlmOpponentSmokeAsync()
	{
		try
		{
			if (!HasAuthenticatedOfficialContent())
			{
				GD.Print("LLM_OPPONENT PASS content=false");
				QuitCleanly();
				return;
			}
			using var server = new StubModelServer(TimeSpan.FromSeconds(2.2));
			bool environment = !string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("NETRUNNER_LLM_BASE_URL"));
			_reducedMotion.ButtonPressed = true;
			GetWindow().Size = new Vector2I(1920, 1080);
			GetWindow().ContentScaleSize = new Vector2I(1920, 1080);
			ShowMenuView(MenuView.Title);
			await TableFrames(2);
			await ClickTableButton(FindDescendants<Button>(_menuTitleView!).Single(button => button.Text == "New game"));
			await ClickTableButton(FindDescendants<Button>(_menuNewGameView!).Single(button => button.Text == "Gateway decks"));
			await ClickTableButton(FindDescendants<Button>(_menuNewGameView!).Single(button => button.Text == "Play as Runner"));
			await ClickTableButton(FindDescendants<Button>(_menuOpponentKinds!).Single(button => button.Text == "Local LLM"));
			// Choosing the LLM shows its server in place of difficulty and style.
			RequireInteraction(_menuLlmSetup!.Visible && !_menuDifficulties!.Visible && !_menuPersonalities!.Visible,
				"Choosing the LLM opponent doesn't show its server in place of difficulty and style.");
			if (!environment)
			{
				// Without a server, Start game stays in the menu and says why.
				await ClickTableButton(FindDescendants<Button>(_menuNewGameActions!).Single(button => button.Name == "StartGame"));
				RequireInteraction(_currentSnapshot is null && _menuNewGameView!.Visible && _llmStatus!.Text.StartsWith("Set the LLM's server", StringComparison.Ordinal),
					"Start game without a server didn't stay in the menu.");
			}

			// The player types the server and tests it.
			foreach ((string name, string value) in new[] { ("LlmBaseUrl", server.BaseUrl), ("LlmModel", StubModelServer.Model), ("LlmApiKey", "smoke-key") })
			{
				LineEdit field = FindDescendants<LineEdit>(_menuLlmSetup).Single(edit => edit.Name == name);
				field.Text = value;
				field.EmitSignal(LineEdit.SignalName.TextChanged, value);
			}
			await ClickTableButton(FindDescendants<Button>(_menuLlmSetup).Single(button => button.Name == "LlmTest"));
			for (int frame = 0; frame < 600 && _llmStatus!.Text == "Connecting…"; frame++) await TableFrames(1);
			RequireInteraction(_llmStatus!.Text == $"Connected to {server.BaseUrl}.", $"The connection test said '{_llmStatus.Text}'.");
			RequireInteraction(_menuLlmDetail!.Text.Contains(StubModelServer.Model, StringComparison.Ordinal), "The menu doesn't name the model.");
			await ClickTableButton(FindDescendants<Button>(_menuYourDecks!).Single(button => button.Name == "Deck-gateway-stolen-goods"));
			await ClickTableButton(FindDescendants<Button>(_menuOpponentDecks!).Single(button => button.Name == "Deck-gateway-advanced-yomi"));
			await ClickTableButton(FindDescendants<Button>(_menuNewGameActions!).Single(button => button.Name == "StartGame"));
			RequireInteraction(_llmOpponentForNextGame, "Start game didn't take the LLM opponent.");

			// The human keeps a hand and plays on; the Corp's turn asks the model, and the header counts its thinking time.
			var human = new FullGameContractAgent(PlayerSide.Runner, _runtimeCardCatalog);
			bool counted = false;
			var waited = System.Diagnostics.Stopwatch.StartNew();
			while (server.Requests < 2 && waited.Elapsed < TimeSpan.FromSeconds(120))
			{
				Task<MatchSnapshot> next = _coordinator!.WaitForHumanDecisionAsync();
				while (!next.IsCompleted && waited.Elapsed < TimeSpan.FromSeconds(120))
				{
					await TableFrames(1);
					counted |= _status.Text.StartsWith("The local model is thinking · ", StringComparison.Ordinal);
					if (_currentSnapshot?.Feedback?.IsError == true) throw new InvalidOperationException(_currentSnapshot.Feedback.Message);
				}
				if (!next.IsCompleted || server.Requests >= 2) break;
				MatchSnapshot ready = await next;
				if (ready.IsTerminal) break;
				try
				{
					await _coordinator.SubmitHumanActionAsync(await human.ChooseActionAsync(ready.Observation));
				}
				catch (InvalidOperationException) when (_coordinator.CurrentSnapshot is not { AwaitingHumanDecision: true })
				{
					// The table moved the decision on by itself (a lone pass).
				}
			}
			MatchSnapshot settled = await _coordinator!.WaitForHumanDecisionAsync().WaitAsync(TimeSpan.FromSeconds(60));
			RequireInteraction(_llmOpponentActive && server.Requests > 0, $"The game never asked the model (agent {_llmOpponentActive}, "
				+ $"requests {server.Requests}, turn {_currentSnapshot?.View.Turn.TurnNumber}, awaiting {_currentSnapshot?.AwaitingHumanDecision}, "
				+ $"thinking {_currentSnapshot?.IsOpponentThinking}, status '{_status.Text}').");
			RequireInteraction(server.Authorization == "Bearer smoke-key", "The game didn't send the API key.");
			RequireInteraction(counted, "The header never counted the model's thinking time.");
			RequireInteraction(settled.AwaitingHumanDecision, "The human's next decision never came.");

			// A save keeps the bot and marks the LLM opponent.
			SoloSaveDocument save = await _coordinator!.CaptureSaveAsync("LLM opponent smoke");
			RequireInteraction(save.Payload.Opponent.LlmOpponent && save.Preview.LlmOpponent, "The save doesn't mark the LLM opponent.");
			GD.Print($"LLM_OPPONENT PASS requests={server.Requests} thinking_count=true save=true");
			QuitCleanly();
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			GD.Print("LLM_OPPONENT FAIL " + exception.Message);
			QuitCleanly(1);
		}
	}

	/// <summary>An OpenAI-compatible server on localhost that answers the bot's own option after a delay.</summary>
	private sealed partial class StubModelServer : IDisposable
	{
		public const string Model = "stub-model";
		private readonly HttpListener _listener = new();
		private readonly TimeSpan _delay;
		private int _requests;

		public StubModelServer(TimeSpan delay)
		{
			_delay = delay;
			var probe = new TcpListener(IPAddress.Loopback, 0);
			probe.Start();
			int port = ((IPEndPoint)probe.LocalEndpoint).Port;
			probe.Stop();
			BaseUrl = $"http://127.0.0.1:{port}/v1";
			_listener.Prefixes.Add($"http://127.0.0.1:{port}/");
			_listener.Start();
			_ = Task.Run(ServeAsync);
		}

		public string BaseUrl { get; }

		public int Requests => Volatile.Read(ref _requests);

		public string? Authorization { get; private set; }

		private async Task ServeAsync()
		{
			while (_listener.IsListening)
			{
				HttpListenerContext context;
				try
				{
					context = await _listener.GetContextAsync();
				}
				catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
				{
					return;
				}
				_ = Task.Run(() => AnswerAsync(context));
			}
		}

		private async Task AnswerAsync(HttpListenerContext context)
		{
			string reply;
			if (context.Request.HttpMethod == "GET" && context.Request.Url!.AbsolutePath.EndsWith("/models", StringComparison.Ordinal))
			{
				reply = $$"""{"data":[{"id":"{{Model}}"}]}""";
			}
			else
			{
				Authorization = context.Request.Headers["Authorization"];
				using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
				JsonNode? body = JsonNode.Parse(await reader.ReadToEndAsync());
				string prompt = (string?)body?["messages"]?.AsArray().Last()?["content"] ?? "";
				string option = Advised().Match(prompt) is { Success: true } match ? match.Groups[1].Value : "1";
				await Task.Delay(_delay);
				Interlocked.Increment(ref _requests);
				var answer = new JsonObject { ["option"] = int.Parse(option, System.Globalization.CultureInfo.InvariantCulture), ["reason"] = "the bot's choice" };
				reply = new JsonObject
				{
					["choices"] = new JsonArray(new JsonObject
					{
						["finish_reason"] = "stop",
						["message"] = new JsonObject { ["role"] = "assistant", ["content"] = answer.ToJsonString() },
					}),
				}.ToJsonString();
			}
			byte[] bytes = Encoding.UTF8.GetBytes(reply);
			context.Response.ContentType = "application/json";
			context.Response.ContentLength64 = bytes.Length;
			try
			{
				await context.Response.OutputStream.WriteAsync(bytes);
				context.Response.Close();
			}
			catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
			{
				// The game ended the request (its deadline, or quitting).
			}
		}

		public void Dispose()
		{
			_listener.Stop();
			_listener.Close();
		}

		[GeneratedRegex(@"The heuristic would choose option (\d+)")]
		private static partial Regex Advised();
	}
}
