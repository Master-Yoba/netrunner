using System.Diagnostics;
using Godot;
using Netrunner.AI;
using Netrunner.Core;
using Netrunner.LLM;
using Netrunner.Match;
using Netrunner.Match.Llm;
using Netrunner.Match.Review;

namespace Netrunner.GodotClient;

/// <summary>
/// The local LLM opponent (docs/LLM_OPPONENT.md): the player's Standard bot with a model on a local OpenAI-compatible
/// server making its strategic choices. The New game view chooses it and sets its server, which falls back to
/// NETRUNNER_LLM_BASE_URL, NETRUNNER_LLM_MODEL and NETRUNNER_LLM_API_KEY as for the CLI.
/// </summary>
public partial class Main
{
	private static OpenAiCompatibleChatClient? _applicationLlmClient;
	private static OpenAiCompatibleOptions? _applicationLlmOptions;
	private static RulesIndex? _applicationLlmRules;
	private static bool _applicationLlmRulesSearched;
	private static string? _applicationLlmRulesRoot;

	private string _llmBaseUrl = "";
	private string _llmModel = "";
	private string _llmApiKey = "";
	/// <summary>The menu's choice, remembered between games.</summary>
	private bool _llmOpponentChosen;
	/// <summary>The next game the menu starts plays the LLM opponent.</summary>
	private bool _llmOpponentForNextGame;
	/// <summary>The current game's opponent is the LLM opponent.</summary>
	private bool _llmOpponentActive;
	private readonly Stopwatch _opponentThinking = new();
	private Label? _llmStatus;
	private HBoxContainer? _menuOpponentKinds;
	private VBoxContainer? _menuLlmSetup;
	private Label? _menuLlmDetail;

	/// <summary>The server: the player's settings, each falling back to the environment; null without a URL and model.</summary>
	private OpenAiCompatibleOptions? LlmOptions()
	{
		string? Read(string name) => name switch
		{
			"NETRUNNER_LLM_BASE_URL" when _llmBaseUrl.Length > 0 => _llmBaseUrl,
			"NETRUNNER_LLM_MODEL" when _llmModel.Length > 0 => _llmModel,
			"NETRUNNER_LLM_API_KEY" when _llmApiKey.Length > 0 => _llmApiKey,
			_ => System.Environment.GetEnvironmentVariable(name),
		};
		try
		{
			return OpenAiCompatibleChatClient.FromEnvironment(Read) is { } options
				&& options.BaseUri.Scheme is "http" or "https" ? options with { Timeout = LlmPlayerOpponent.RequestTimeout } : null;
		}
		catch (UriFormatException)
		{
			return null;
		}
	}

	/// <summary>The application's client for the current server settings; null when none is set.</summary>
	private OpenAiCompatibleChatClient? LlmClient()
	{
		if (LlmOptions() is not { } options) return null;
		if (_applicationLlmClient is not null && _applicationLlmOptions == options) return _applicationLlmClient;
		_applicationLlmClient?.Dispose();
		_applicationLlmOptions = options;
		return _applicationLlmClient = new OpenAiCompatibleChatClient(options);
	}

	/// <summary>
	/// The comprehensive rules for the mechanics a decision shows, when a checkout has them (data/vendor/rules). A save
	/// loads on a worker thread, so the main thread finds the folder first (<see cref="BuildMenuOpponentKinds"/>).
	/// </summary>
	private static RulesIndex? LlmRules()
	{
		lock (typeof(Main))
		{
			if (_applicationLlmRulesSearched) return _applicationLlmRules;
			_applicationLlmRulesSearched = true;
			if (_applicationLlmRulesRoot is { } root && File.Exists(Path.Combine(root, "index.json"))) _applicationLlmRules = RulesIndex.Load(root);
			return _applicationLlmRules;
		}
	}

	/// <summary>A new game's opponent: the LLM opponent when the menu chose it and a server is set, else null.</summary>
	private LlmOpponentAgent? CreateLlmOpponent(FullMatchDefinition definition, PlayerSide humanSide)
	{
		_llmOpponentActive = false;
		if (!_llmOpponentForNextGame || LlmClient() is not { } client) return null;
		return Watch(LlmPlayerOpponent.Create(definition, humanSide, client, LlmRules()));
	}

	/// <summary>A loaded LLM opponent's save plays the LLM opponent again when a server is set, else its bot.</summary>
	private LlmOpponentAgent? RestoreLlmOpponent(OfflineAgent bot, FullMatchDefinition definition)
	{
		if (LlmClient() is not { } client)
		{
			Callable.From(() => ShowSoloNotice("This game was against the local LLM; set its server in Settings. The bot plays on.")).CallDeferred();
			return null;
		}
		return Watch(LlmPlayerOpponent.Restore(bot, definition, client, LlmRules()));
	}

	/// <summary>Tells the player when the model gave no answer and the bot's choice stood (its deadline, or the server).</summary>
	private LlmOpponentAgent Watch(LlmOpponentAgent agent)
	{
		_llmOpponentActive = true;
		agent.Decided = decision =>
		{
			if (decision.Outcome != "failed") return;
			string message = decision.Reason is { } reason && reason.StartsWith("No answer within", StringComparison.Ordinal)
				? "The model took too long; the bot made that choice."
				: "The model's server failed; the bot made that choice.";
			Callable.From(() => ShowSoloNotice(message)).CallDeferred();
		};
		return agent;
	}

	/// <summary>The header's and the dock's text while the opponent thinks, with its time once it takes a while.</summary>
	private (string Status, string Prompt) OpponentThinkingText()
	{
		double seconds = _opponentThinking.Elapsed.TotalSeconds;
		string who = _llmOpponentActive ? "The local model" : "Opponent";
		string time = seconds >= 2 ? $" · {seconds:0} s" : "";
		return ($"{who} is thinking{time}", $"{who} is thinking{time}. You can inspect cards and read history.");
	}

	/// <summary>Counts the opponent's thinking time and refreshes its text every frame (the snapshot doesn't change meanwhile).</summary>
	private void UpdateOpponentThinking()
	{
		bool thinking = _currentSnapshot is { IsOpponentThinking: true, IsTerminal: false };
		if (!thinking)
		{
			_opponentThinking.Reset();
			return;
		}
		if (!_opponentThinking.IsRunning) _opponentThinking.Start();
		if (!_useCardTable || _opponentThinking.Elapsed.TotalSeconds < 2) return;
		(string status, string prompt) = OpponentThinkingText();
		if (_status.Text != status) _status.Text = status;
		if (_tablePrompt.Text != prompt) _tablePrompt.Text = prompt;
	}

	private void LoadLlmPreferences(ConfigFile config)
	{
		_llmBaseUrl = (string)config.GetValue("llm", "base_url", "");
		_llmModel = (string)config.GetValue("llm", "model", "");
		_llmApiKey = (string)config.GetValue("llm", "api_key", "");
		_llmOpponentChosen = (bool)config.GetValue("llm", "opponent", false);
	}

	private void SaveLlmPreferences(ConfigFile config)
	{
		config.SetValue("llm", "base_url", _llmBaseUrl);
		config.SetValue("llm", "model", _llmModel);
		config.SetValue("llm", "api_key", _llmApiKey);
		config.SetValue("llm", "opponent", _llmOpponentChosen);
	}

	/// <summary>
	/// The menu's opponent row, the heuristic bot or the local LLM, and under it the LLM's server: URL, model, API key and a
	/// connection test, in the place of the difficulty and style rows the LLM doesn't use (the menu doesn't scroll).
	/// </summary>
	private void BuildMenuOpponentKinds(VBoxContainer view)
	{
		_applicationLlmRulesRoot ??= Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "..", "data", "vendor", "rules"));
		_menuOpponentKinds = MenuToggleRow(view, [("Heuristic bot", "bot"), ("Local LLM", "llm")], _llmOpponentChosen ? "llm" : "bot", value =>
		{
			_llmOpponentChosen = value == "llm";
			SavePreferences();
			RefreshMenuOpponentOptions();
		});
		_menuOpponentKinds.Name = "OpponentKind";
		_menuLlmSetup = new VBoxContainer { Name = "LlmSetup" };
		_menuLlmSetup.AddThemeConstantOverride("separation", 6);
		var fields = new HBoxContainer { Name = "Server" };
		fields.AddThemeConstantOverride("separation", 8);
		LineEdit Field(string name, string placeholder, string value, float width, bool secret, Action<string> set)
		{
			var field = new LineEdit
			{
				Name = name,
				PlaceholderText = placeholder,
				Text = value,
				Secret = secret,
				AccessibilityName = placeholder,
				CustomMinimumSize = new Vector2(width, 44),
			};
			field.AddThemeFontSizeOverride("font_size", 16);
			field.TextChanged += text =>
			{
				set(text.Trim());
				RefreshMenuLlmOpponent();
			};
			field.FocusExited += SavePreferences;
			fields.AddChild(field);
			return field;
		}
		Field("LlmBaseUrl", "Server URL, e.g. http://localhost:8080/v1", _llmBaseUrl, 380, false, value => _llmBaseUrl = value);
		Field("LlmModel", "Model name", _llmModel, 220, false, value => _llmModel = value);
		Field("LlmApiKey", "API key, if needed", _llmApiKey, 200, true, value => _llmApiKey = value);
		var test = new Button { Name = "LlmTest", Text = "Test connection", CustomMinimumSize = new Vector2(180, 44) };
		test.AddThemeFontSizeOverride("font_size", 16);
		test.Pressed += async () =>
		{
			SavePreferences();
			if (LlmOptions() is not { } options)
			{
				_llmStatus!.Text = "Set a server URL and a model first.";
				return;
			}
			test.Disabled = true;
			_llmStatus!.Text = "Connecting…";
			using var client = new OpenAiCompatibleChatClient(options with { Timeout = TimeSpan.FromSeconds(10) });
			(_, string message) = await client.CheckAsync();
			if (!IsInstanceValid(test)) return;
			test.Disabled = false;
			_llmStatus.Text = message;
		};
		fields.AddChild(test);
		_menuLlmSetup.AddChild(fields);
		_menuLlmDetail = MakeWrappedLabel("", 14, Muted);
		_menuLlmDetail.Name = "LlmDetail";
		_menuLlmSetup.AddChild(_menuLlmDetail);
		_llmStatus = MakeWrappedLabel("", 14, Muted);
		_llmStatus.Name = "LlmStatus";
		_menuLlmSetup.AddChild(_llmStatus);
		view.AddChild(_menuLlmSetup);
	}

	/// <summary>Whether the menu's next game plays the LLM opponent: chosen, not the tutorial, and a server set.</summary>
	private bool MenuPlaysLlmOpponent => MenuChoosesLlmOpponent && LlmOptions() is not null;

	private bool MenuChoosesLlmOpponent => _llmOpponentChosen && _menuMode != TutorialMode;

	/// <summary>Shows the LLM's server under the opponent row when it is chosen; it plays the Standard bot, so difficulty and style go.</summary>
	private void RefreshMenuLlmOpponent()
	{
		if (_menuOpponentKinds is null || _menuLlmSetup is null || _menuLlmDetail is null) return;
		_menuOpponentKinds.Visible = _menuMode != TutorialMode;
		_menuLlmSetup.Visible = MenuChoosesLlmOpponent;
		_menuLlmDetail.Text = LlmOptions() is { } options
			? $"The Standard bot with {options.Model} making its strategic choices: installs, runs, advances and scoring. A decision takes a few seconds."
			: "Set the server of a local model (llama.cpp, Ollama, LM Studio): its URL and model, and an API key if it needs one.";
		if (_menuDifficulties is not null) _menuDifficulties.Visible = !MenuChoosesLlmOpponent;
		if (_menuPersonalities is not null && MenuChoosesLlmOpponent) _menuPersonalities.Visible = false;
	}

	/// <summary>Start game with the LLM chosen but no server set stays in the menu and says why.</summary>
	private bool LlmOpponentBlocksStart()
	{
		if (!MenuChoosesLlmOpponent || LlmOptions() is not null) return false;
		if (_llmStatus is not null) _llmStatus.Text = "Set the LLM's server URL and model to start, or choose the heuristic bot.";
		return true;
	}
}
