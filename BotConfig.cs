using System;
using System.IO;
using System.Text.Json;

namespace StressBotBenchmark
{
    public class BotConfig
    {
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 7172;
        public bool UseApiLogin { get; set; } = false;
        public string ApiLoginUrl { get; set; } = "http://127.0.0.1:5185/auth/login";
        
        public int BotCount { get; set; } = 1000;
        public string Prefix { get; set; } = "stressbot";
        public string Password { get; set; } = "test123";
        public int AccountWidth { get; set; } = 3;
        
        public double LoginDelayMs { get; set; } = 15;
        public int BurstSize { get; set; } = 20;
        public double BurstPauseMs { get; set; } = 300;
        
        public double WalkIntervalMs { get; set; } = 500;
        public double ChatIntervalMs { get; set; } = 5000;
        public double SpellIntervalMs { get; set; } = 5000;
        public double AttackScanIntervalMs { get; set; } = 800;
        
        public string SpellText { get; set; } = "exevo gran mas flam";
        public bool EnableRandomWalk { get; set; } = true;
        public bool EnableChat { get; set; } = false;
        public bool EnableSpell { get; set; } = true;
        public bool EnableAttack { get; set; } = true;
        public bool EnableChaseMode { get; set; } = true;
        
        public byte FightMode { get; set; } = 1; // 1 = Offensive
        public bool SafeFight { get; set; } = false;
        
        public double DashboardIntervalMs { get; set; } = 1000;
        public bool LoginOnly { get; set; } = false;
        public bool Reconnect { get; set; } = true;
        
        public int QueueSize { get; set; } = 32;
        public int MaxSendLagMsToDrop { get; set; } = 1200;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public const string DefaultFileName = "config.json";

        /// <summary>
        /// Loads the configuration. An explicitly requested path must exist; without one,
        /// config.json is looked up in the working directory and next to the executable,
        /// and a default file is generated in the working directory if neither exists.
        /// Throws on a missing explicit file or an unreadable/invalid file.
        /// </summary>
        public static BotConfig Load(string? explicitPath = null)
        {
            string path;
            if (explicitPath != null)
            {
                path = explicitPath;
                if (!File.Exists(path))
                    throw new FileNotFoundException($"Config file '{Path.GetFullPath(path)}' does not exist.", path);
            }
            else
            {
                string? found = FindDefaultConfig();
                if (found == null)
                {
                    var defaultConfig = new BotConfig();
                    defaultConfig.Save(DefaultFileName);
                    Console.WriteLine($"[Config] '{DefaultFileName}' not found, generated one with default values.");
                    return defaultConfig;
                }
                path = found;
            }

            try
            {
                string json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<BotConfig>(json, JsonOptions)
                             ?? throw new InvalidDataException("file contains 'null'");
                Console.WriteLine($"[Config] Loaded '{Path.GetFullPath(path)}'.");
                return config;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
            {
                throw new InvalidDataException($"Failed to load config '{Path.GetFullPath(path)}': {ex.Message}", ex);
            }
        }

        private static string? FindDefaultConfig()
        {
            string[] candidates =
            {
                Path.Combine(Directory.GetCurrentDirectory(), DefaultFileName),
                Path.Combine(AppContext.BaseDirectory, DefaultFileName)
            };
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        public void Save(string path = DefaultFileName)
        {
            try
            {
                string json = JsonSerializer.Serialize(this, JsonOptions);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config] Failed to save '{path}': {ex.Message}");
            }
        }
    }
}
