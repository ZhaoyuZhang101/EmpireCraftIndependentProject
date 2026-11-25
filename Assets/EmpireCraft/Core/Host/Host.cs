using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json.Linq;

namespace EmpireCraft.Core
{
    // The host layer: the handful of services the ported rules expect from
    // whatever runs them (frame clock, log, text table, data folder, profiler).
    // The Unity client and the dotnet validation programs each drive it; nothing
    // here references an engine. Ported files reach FrameClock and EcMath through
    // `using Time = ...` / `using Mathf = ...` aliases written by Tools/PortTool,
    // which is why those two keep engine-style member names.

    // Advanced once per rendered frame (or once per step by a headless host).
    public static class FrameClock
    {
        public static int frameCount { get; private set; }
        public static float realtimeSinceStartup { get; private set; }
        public static float unscaledDeltaTime { get; private set; }
        // Game-speed multiplier the player picked; background budgets may widen with it.
        public static float SpeedFactor = 1f;

        public static void Advance(float deltaSeconds)
        {
            if (float.IsNaN(deltaSeconds) || deltaSeconds < 0f) deltaSeconds = 0f;
            frameCount++;
            unscaledDeltaTime = deltaSeconds;
            realtimeSinceStartup += deltaSeconds;
        }

        public static void Reset()
        {
            frameCount = 0;
            realtimeSinceStartup = unscaledDeltaTime = 0f;
            SpeedFactor = 1f;
        }
    }

    public static class EcMath
    {
        public const float PI = (float)Math.PI;
        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        public static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Abs(float value) => Math.Abs(value);
        public static int Abs(int value) => Math.Abs(value);
        public static float Sign(float value) => value < 0f ? -1f : 1f;
        public static float Sqrt(float value) => (float)Math.Sqrt(value);
        public static float Pow(float value, float power) => (float)Math.Pow(value, power);
        public static float Exp(float value) => (float)Math.Exp(value);
        public static float Log(float value) => (float)Math.Log(value);
        public static float Floor(float value) => (float)Math.Floor(value);
        public static float Ceil(float value) => (float)Math.Ceiling(value);
        public static float Round(float value) => (float)Math.Round(value);
        public static int FloorToInt(float value) => (int)Math.Floor(value);
        public static int CeilToInt(float value) => (int)Math.Ceiling(value);
        public static int RoundToInt(float value) => (int)Math.Round(value);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;
        public static float InverseLerp(float a, float b, float value) => a == b ? 0f : Clamp01((value - a) / (b - a));
        public static float MoveTowards(float current, float target, float maxDelta) =>
            Math.Abs(target - current) <= maxDelta ? target : current + Sign(target - current) * maxDelta;
        public static bool Approximately(float a, float b) => Math.Abs(a - b) < 1e-6f * Math.Max(1f, Math.Max(Math.Abs(a), Math.Abs(b)));
    }

    // Colour as four floats in 0..1, the shape the ported rules compute with.
    public struct EcColor : IEquatable<EcColor>
    {
        public float r, g, b, a;
        public EcColor(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static EcColor white => new EcColor(1f, 1f, 1f);
        public static EcColor black => new EcColor(0f, 0f, 0f);
        public static EcColor clear => new EcColor(0f, 0f, 0f, 0f);
        public uint Rgb => ((uint)Channel(r) << 16) | ((uint)Channel(g) << 8) | (uint)Channel(b);
        static int Channel(float value) => (int)Math.Round(EcMath.Clamp01(value) * 255f);
        public static EcColor FromRgb(uint rgb) => new EcColor(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
        public static EcColor Lerp(EcColor from, EcColor to, float t)
        {
            t = EcMath.Clamp01(t);
            return new EcColor(from.r + (to.r - from.r) * t, from.g + (to.g - from.g) * t, from.b + (to.b - from.b) * t, from.a + (to.a - from.a) * t);
        }

        // Hue, saturation and value in 0..1.
        public static EcColor HSVToRGB(float hue, float saturation, float value)
        {
            hue = (hue % 1f + 1f) % 1f * 6f;
            int sector = (int)hue;
            float fraction = hue - sector, low = value * (1f - saturation);
            float falling = value * (1f - saturation * fraction), rising = value * (1f - saturation * (1f - fraction));
            switch (sector % 6)
            {
                case 0: return new EcColor(value, rising, low);
                case 1: return new EcColor(falling, value, low);
                case 2: return new EcColor(low, value, rising);
                case 3: return new EcColor(low, falling, value);
                case 4: return new EcColor(rising, low, value);
                default: return new EcColor(value, low, falling);
            }
        }

        public bool Equals(EcColor other) => r == other.r && g == other.g && b == other.b && a == other.a;
        public override bool Equals(object other) => other is EcColor color && Equals(color);
        public override int GetHashCode() => (r, g, b, a).GetHashCode();
        public static bool operator ==(EcColor left, EcColor right) => left.Equals(right);
        public static bool operator !=(EcColor left, EcColor right) => !left.Equals(right);
    }

    public static class EcColorUtility
    {
        // "#RGB", "#RRGGBB" or "#RRGGBBAA"; the leading '#' is optional.
        public static bool TryParseHtmlString(string text, out EcColor color)
        {
            color = default;
            if (string.IsNullOrEmpty(text)) return false;
            string hex = text.Trim().TrimStart('#');
            if (hex.Length == 3) hex = "" + hex[0] + hex[0] + hex[1] + hex[1] + hex[2] + hex[2];
            if (hex.Length != 6 && hex.Length != 8) return false;
            if (!uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint value)) return false;
            if (hex.Length == 6) value = (value << 8) | 255u;
            color = new EcColor(((value >> 24) & 255) / 255f, ((value >> 16) & 255) / 255f, ((value >> 8) & 255) / 255f, (value & 255) / 255f);
            return true;
        }

        public static string ToHtmlStringRGB(EcColor color) => color.Rgb.ToString("X6");
    }

    public enum LogLevel : byte { Info, Warning, Error }

    public static class LogService
    {
        // Set by the host (Debug.Log in Unity, the console in validation). Null drops the line.
        public static Action<LogLevel, string> Sink;
        public static int Warnings, Errors;
        public static void LogInfo(string message) => Sink?.Invoke(LogLevel.Info, message);
        public static void LogWarning(string message) { Warnings++; Sink?.Invoke(LogLevel.Warning, message); }
        public static void LogError(string message) { Errors++; Sink?.Invoke(LogLevel.Error, message); }
    }

    // Text table. A missing key reads back as the key itself, so a rule never
    // fails on text that has not been brought over yet.
    public static class LM
    {
        static readonly Dictionary<string, string> Table = new Dictionary<string, string>(StringComparer.Ordinal);
        // Language code of the loaded table: "ch", "en" or "cz", as the mod's Locales files are named.
        public static string Language = "ch";
        public static int Count => Table.Count;
        public static bool Has(string key) => key != null && Table.ContainsKey(key);
        public static string Get(string key) => key != null && Table.TryGetValue(key, out string text) ? text : key;
        public static void Set(string key, string text) { if (key != null) Table[key] = text ?? ""; }
        public static void Clear() => Table.Clear();

        // A flat json object of key -> text, the layout of the mod's Locales files.
        public static int LoadJson(string json)
        {
            int added = 0;
            foreach (var pair in JObject.Parse(json))
                if (pair.Value != null && pair.Value.Type == JTokenType.String) { Table[pair.Key] = (string)pair.Value; added++; }
            return added;
        }
        public static int LoadFile(string path) => LoadJson(File.ReadAllText(path));
    }

    // Where the rule data (Assets/EmpireCraft/Data, or its shipped copy) is read from.
    public static class GameData
    {
        public static string Folder = "";
        public static string PathOf(params string[] parts)
        {
            string path = Folder ?? "";
            foreach (string part in parts) path = Path.Combine(path, part);
            return path;
        }
    }

    // Per-key time accounting for the simulation steps. Off unless a host turns it on.
    public static class FrameProfiler
    {
        public static bool Enabled;
        sealed class Entry { public long Ticks; public int Calls; }
        static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

        public static Scope Measure(string key) => Enabled && key != null ? new Scope(key, Stopwatch.GetTimestamp()) : default;

        public readonly struct Scope : IDisposable
        {
            readonly string _key; readonly long _started;
            internal Scope(string key, long started) { _key = key; _started = started; }
            public void Dispose()
            {
                if (_key == null) return;
                if (!Entries.TryGetValue(_key, out Entry entry)) Entries[_key] = entry = new Entry();
                entry.Ticks += Stopwatch.GetTimestamp() - _started; entry.Calls++;
            }
        }

        public static double Milliseconds(string key) => Entries.TryGetValue(key, out Entry entry) ? entry.Ticks * 1000d / Stopwatch.Frequency : 0d;
        public static int Calls(string key) => Entries.TryGetValue(key, out Entry entry) ? entry.Calls : 0;
        public static IEnumerable<string> Keys => Entries.Keys;
        public static void Reset() => Entries.Clear();
    }
}
