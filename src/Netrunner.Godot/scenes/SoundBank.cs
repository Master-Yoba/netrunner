using Godot;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// The client's sound effects (U5), synthesized at startup from tones and
/// noise, so the game ships no audio assets and nothing needs a license
/// notice. A few voices play at once; a cue repeated within 40 ms is dropped,
/// so a burst of credit beats doesn't stack.
/// </summary>
public partial class SoundBank : Node
{
	private const int Rate = 22050;
	private const int Voices = 6;
	private readonly Dictionary<SoundCue, AudioStreamWav> _streams = [];
	private readonly Dictionary<SoundCue, ulong> _lastPlayed = [];
	private readonly List<AudioStreamPlayer> _players = [];
	private int _nextVoice;

	/// <summary>Effects volume from 0 to 1.</summary>
	public float Volume { get; set; } = 0.7f;

	public bool Muted { get; set; }

	public int CueCount => _streams.Count;

	/// <summary>Cues asked for, heard or not; smokes check that beats reach the sound bank.</summary>
	public int Requested { get; private set; }

	public override void _Ready()
	{
		for (int voice = 0; voice < Voices; voice++)
		{
			var player = new AudioStreamPlayer { Name = $"Voice{voice}" };
			AddChild(player);
			_players.Add(player);
		}
		foreach (SoundCue cue in Enum.GetValues<SoundCue>())
			if (cue != SoundCue.None)
				_streams[cue] = Stream(Synthesize(cue));
	}

	public void Play(SoundCue cue)
	{
		if (cue != SoundCue.None) Requested++;
		if (cue == SoundCue.None || Muted || Volume <= 0 || !_streams.TryGetValue(cue, out AudioStreamWav? stream)) return;
		ulong now = Time.GetTicksMsec();
		if (_lastPlayed.TryGetValue(cue, out ulong last) && now - last < 40) return;
		_lastPlayed[cue] = now;
		AudioStreamPlayer player = _players[_nextVoice];
		_nextVoice = (_nextVoice + 1) % _players.Count;
		player.Stream = stream;
		player.VolumeDb = Mathf.LinearToDb(Volume);
		player.Play();
	}

	private static AudioStreamWav Stream(float[] samples)
	{
		byte[] data = new byte[samples.Length * 2];
		for (int index = 0; index < samples.Length; index++)
		{
			short value = (short)Math.Clamp(samples[index] * 32767f, -32768f, 32767f);
			data[index * 2] = (byte)(value & 0xff);
			data[index * 2 + 1] = (byte)((value >> 8) & 0xff);
		}
		return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = data };
	}

	/// <summary>One cue's samples; every cue is deterministic.</summary>
	internal static float[] Synthesize(SoundCue cue) => cue switch
	{
		SoundCue.Click => Tone(0.035, 1400, 0.35, decay: 90),
		SoundCue.Credit => Mix(Tone(0.14, 2093, 0.22, decay: 28, harmonics: 3), Delay(Tone(0.12, 3136, 0.16, decay: 30, harmonics: 3), 0.045)),
		SoundCue.CardSlide => Noise(0.12, 0.22, lowPass: 0.18, attack: 0.02, decay: 30, seed: 1),
		SoundCue.CardPlace => Mix(Noise(0.08, 0.18, lowPass: 0.12, attack: 0.005, decay: 45, seed: 2), Tone(0.1, 120, 0.4, decay: 40)),
		SoundCue.CardFlip => Mix(Noise(0.06, 0.2, lowPass: 0.35, attack: 0.003, decay: 60, seed: 3), Delay(Noise(0.06, 0.16, lowPass: 0.35, attack: 0.003, decay: 60, seed: 4), 0.05)),
		SoundCue.Rez => Sweep(0.38, 140, 330, 0.3, harmonics: 4, attack: 0.04, decay: 7),
		SoundCue.Advance => Sweep(0.09, 600, 900, 0.25, harmonics: 2, attack: 0.005, decay: 35),
		SoundCue.ScoreGain => Arpeggio([523.25, 659.25, 783.99, 1046.5], 0.09, 0.26),
		SoundCue.ScoreLoss => Arpeggio([493.88, 392.0, 311.13, 246.94], 0.1, 0.24),
		SoundCue.RunStart => Mix(Noise(0.3, 0.18, lowPass: 0.08, attack: 0.15, decay: 10, seed: 5), Sweep(0.3, 200, 520, 0.12, harmonics: 1, attack: 0.1, decay: 8)),
		SoundCue.Encounter => Mix(Tone(0.12, 220, 0.3, decay: 20, harmonics: 3), Delay(Tone(0.12, 220, 0.3, decay: 20, harmonics: 3), 0.16)),
		SoundCue.Break => Mix(Noise(0.16, 0.3, lowPass: 0.7, attack: 0.002, decay: 32, seed: 6), Tone(0.14, 1760, 0.12, decay: 30, harmonics: 2)),
		SoundCue.SubroutineFire => Square(0.22, 92, 0.2, decay: 12),
		SoundCue.RunSuccess => Arpeggio([659.25, 987.77], 0.1, 0.24),
		SoundCue.RunFail => Arpeggio([392.0, 293.66], 0.12, 0.22),
		SoundCue.Damage => Mix(Noise(0.28, 0.4, lowPass: 0.25, attack: 0.003, decay: 14, seed: 7), Sweep(0.3, 110, 45, 0.5, harmonics: 1, attack: 0.003, decay: 10)),
		SoundCue.Tag => Mix(Tone(0.07, 1318.5, 0.2, decay: 40), Delay(Tone(0.07, 1318.5, 0.2, decay: 40), 0.1)),
		SoundCue.YourTurn => Mix(Tone(0.45, 880, 0.14, decay: 8, harmonics: 2), Delay(Tone(0.4, 1318.5, 0.1, decay: 9, harmonics: 2), 0.08)),
		SoundCue.Win => Arpeggio([523.25, 659.25, 783.99, 1046.5, 1318.5, 1568.0], 0.11, 0.26),
		SoundCue.Loss => Arpeggio([392.0, 369.99, 349.23, 261.63], 0.18, 0.24),
		_ => [],
	};

	private static float[] Tone(double seconds, double hz, double gain, double decay, int harmonics = 1) =>
		Sweep(seconds, hz, hz, gain, harmonics, attack: 0.003, decay);

	private static float[] Sweep(double seconds, double fromHz, double toHz, double gain, int harmonics, double attack, double decay)
	{
		int count = (int)(seconds * Rate);
		float[] samples = new float[count];
		double phase = 0;
		for (int index = 0; index < count; index++)
		{
			double t = index / (double)Rate;
			phase += 2 * Math.PI * (fromHz + (toHz - fromHz) * index / count) / Rate;
			double value = 0;
			for (int harmonic = 1; harmonic <= harmonics; harmonic++) value += Math.Sin(phase * harmonic) / (harmonic * harmonic);
			samples[index] = (float)(gain * value * Envelope(t, seconds, attack, decay));
		}
		return samples;
	}

	private static float[] Square(double seconds, double hz, double gain, double decay)
	{
		int count = (int)(seconds * Rate);
		float[] samples = new float[count];
		for (int index = 0; index < count; index++)
		{
			double t = index / (double)Rate;
			double value = Math.Sin(2 * Math.PI * hz * t) >= 0 ? 1 : -1;
			samples[index] = (float)(gain * value * Envelope(t, seconds, 0.004, decay));
		}
		return samples;
	}

	private static float[] Noise(double seconds, double gain, double lowPass, double attack, double decay, int seed)
	{
		int count = (int)(seconds * Rate);
		float[] samples = new float[count];
		uint state = 0x9e3779b9u * (uint)seed + 1;
		double filtered = 0;
		for (int index = 0; index < count; index++)
		{
			state ^= state << 13;
			state ^= state >> 17;
			state ^= state << 5;
			double white = state / (double)uint.MaxValue * 2 - 1;
			filtered += lowPass * (white - filtered);
			samples[index] = (float)(gain * filtered / Math.Sqrt(lowPass) * Envelope(index / (double)Rate, seconds, attack, decay));
		}
		return samples;
	}

	private static float[] Arpeggio(double[] notes, double step, double gain)
	{
		float[] result = [];
		for (int index = 0; index < notes.Length; index++)
		{
			bool last = index == notes.Length - 1;
			result = Mix(result, Delay(Tone(last ? step * 4 : step * 2, notes[index], gain, decay: last ? 5 : 12, harmonics: 3), step * index));
		}
		return result;
	}

	/// <summary>An attack ramp, an exponential decay, and a short fade so every cue ends on silence.</summary>
	private static double Envelope(double t, double seconds, double attack, double decay)
	{
		double rise = attack <= 0 ? 1 : Math.Min(1, t / attack);
		double fade = Math.Min(1, (seconds - t) / 0.01);
		return rise * Math.Exp(-decay * Math.Max(0, t - attack)) * Math.Max(0, fade);
	}

	private static float[] Delay(float[] samples, double seconds)
	{
		int offset = (int)(seconds * Rate);
		float[] result = new float[samples.Length + offset];
		Array.Copy(samples, 0, result, offset, samples.Length);
		return result;
	}

	private static float[] Mix(float[] first, float[] second)
	{
		float[] result = new float[Math.Max(first.Length, second.Length)];
		for (int index = 0; index < result.Length; index++)
			result[index] = (index < first.Length ? first[index] : 0) + (index < second.Length ? second[index] : 0);
		return result;
	}
}
