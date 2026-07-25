using System;
using System.IO;
using StressTraining.Core;
using StressTraining.Data;
using UnityEngine;

namespace StressTraining.Persistence
{
    /// <summary>Persists questionnaires.json inside the session directory.</summary>
    public sealed class QuestionnaireRepository
    {
        private readonly PersistencePaths _paths;
        private readonly AppErrorService _errors;

        public QuestionnaireRepository(PersistencePaths paths, AppErrorService errors)
        {
            _paths = paths;
            _errors = errors;
        }

        public void Save(string userId, SessionQuestionnairesFile file)
        {
            try
            {
                AtomicFileWriter.Write(
                    _paths.QuestionnairesFile(userId, file.sessionId),
                    JsonUtility.ToJson(file, true));
            }
            catch (Exception ex)
            {
                _errors?.Report("QUESTIONNAIRE_SAVE_FAILED",
                    "Odgovori upitnika nijesu mogli biti sačuvani.",
                    ex.Message, true, ErrorSessionImpact.Degraded, ex);
            }
        }

        public SessionQuestionnairesFile Load(string userId, string sessionId)
        {
            try
            {
                string file = _paths.QuestionnairesFile(userId, sessionId);
                if (!File.Exists(file)) return null;
                return JsonUtility.FromJson<SessionQuestionnairesFile>(File.ReadAllText(file));
            }
            catch { return null; }
        }
    }
}
