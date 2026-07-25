using System;
using System.IO;
using UnityEngine;

namespace StressTraining.Core
{
    /// <summary>
    /// Loads <see cref="StressTrainingConfig"/> from
    /// {persistentDataPath}/StressTrainingData/config/config.json when present,
    /// otherwise uses code defaults and writes them out once as a template.
    /// A corrupted config never crashes the app — defaults are used and the
    /// error is reported.
    /// </summary>
    public sealed class ConfigService
    {
        public StressTrainingConfig Config { get; private set; } = new StressTrainingConfig();
        public bool LoadedFromFile { get; private set; }

        private readonly string _configPath;
        private readonly AppErrorService _errors;

        public ConfigService(string configDirectory, AppErrorService errors)
        {
            _errors = errors;
            _configPath = Path.Combine(configDirectory, "config.json");
            Load(configDirectory);
        }

        private void Load(string configDirectory)
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath);
                    var cfg = JsonUtility.FromJson<StressTrainingConfig>(json);
                    if (cfg != null)
                    {
                        Config = cfg;
                        LoadedFromFile = true;
                        return;
                    }
                }
                // First run: persist defaults as an editable template.
                Directory.CreateDirectory(configDirectory);
                File.WriteAllText(_configPath, JsonUtility.ToJson(Config, prettyPrint: true));
            }
            catch (Exception ex)
            {
                Config = new StressTrainingConfig();
                _errors?.Report("CONFIG_LOAD_FAILED",
                    "Podešavanja nijesu mogla biti učitana — koriste se podrazumijevane vrijednosti.",
                    $"Failed to load {_configPath}", recoverable: true,
                    ErrorSessionImpact.None, ex);
            }
        }
    }
}
