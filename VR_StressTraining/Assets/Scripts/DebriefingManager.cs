using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Handles Phase 3: shows HR graph and session stats
public class DebriefingManager : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI timeText;
    public TextMeshProUGUI maxHRText;
    public TextMeshProUGUI avgHRText;
    public TextMeshProUGUI hintsText;

    [Header("HR Graph")]
    public LineRenderer hrGraph;
    public float graphWidth = 2.0f;
    public float graphHeight = 1.0f;

    private float sessionStartTime;
    private List<float> hrHistory = new List<float>();

    void Start()
    {
        sessionStartTime = Time.time;
        HRReceiver.OnHRUpdated += OnHRSample;
    }

    void OnDestroy()
    {
        HRReceiver.OnHRUpdated -= OnHRSample;
    }

    void OnHRSample(int hr)
    {
        hrHistory.Add(hr);
    }

    public void ShowDebriefing(float completionTime, int hintsUsed)
    {
        gameObject.SetActive(true);

        int maxHR = 0, totalHR = 0;
        foreach (float h in hrHistory)
        {
            if (h > maxHR) maxHR = (int)h;
            totalHR += (int)h;
        }
        int avgHR = hrHistory.Count > 0 ? totalHR / hrHistory.Count : 0;

        if (timeText)  timeText.text  = $"Completed in: {completionTime:F1}s";
        if (maxHRText) maxHRText.text = $"Max HR: {maxHR} bpm";
        if (avgHRText) avgHRText.text = $"Avg HR: {avgHR} bpm";
        if (hintsText) hintsText.text = $"Hints used: {hintsUsed}/{PuzzleManager.Instance?.maxHints}";

        DrawHRGraph();
    }

    void DrawHRGraph()
    {
        if (hrGraph == null || hrHistory.Count < 2) return;

        hrGraph.positionCount = hrHistory.Count;
        float minHR = 40f, maxHR = 220f;

        for (int i = 0; i < hrHistory.Count; i++)
        {
            float x = (i / (float)(hrHistory.Count - 1)) * graphWidth - graphWidth / 2f;
            float y = ((hrHistory[i] - minHR) / (maxHR - minHR)) * graphHeight;
            hrGraph.SetPosition(i, new Vector3(x, y, 0));
        }
    }
}
