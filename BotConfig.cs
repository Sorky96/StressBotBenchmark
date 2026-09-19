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

        public static BotConfig Load(string path = "config.json")
        {
            if (!File.Exists(path))
            {
                var defaultConfig = new BotConfig();
                defaultConfig.Save(path);
                return defaultConfig;
            }

            try
            {
                string json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<BotConfig>(json, JsonOptions);
                return config ?? new BotConfig();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config] Failed to load '{path}': {ex.Message}. Using default configuration.");
                return new BotConfig();
            }
        }

        public void Save(string path = "config.json")
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
