using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Example UI script — attach to a Canvas/UI element in VR.
/// Reads from HRReceiver and updates a TextMeshPro label + optional color feedback.
///
/// Requires HRReceiver to be present somewhere in the scene.
/// </summary>
public class HRDisplay : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text hrLabel;       // e.g. "83 BPM"
    [SerializeField] private TMP_Text zoneLabel;     // e.g. "UMJERENO"
    [SerializeField] private Image    zoneBg;        // optional background panel

    [Header("Zone Colors")]
    [SerializeField] private Color restColor       = new Color(0.25f, 0.67f, 1f);    // <60
    [SerializeField] private Color lightColor      = new Color(0.25f, 1f, 0.25f);    // 60-100
    [SerializeField] private Color moderateColor   = new Color(1f, 1f, 0.25f);       // 100-140
    [SerializeField] private Color intenseColor    = new Color(1f, 0.53f, 0.25f);    // 140-170
    [SerializeField] private Color maxColor        = new Color(1f, 0.25f, 0.25f);    // >170
    [SerializeField] private Color noSignalColor   = Color.gray;

    void OnEnable()  => HRReceiver.OnHRUpdated += OnHR;
    void OnDisable() => HRReceiver.OnHRUpdated -= OnHR;

    void Start()
    {
        UpdateDisplay(HRReceiver.CurrentHR);
    }

    private void OnHR(int hr) => UpdateDisplay(hr);

    private void UpdateDisplay(int hr)
    {
        if (hr <= 0)
        {
            if (hrLabel)   hrLabel.text  = "-- BPM";
            if (zoneLabel) zoneLabel.text = "NO SIGNAL";
            if (zoneBg)    zoneBg.color   = noSignalColor;
            return;
        }

        if (hrLabel) hrLabel.text = hr + " BPM";

        (string zone, Color color) = GetZone(hr);
        if (zoneLabel) zoneLabel.text = zone;
        if (zoneBg)    zoneBg.color   = new Color(color.r, color.g, color.b, 0.25f);
        if (hrLabel)   hrLabel.color  = color;
        if (zoneLabel) zoneLabel.color = color;
    }

    private (string, Color) GetZone(int hr)
    {
        if (hr < 60)  return ("ODMOR",      restColor);
        if (hr < 100) return ("LAGANO",     lightColor);
        if (hr < 140) return ("UMJERENO",   moderateColor);
        if (hr < 170) return ("INTENZIVNO", intenseColor);
        return ("MAKSIMALNO", maxColor);
    }
}
